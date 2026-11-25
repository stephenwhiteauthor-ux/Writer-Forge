using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WriterForge.Editor.Documents;
using WriterForge.Editor.Services;
using WriterForge.Editor.Windows;

namespace WriterForge.Editor.Controls
{
    /// <summary>
    /// The embeddable manuscript editor. Lives in a class library so
    /// WriterForge.App — and any future host application — can embed it.
    /// </summary>
    public partial class EditorControl : UserControl
    {
        private static readonly Regex WordPattern = new(@"[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*", RegexOptions.Compiled);

        private bool _suspendDirty;
        private bool _syncing;

        // Proofing (spell + grammar underlines, suggestions)
        private static readonly Regex ProofWordPattern = new(@"[\p{L}][\p{L}'’\-]*", RegexOptions.Compiled);

        private readonly DispatcherTimer _proofTimer;
        private readonly HashSet<string> _knownWords = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _unknownWords = new(StringComparer.OrdinalIgnoreCase);
        private string? _proofText;
        private string? _grammarCheckedText;
        private CancellationTokenSource? _grammarCts;

        /// <summary>Enable/disable live spell checking.</summary>
        public bool SpellCheckEnabled { get; set; } = true;

        /// <summary>Enable/disable live grammar checking.</summary>
        public bool GrammarCheckEnabled { get; set; } = true;

        /// <summary>Raised when the dirty state changes (text edited, loaded, or saved).</summary>
        public event EventHandler? DirtyChanged;

        /// <summary>True when the document has unsaved changes.</summary>
        public bool IsDirty { get; private set; }

        /// <summary>Metadata stored inside .wforge packages.</summary>
        public DocumentMetadata Metadata { get; private set; } = new();

        public EditorControl()
        {
            InitializeComponent();
            Document.TextChanged += Document_TextChanged;
            Document.SelectionChanged += Document_SelectionChanged;
            Document.PreviewMouseRightButtonDown += Document_PreviewMouseRightButtonDown;
            Loaded += (_, _) => { SyncToolbar(); UpdateStatus(); };

            _proofTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _proofTimer.Tick += (_, _) => { _proofTimer.Stop(); RunProofing(); };
        }

        /// <summary>Access to the underlying document for services (spell check, stats, export).</summary>
        public RichTextBox DocumentSurface => Document;

        // ---------- Document lifecycle ----------

        public void NewDocument()
        {
            _suspendDirty = true;
            try
            {
                Document.Document = new FlowDocument();
            }
            finally
            {
                _suspendDirty = false;
            }
            Metadata = new DocumentMetadata();
            SetDirty(false);
            ResetProofing();
        }

        public void LoadDocument(string path)
        {
            _suspendDirty = true;
            try
            {
                if (path.EndsWith(".wforge", StringComparison.OrdinalIgnoreCase))
                {
                    var (document, metadata) = WforgePackage.Load(path);
                    Document.Document = document;
                    Metadata = metadata;
                }
                else if (path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
                {
                    Document.Document = DocxService.Import(path);
                    ApplyImportedStyles();
                }
                else
                {
                    var range = new TextRange(Document.Document.ContentStart, Document.Document.ContentEnd);
                    var format = path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                        ? DataFormats.Text
                        : DataFormats.Rtf;
                    using var stream = File.OpenRead(path);
                    range.Load(stream, format);
                }
            }
            finally
            {
                _suspendDirty = false;
            }
            SetDirty(false);
            ResetProofing();
        }

        public void SaveDocument(string path)
        {
            if (path.EndsWith(".wforge", StringComparison.OrdinalIgnoreCase))
            {
                WforgePackage.Save(path, Document.Document, Metadata);
            }
            else if (path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            {
                DocxService.Export(path, Document.Document);
            }
            else
            {
                var range = new TextRange(Document.Document.ContentStart, Document.Document.ContentEnd);
                var format = path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                    ? DataFormats.Text
                    : DataFormats.Rtf;
                using var stream = File.Create(path);
                range.Save(stream, format);
            }
            SetDirty(false);
        }

        /// <summary>Exports the document to PDF. Does not change dirty state.</summary>
        public void ExportPdf(string path)
        {
            var title = !string.IsNullOrWhiteSpace(Metadata.Title)
                ? Metadata.Title
                : Path.GetFileNameWithoutExtension(path);
            PdfExportService.Export(Document.Document, path, title);
        }

        private void SetDirty(bool dirty)
        {
            if (IsDirty == dirty) return;
            IsDirty = dirty;
            DirtyChanged?.Invoke(this, EventArgs.Empty);
        }

        // ---------- Events ----------

        private void Document_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_suspendDirty) SetDirty(true);
            UpdateStatus();
            _proofTimer.Stop();
            _proofTimer.Start();
        }

        private void Document_SelectionChanged(object sender, RoutedEventArgs e)
        {
            SyncToolbar();
        }

        // ---------- Status ----------

        private void UpdateStatus()
        {
            var text = new TextRange(Document.Document.ContentStart, Document.Document.ContentEnd).Text;
            var words = WordPattern.Matches(text).Count;
            StatusText.Text = $"Words: {words}    Characters: {text.Length}    Paragraphs: {Document.Document.Blocks.Count}";
        }

        // ---------- Toolbar sync ----------

        private void SyncToolbar()
        {
            if (_syncing) return;
            var sel = Document.Selection;

            _syncing = true;
            try
            {
                UndoButton.IsEnabled = Document.CanUndo;
                RedoButton.IsEnabled = Document.CanRedo;

                BoldButton.IsChecked = GetValue(sel, TextElement.FontWeightProperty) is FontWeight w && w == FontWeights.Bold;
                ItalicButton.IsChecked = GetValue(sel, TextElement.FontStyleProperty) is FontStyle s && s == FontStyles.Italic;
                UnderlineButton.IsChecked = HasDecoration(sel, TextDecorationLocation.Underline);
                StrikeButton.IsChecked = HasDecoration(sel, TextDecorationLocation.Strikethrough);
                SubButton.IsChecked = GetValue(sel, Inline.BaselineAlignmentProperty) is BaselineAlignment b && b == BaselineAlignment.Subscript;
                SupButton.IsChecked = GetValue(sel, Inline.BaselineAlignmentProperty) is BaselineAlignment b2 && b2 == BaselineAlignment.Superscript;

                SyncComboBox(FontBox, GetValue(sel, TextElement.FontFamilyProperty) as FontFamily);
                SyncComboBox(SizeBox, GetValue(sel, TextElement.FontSizeProperty));
                SyncStyleBox(sel);
                SyncAlignButtons(sel);
                SyncListButtons(sel);
            }
            finally
            {
                _syncing = false;
            }
        }

        private static object? GetValue(TextRange sel, DependencyProperty property)
        {
            var value = sel.GetPropertyValue(property);
            return ReferenceEquals(value, DependencyProperty.UnsetValue) ? null : value;
        }

        private static bool HasDecoration(TextRange sel, TextDecorationLocation location)
        {
            if (sel.GetPropertyValue(Inline.TextDecorationsProperty) is not TextDecorationCollection decorations)
                return false;
            foreach (var d in decorations)
                if (d.Location == location)
                    return true;
            return false;
        }

        private void SyncComboBox(ComboBox box, object? value)
        {
            foreach (ComboBoxItem item in box.Items)
            {
                var match = value switch
                {
                    FontFamily f => string.Equals(item.Tag as string, f.Source, StringComparison.OrdinalIgnoreCase),
                    double size => double.TryParse(item.Tag as string, out var s) && Math.Abs(s - size) < 0.01,
                    _ => false,
                };
                item.IsSelected = match;
            }
        }

        private void SyncStyleBox(TextRange sel)
        {
            var style = sel.Start.Paragraph?.Style;
            foreach (ComboBoxItem item in StyleBox.Items)
                item.IsSelected = ReferenceEquals(item.Tag is string key ? FindResource(key) : null, style);
        }

        private void SyncAlignButtons(TextRange sel)
        {
            var align = sel.Start.Paragraph?.TextAlignment ?? TextAlignment.Left;
            foreach (var btn in new[] { AlignLeftButton, AlignCenterButton, AlignRightButton, AlignJustifyButton })
                btn.IsChecked = string.Equals(btn.Tag as string, align.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        // ---------- Toolbar actions ----------

        private void ApplyProperty(DependencyProperty property, object? value)
        {
            Document.Selection.ApplyPropertyValue(property, value);
        }

        private void UndoClick(object sender, RoutedEventArgs e)
        {
            if (Document.CanUndo) Document.Undo();
            SyncToolbar();
        }

        private void RedoClick(object sender, RoutedEventArgs e)
        {
            if (Document.CanRedo) Document.Redo();
            SyncToolbar();
        }

        private void StyleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || StyleBox.SelectedItem is not ComboBoxItem item || item.Tag is not string key)
                return;
            ApplyParagraphStyle(key);
            MacroRecorder.Record("Style", key);
        }

        private void ApplyParagraphStyle(string key)
        {
            if (FindResource(key) is Style style)
            {
                ApplyProperty(Paragraph.StyleProperty, style);
                ApplyProperty(EditorTags.StyleKeyProperty, key);
            }
        }

        /// <summary>After a .docx import, resolve style tags to the built-in style objects.</summary>
        private void ApplyImportedStyles()
        {
            foreach (var paragraph in BlockTree.AllParagraphs(Document.Document))
            {
                var key = EditorTags.GetStyleKey(paragraph);
                if (key is not null && FindResource(key) is Style style)
                    paragraph.Style = style;
            }
        }

        private void FontBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || FontBox.SelectedItem is not ComboBoxItem item || item.Tag is not string name)
                return;
            ApplyFontName(name);
            MacroRecorder.Record("Font", name);
        }

        private void ApplyFontName(string name)
        {
            var family = new FontFamily(name);
            ApplyProperty(TextElement.FontFamilyProperty, family);
            if (Document.Selection.IsEmpty) Document.FontFamily = family;
        }

        private void SizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || SizeBox.SelectedItem is not ComboBoxItem item || item.Tag is not string text)
                return;
            if (!double.TryParse(text, out var size)) return;
            ApplyFontSize(size);
            MacroRecorder.Record("Size", text);
        }

        private void ApplyFontSize(double size)
        {
            ApplyProperty(TextElement.FontSizeProperty, size);
            if (Document.Selection.IsEmpty) Document.FontSize = size;
        }

        private void ToggleFontWeight()
        {
            var current = GetValue(Document.Selection, TextElement.FontWeightProperty);
            var weight = current is FontWeight w && w == FontWeights.Bold ? FontWeights.Normal : FontWeights.Bold;
            ApplyProperty(TextElement.FontWeightProperty, weight);
        }

        private void BoldClick(object sender, RoutedEventArgs e) { ToggleFontWeight(); SyncToolbar(); MacroRecorder.Record("Bold"); }

        private void ToggleFontStyle()
        {
            var current = GetValue(Document.Selection, TextElement.FontStyleProperty);
            var style = current is FontStyle s && s == FontStyles.Italic ? FontStyles.Normal : FontStyles.Italic;
            ApplyProperty(TextElement.FontStyleProperty, style);
        }

        private void ItalicClick(object sender, RoutedEventArgs e) { ToggleFontStyle(); SyncToolbar(); MacroRecorder.Record("Italic"); }

        private void ToggleDecoration(TextDecorationLocation location)
        {
            var has = HasDecoration(Document.Selection, location);
            var decorations = new TextDecorationCollection();

            if (Document.Selection.GetPropertyValue(Inline.TextDecorationsProperty) is TextDecorationCollection current)
            {
                foreach (var d in current)
                    if (d.Location != location)
                        decorations.Add(d);
            }
            if (!has)
            {
                var add = location == TextDecorationLocation.Underline ? TextDecorations.Underline : TextDecorations.Strikethrough;
                foreach (var d in add)
                    decorations.Add(d);
            }
            ApplyProperty(Inline.TextDecorationsProperty, decorations.Count > 0 ? decorations : null);
        }

        private void UnderlineClick(object sender, RoutedEventArgs e) { ToggleDecoration(TextDecorationLocation.Underline); SyncToolbar(); MacroRecorder.Record("Underline"); }

        private void StrikeClick(object sender, RoutedEventArgs e) { ToggleDecoration(TextDecorationLocation.Strikethrough); SyncToolbar(); MacroRecorder.Record("Strike"); }

        private void ToggleBaseline(BaselineAlignment target, BaselineAlignment off)
        {
            var current = GetValue(Document.Selection, Inline.BaselineAlignmentProperty);
            var value = current is BaselineAlignment b && b == target ? off : target;
            ApplyProperty(Inline.BaselineAlignmentProperty, value);
        }

        private void SubClick(object sender, RoutedEventArgs e) { ToggleBaseline(BaselineAlignment.Subscript, BaselineAlignment.Baseline); SyncToolbar(); MacroRecorder.Record("Subscript"); }

        private void SupClick(object sender, RoutedEventArgs e) { ToggleBaseline(BaselineAlignment.Superscript, BaselineAlignment.Baseline); SyncToolbar(); MacroRecorder.Record("Superscript"); }

        private void AlignClick(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton { Tag: string tag }) return;
            if (!Enum.TryParse<TextAlignment>(tag, true, out var align)) return;
            ApplyProperty(Paragraph.TextAlignmentProperty, align);
            SyncToolbar();
            MacroRecorder.Record("Align", tag);
        }

        // ---------- Colour ----------

        private static readonly (string Name, string Hex)[] FontColorPalette =
        {
            ("Dark Red", "#C00000"), ("Dark Green", "#00703C"), ("Dark Blue", "#1F4E9C"),
            ("Purple", "#5B2C8A"), ("Orange", "#C55A11"), ("Gray", "#595959"), ("Black", "#000000"),
        };

        private static readonly (string Name, string Hex)[] HighlightPalette =
        {
            ("Yellow", "#FFF2A8"), ("Light Green", "#C6EFCE"), ("Light Blue", "#DDEBF7"),
            ("Pink", "#FCE4EC"), ("Orange", "#FBE5D6"), ("Gray", "#D9D9D9"),
        };

        private void FontColorClick(object sender, RoutedEventArgs e) => ShowColorMenu(FontColorButton, true);

        private void HighlightClick(object sender, RoutedEventArgs e) => ShowColorMenu(HighlightButton, false);

        private void ShowColorMenu(FrameworkElement target, bool foreground)
        {
            var popup = new Popup
            {
                PlacementTarget = target,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
            };
            var wrap = new WrapPanel { Margin = new Thickness(4) };
            foreach (var (name, hex) in foreground ? FontColorPalette : HighlightPalette)
            {
                var h = hex;
                var swatch = new Border
                {
                    Width = 20,
                    Height = 20,
                    Margin = new Thickness(1),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(h)),
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(0.5),
                    Cursor = Cursors.Hand,
                    ToolTip = name,
                };
                swatch.MouseLeftButtonDown += (_, _) =>
                {
                    ApplyColour(h, foreground);
                    popup.IsOpen = false;
                };
                wrap.Children.Add(swatch);
            }
            popup.Child = new Border
            {
                BorderBrush = SystemColors.ControlDarkBrush,
                BorderThickness = new Thickness(1),
                Background = SystemColors.WindowBrush,
                Child = wrap,
            };
            popup.IsOpen = true;
        }

        private void ApplyColour(string hex, bool foreground)
        {
            var property = foreground ? TextElement.ForegroundProperty : TextElement.BackgroundProperty;
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            ApplyProperty(property, brush);
            MacroRecorder.Record(foreground ? "Color" : "Highlight", hex);
        }

        // ---------- Line spacing, paragraph spacing, indent ----------

        private void SpacingBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || SpacingBox.SelectedItem is not ComboBoxItem { Tag: string tag })
                return;
            PlaySpacing(tag);
            MacroRecorder.Record("Spacing", tag);
        }

        private void PlaySpacing(string tag)
        {
            if (!double.TryParse(tag, out var spacing)) return;
            var size = GetValue(Document.Selection, TextElement.FontSizeProperty) is double s && !double.IsNaN(s)
                ? s
                : Document.FontSize;
            var family = GetValue(Document.Selection, TextElement.FontFamilyProperty) as FontFamily ?? Document.FontFamily;
            var lineHeight = size * (family?.LineSpacing ?? 1.2) * spacing;
            foreach (var p in SelectedParagraphs())
                p.LineHeight = lineHeight;
        }

        private void BeforeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || BeforeBox.SelectedItem is not ComboBoxItem { Tag: string tag })
                return;
            PlayParaSpacing(true, tag);
            MacroRecorder.Record("SpaceBefore", tag);
        }

        private void AfterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || AfterBox.SelectedItem is not ComboBoxItem { Tag: string tag })
                return;
            PlayParaSpacing(false, tag);
            MacroRecorder.Record("SpaceAfter", tag);
        }

        private void PlayParaSpacing(bool before, string tag)
        {
            const double dipPerPoint = 96.0 / 72.0;
            double? points = double.TryParse(tag, out var pt) ? pt : null;
            foreach (var p in SelectedParagraphs())
            {
                if (points is null)
                {
                    p.Margin = new Thickness(double.NaN);
                    continue;
                }
                var m = EffectiveMargin(p);
                var value = points.Value * dipPerPoint;
                p.Margin = before
                    ? new Thickness(m.Left, value, m.Right, m.Bottom)
                    : new Thickness(m.Left, m.Top, m.Right, value);
            }
        }

        private void IndentClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string tag } || !double.TryParse(tag, out var delta))
                return;
            PlayIndent(delta);
            MacroRecorder.Record("Indent", tag);
        }

        private void PlayIndent(double delta)
        {
            foreach (var p in SelectedParagraphs())
            {
                var m = EffectiveMargin(p);
                p.Margin = new Thickness(Math.Max(0, m.Left + delta), m.Top, m.Right, m.Bottom);
            }
        }

        private List<Paragraph> SelectedParagraphs()
        {
            var sel = Document.Selection;
            return BlockTree.AllParagraphs(Document.Document)
                .Where(p => p.ContentStart.CompareTo(sel.End) <= 0 && p.ContentEnd.CompareTo(sel.Start) >= 0)
                .ToList();
        }

        private static Thickness EffectiveMargin(Paragraph p)
        {
            var m = p.Margin;
            var auto = p.FontSize * (p.FontFamily?.LineSpacing ?? 1.2);
            return new Thickness(
                double.IsNaN(m.Left) ? 0 : m.Left,
                double.IsNaN(m.Top) ? auto : m.Top,
                double.IsNaN(m.Right) ? 0 : m.Right,
                double.IsNaN(m.Bottom) ? auto : m.Bottom);
        }

        private void InsertImageClick(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Insert Image",
                Filter = "Images (*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp",
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true)
                InsertImage(dialog.FileName);
        }

        /// <summary>Insert an image file at the caret as an inline element.</summary>
        public void InsertImage(string path)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                var image = new Image
                {
                    Source = bitmap,
                    MaxWidth = 480,
                    Stretch = Stretch.Uniform,
                };
                image.ContextMenu = BuildImageMenu(image, bitmap);
                InsertInlineAtCaret(new InlineUIContainer(image));
            }
            catch
            {
                // Unreadable/unsupported image — ignore.
            }
        }

        private static ContextMenu BuildImageMenu(Image image, BitmapImage bitmap)
        {
            var menu = new ContextMenu();
            void Add(string header, double? factor)
            {
                var item = new MenuItem { Header = header };
                item.Click += (_, _) =>
                {
                    // factor == null restores the natural size.
                    image.Width = factor is null ? double.NaN : bitmap.Width * factor.Value;
                };
                menu.Items.Add(item);
            }
            Add($"Original size ({bitmap.Width:F0} px)", null);
            Add("75% width", 0.75);
            Add("50% width", 0.5);
            Add("25% width", 0.25);
            return menu;
        }

        private void InsertInlineAtCaret(InlineUIContainer container)
        {
            var caret = Document.CaretPosition;
            var paragraph = caret.Paragraph;
            if (paragraph is null)
            {
                paragraph = new Paragraph();
                Document.Document.Blocks.Add(paragraph);
            }

            var offset = paragraph.ContentStart.GetOffsetToPosition(caret);
            var acc = 0;
            Inline? before = null;
            Inline? after = null;
            foreach (var inline in paragraph.Inlines)
            {
                var len = InlineLength(inline);
                if (offset < acc + len)
                {
                    after = inline;
                    break;
                }
                before = inline;
                acc += len;
            }

            if (after is not null) paragraph.Inlines.InsertBefore(after, container);
            else if (before is not null) paragraph.Inlines.InsertAfter(before, container);
            else paragraph.Inlines.Add(container);
            Document.CaretPosition = paragraph.ContentEnd;
        }

        private static int InlineLength(Inline inline) => inline switch
        {
            Run run => run.Text.Length,
            LineBreak => 1,
            InlineUIContainer => 1,
            Span span => span.Inlines.Sum(InlineLength),
            _ => 0,
        };

        private void ClearFormattingClick(object sender, RoutedEventArgs e)
        {
            ClearAllFormatting();
            SyncToolbar();
            MacroRecorder.Record("Clear");
        }

        private void ClearAllFormatting()
        {
            var sel = Document.Selection;
            foreach (var prop in new[]
            {
                TextElement.FontWeightProperty,
                TextElement.FontStyleProperty,
                TextElement.FontSizeProperty,
                TextElement.FontFamilyProperty,
                Inline.TextDecorationsProperty,
                Inline.BaselineAlignmentProperty,
            })
            {
                sel.ApplyPropertyValue(prop, DependencyProperty.UnsetValue);
            }
        }

        // ---------- Macro replay ----------

        /// <summary>Replay a recorded sequence of editor actions.</summary>
        public void PlayMacro(IEnumerable<MacroAction> actions)
        {
            foreach (var a in actions)
            {
                switch (a.Name)
                {
                    case "Bold": ToggleFontWeight(); break;
                    case "Italic": ToggleFontStyle(); break;
                    case "Underline": ToggleDecoration(TextDecorationLocation.Underline); break;
                    case "Strike": ToggleDecoration(TextDecorationLocation.Strikethrough); break;
                    case "Subscript": ToggleBaseline(BaselineAlignment.Subscript, BaselineAlignment.Baseline); break;
                    case "Superscript": ToggleBaseline(BaselineAlignment.Superscript, BaselineAlignment.Baseline); break;
                    case "Bullets": ToggleList(TextMarkerStyle.Disc); break;
                    case "Numbers": ToggleList(TextMarkerStyle.Decimal); break;
                    case "Clear": ClearAllFormatting(); break;
                    case "Style" when a.Arg is not null: ApplyParagraphStyle(a.Arg); break;
                    case "Font" when a.Arg is not null: ApplyFontName(a.Arg); break;
                    case "Size" when a.Arg is not null && double.TryParse(a.Arg, out var size): ApplyFontSize(size); break;
                    case "Align" when a.Arg is not null && Enum.TryParse<TextAlignment>(a.Arg, true, out var align):
                        ApplyProperty(Paragraph.TextAlignmentProperty, align);
                        break;
                    case "Color" when a.Arg is not null: ApplyRecordedBrush(TextElement.ForegroundProperty, a.Arg); break;
                    case "Highlight" when a.Arg is not null: ApplyRecordedBrush(TextElement.BackgroundProperty, a.Arg); break;
                    case "Spacing": PlaySpacing(a.Arg ?? "1"); break;
                    case "SpaceBefore": PlayParaSpacing(true, a.Arg ?? ""); break;
                    case "SpaceAfter": PlayParaSpacing(false, a.Arg ?? ""); break;
                    case "Indent" when a.Arg is not null && double.TryParse(a.Arg, out var ind): PlayIndent(ind); break;
                    case "SmartQuotes": RunTypographyTool(TypographyTools.SmartQuotes); break;
                    case "Ellipsis": RunTypographyTool(TypographyTools.NormalizeEllipsis); break;
                    case "Whitespace": RunTypographyTool(TypographyTools.CleanWhitespace); break;
                    case "EmptyParagraphs": RunTypographyTool(TypographyTools.RemoveEmptyParagraphs); break;
                    case "TypographyAll": RunTypographyTool(TypographyTools.RunAll); break;
                }
            }
            SyncToolbar();
        }

        private void ApplyRecordedBrush(DependencyProperty property, string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            ApplyProperty(property, brush);
        }

        // ---------- Services: stats, typography, find/replace ----------

        /// <summary>Show the document statistics dialog.</summary>
        public void ShowStatistics()
        {
            var stats = DocumentStatsService.Analyze(Document.Document);
            var owner = Window.GetWindow(this);
            new DocumentStatsWindow(stats) { Owner = owner }.ShowDialog();
        }

        /// <summary>Run a typography cleanup tool; returns the number of changes made.</summary>
        private int RunTypographyTool(Func<FlowDocument, int> tool)
        {
            var changes = tool(Document.Document);
            if (changes > 0) SetDirty(true);
            return changes;
        }

        public int SmartQuotes() => RunTypographyTool(TypographyTools.SmartQuotes);
        public int NormalizeEllipsis() => RunTypographyTool(TypographyTools.NormalizeEllipsis);
        public int CleanWhitespace() => RunTypographyTool(TypographyTools.CleanWhitespace);
        public int RemoveEmptyParagraphs() => RunTypographyTool(TypographyTools.RemoveEmptyParagraphs);
        public int RunAllTypography() => RunTypographyTool(TypographyTools.RunAll);

        // ---------- Writing analysis ----------

        private static readonly SolidColorBrush AnalysisBrush = CreateFrozenBrush(0xFF, 0xEB, 0x3B);
        private readonly List<TextRange> _analysisHighlights = new();

        private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        /// <summary>Apply page size and margins to the document.</summary>
        public void ApplyPageSetup(Size pageSize, Thickness margins)
        {
            _suspendDirty = true;
            try
            {
                Document.Document.PageWidth = pageSize.Width;
                Document.Document.PageHeight = pageSize.Height;
                Document.Document.PagePadding = margins;
            }
            finally
            {
                _suspendDirty = false;
            }
            SetDirty(true);
        }

        /// <summary>Run the writing-analysis pass over the document.</summary>
        public AnalysisReport AnalyzeDocument() => WriterAnalysisService.Analyze(Document.Document);

        /// <summary>Highlight the given findings in the document (replaces any previous highlights).</summary>
        public void HighlightFindings(IEnumerable<AnalysisFinding> findings)
        {
            ClearAnalysisHighlights();
            var map = DocumentTextMap.Build(Document.Document);
            foreach (var f in findings)
            {
                var range = map.RangeForSpan(f.Offset, f.Length);
                if (range is null) continue;
                range.ApplyPropertyValue(TextElement.BackgroundProperty, AnalysisBrush);
                _analysisHighlights.Add(range);
            }
        }

        /// <summary>Remove all analysis highlights.</summary>
        public void ClearAnalysisHighlights()
        {
            foreach (var range in _analysisHighlights)
                range.ApplyPropertyValue(TextElement.BackgroundProperty, DependencyProperty.UnsetValue);
            _analysisHighlights.Clear();
        }

        private FindReplaceDialog? _findDialog;

        /// <summary>Apply spell/grammar check toggles from user settings.</summary>
        public void ApplyProofingSettings(bool spellCheck, bool grammarCheck)
        {
            SpellCheckEnabled = spellCheck;
            GrammarCheckEnabled = grammarCheck;
            if (!spellCheck) ClearProofDecorations(ProofDecorations.SpellBrush);
            if (!grammarCheck)
            {
                ClearProofDecorations(ProofDecorations.GrammarBrush);
                _grammarIssues = new List<GrammarIssue>();
                _grammarCheckedText = null;
                _grammarCts?.Cancel();
            }
            if (SpellCheckEnabled || GrammarCheckEnabled)
            {
                _proofText = null;
                _proofTimer.Start();
            }
            else
            {
                _proofTimer.Stop();
            }
        }

        /// <summary>Show the find/replace dialog (reuses a single instance).</summary>
        public void ShowFindReplace()
        {
            if (_findDialog is null)
            {
                _findDialog = new FindReplaceDialog(Document);
                _findDialog.Closed += (_, _) => _findDialog = null;
            }
            _findDialog.Show();
            _findDialog.Activate();
        }

        // ---------- Proofing: spell + grammar underlines ----------

        private void ResetProofing()
        {
            _knownWords.Clear();
            _unknownWords.Clear();
            _proofText = null;
            _grammarCheckedText = null;
            _grammarCts?.Cancel();
            _proofTimer.Stop();
        }

        private void RunProofing()
        {
            if (!SpellCheckEnabled && !GrammarCheckEnabled) return;

            DocumentTextMap map;
            try
            {
                map = DocumentTextMap.Build(Document.Document);
            }
            catch
            {
                return;
            }

            if (map.Text == _proofText) return;
            _proofText = map.Text;

            if (SpellCheckEnabled) CheckSpelling();
            if (GrammarCheckEnabled) QueueGrammarCheck(map);
        }

        private bool IsKnownWord(string word)
        {
            if (_knownWords.Contains(word)) return true;
            if (_unknownWords.Contains(word)) return false;

            var known = SpellCheckService.Instance.Check(word);
            (_ = known ? _knownWords : _unknownWords).Add(word);
            return known;
        }

        private void CheckSpelling()
        {
            var checker = SpellCheckService.Instance;
            ClearProofDecorations(ProofDecorations.SpellBrush);
            if (!checker.IsAvailable) return;

            foreach (var run in DocumentTextMap.EnumerateRuns(Document.Document))
            {
                var text = run.Text;
                if (string.IsNullOrEmpty(text)) continue;

                foreach (Match match in ProofWordPattern.Matches(text))
                {
                    var word = match.Value;
                    if (word.Length < 2) continue;
                    if (word.Any(char.IsDigit)) continue;
                    if (word.All(c => !char.IsLetter(c) || char.IsUpper(c))) continue;
                    if (IsKnownWord(word)) continue;

                    var range = RangeWithinRun(run, match.Index, match.Length);
                    if (range is not null) AddProofUnderline(range, ProofDecorations.SpellBrush);
                }
            }
        }

        private void QueueGrammarCheck(DocumentTextMap map)
        {
            var text = map.Text;
            if (text.Trim().Length < 20)
            {
                _grammarCheckedText = text;
                _grammarIssues = new List<GrammarIssue>();
                ClearProofDecorations(ProofDecorations.GrammarBrush);
                return;
            }
            if (!GrammarCheckService.IsAvailable) return;
            if (string.Equals(text, _grammarCheckedText, StringComparison.Ordinal)) return;

            _grammarCts?.Cancel();
            var cts = new CancellationTokenSource();
            _grammarCts = cts;

            Task.Run(() => GrammarCheckService.Check(text)).ContinueWith(task =>
            {
                if (cts.IsCancellationRequested || task.IsCanceled || task.IsFaulted) return;
                var issues = task.Result;

                Dispatcher.BeginInvoke(() =>
                {
                    if (cts.IsCancellationRequested) return;
                    if (_proofText != text) return; // document changed since the check started
                    _grammarCheckedText = text;
                    ApplyGrammarUnderlines(issues, map);
                });
            });
        }

        private void ApplyGrammarUnderlines(List<GrammarIssue> issues, DocumentTextMap map)
        {
            _grammarIssues = issues;
            ClearProofDecorations(ProofDecorations.GrammarBrush);
            foreach (var issue in issues)
            {
                var range = map.RangeForOffset(issue.Offset, issue.Length);
                if (range is not null) AddProofUnderline(range, ProofDecorations.GrammarBrush);
            }
        }

        private static TextRange? RangeWithinRun(Run run, int start, int length)
        {
            var s = run.ContentStart.GetPositionAtOffset(start);
            var e = run.ContentStart.GetPositionAtOffset(start + length);
            return s is null || e is null ? null : new TextRange(s, e);
        }

        private static void AddProofUnderline(TextRange range, SolidColorBrush brush)
        {
            var decorations = new TextDecorationCollection();
            if (range.GetPropertyValue(Inline.TextDecorationsProperty) is TextDecorationCollection existing)
            {
                foreach (var d in existing)
                    if (!ProofDecorations.IsProof(d, ProofDecorations.SpellBrush) && !ProofDecorations.IsProof(d, ProofDecorations.GrammarBrush))
                        decorations.Add(d);
            }
            decorations.Add(new TextDecoration { Pen = new Pen(brush, 1.2) });
            range.ApplyPropertyValue(Inline.TextDecorationsProperty, decorations);
        }

        private void ClearProofDecorations(SolidColorBrush brush)
        {
            foreach (var run in DocumentTextMap.EnumerateRuns(Document.Document))
            {
                if (run.GetValue(Inline.TextDecorationsProperty) is not TextDecorationCollection decorations)
                    continue;

                TextDecorationCollection? kept = null;
                foreach (var d in decorations)
                {
                    if (ProofDecorations.IsProof(d, brush)) continue;
                    kept ??= new TextDecorationCollection();
                    kept.Add(d);
                }
                if (HasProofDecoration(decorations, brush))
                    run.SetValue(Inline.TextDecorationsProperty, kept);
            }
        }

        private static bool HasProofDecoration(TextDecorationCollection decorations, SolidColorBrush brush)
        {
            foreach (var d in decorations)
                if (ProofDecorations.IsProof(d, brush)) return true;
            return false;
        }

        // ---------- Lists ----------

        private void SyncListButtons(TextRange sel)
        {
            var list = sel.Start.Paragraph is { } p ? BlockTree.ParentList(p) : null;
            BulletsButton.IsChecked = list?.MarkerStyle == TextMarkerStyle.Disc;
            NumbersButton.IsChecked = list?.MarkerStyle == TextMarkerStyle.Decimal;
        }

        private void BulletsClick(object sender, RoutedEventArgs e) { ToggleList(TextMarkerStyle.Disc); MacroRecorder.Record("Bullets"); }

        private void NumbersClick(object sender, RoutedEventArgs e) { ToggleList(TextMarkerStyle.Decimal); MacroRecorder.Record("Numbers"); }

        private void ToggleList(TextMarkerStyle markerStyle)
        {
            var sel = Document.Selection;
            var selected = BlockTree.AllParagraphs(Document.Document)
                .Where(p => p.ContentStart.CompareTo(sel.End) <= 0 && p.ContentEnd.CompareTo(sel.Start) >= 0)
                .ToList();
            if (selected.Count == 0) return;

            // If everything selected is already in a list with this marker style, unwrap it.
            var allInSameList = selected.All(p =>
            {
                var l = BlockTree.ParentList(p);
                return l is not null && l.MarkerStyle == markerStyle;
            }) && selected.Select(BlockTree.ParentList).Distinct().Count() == 1;

            if (allInSameList)
            {
                UnwrapListItems(selected);
            }
            else
            {
                // Normalise: pull any selected list items out first, then wrap.
                UnwrapListItems(selected.Where(p => BlockTree.ParentList(p) is not null).ToList());
                WrapInList(selected, markerStyle);
            }
            SyncToolbar();
        }

        private void UnwrapListItems(List<Paragraph> paragraphs)
        {
            var lastInserted = new Dictionary<List, Block>();
            foreach (var p in paragraphs)
            {
                var list = BlockTree.ParentList(p);
                if (list is null || p.Parent is not ListItem item) continue;
                var parentBlocks = ParentBlocksOf(list);
                if (parentBlocks is null) continue;

                var anchor = lastInserted.TryGetValue(list, out var last) ? last : list;
                item.Blocks.Remove(p);
                parentBlocks.InsertAfter(anchor, p);
                lastInserted[list] = p;

                if (item.Blocks.Count == 0) list.ListItems.Remove(item);
                if (list.ListItems.Count == 0) parentBlocks.Remove(list);
            }
        }

        private void WrapInList(List<Paragraph> paragraphs, TextMarkerStyle markerStyle)
        {
            foreach (var group in paragraphs.GroupBy(ParentBlocksOf))
            {
                var blocks = group.Key;
                if (blocks is null) continue;
                var items = group.ToList();

                var list = new List { MarkerStyle = markerStyle };
                blocks.InsertBefore(items[0], list);
                foreach (var p in items)
                {
                    blocks.Remove(p);
                    var item = new ListItem();
                    item.Blocks.Add(p);
                    list.ListItems.Add(item);
                }
            }
        }

        private static BlockCollection? ParentBlocksOf(Block block) =>
            block.Parent switch
            {
                Section section => section.Blocks,
                FlowDocument document => document.Blocks,
                ListItem item => item.Blocks,
                _ => null,
            };

        // ---------- Right-click suggestions ----------

        private void Document_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var point = e.GetPosition(Document);
            var pointer = Document.GetPositionFromPoint(point, true);
            if (pointer is null) return;

            var run = pointer.Parent as Run;
            if (run is null || run.Text.Length == 0) return;

            var local = run.ContentStart.GetOffsetToPosition(pointer);
            if (local < 0 || local > run.Text.Length) return;

            var (start, end, word) = WordBoundsAt(run.Text, local);
            if (word.Length == 0) return;

            var map = DocumentTextMap.Build(Document.Document);
            var globalOffset = map.OffsetOf(run, start);

            var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
            var added = false;

            var misspelled = word.Length >= 2
                && !word.Any(char.IsDigit)
                && !word.All(c => !char.IsLetter(c) || char.IsUpper(c))
                && !IsKnownWord(word);

            if (misspelled)
            {
                var suggestions = SpellCheckService.Instance.Suggest(word);
                foreach (var suggestion in suggestions.Take(8))
                {
                    var replacement = suggestion;
                    menu.Items.Add(ActionItem(replacement, () => ReplaceWord(run, start, end, replacement)));
                }
                if (suggestions.Count == 0)
                    menu.Items.Add(new MenuItem { Header = "(no suggestions)", IsEnabled = false });

                menu.Items.Add(new Separator());
                menu.Items.Add(ActionItem("Add to dictionary", () => { SpellCheckService.Instance.AddToDictionary(word); RecheckWord(word); }));
                menu.Items.Add(ActionItem("Ignore", () => { SpellCheckService.Instance.Ignore(word); RecheckWord(word); }));
                added = true;
            }

            var issue = FindGrammarIssueAt(map, globalOffset);

            if (issue is not null)
            {
                if (added) menu.Items.Add(new Separator());
                menu.Items.Add(new MenuItem { Header = issue.Message ?? "Grammar suggestion", IsEnabled = false });
                foreach (var replacement in issue.Replacements.Take(6))
                {
                    var repl = replacement;
                    menu.Items.Add(ActionItem($"Replace with \"{repl}\"", () =>
                    {
                        var range = map.RangeForOffset(issue.Offset, issue.Length);
                        if (range is not null) range.Text = repl;
                    }));
                }
                added = true;
            }

            if (!added) return;
            e.Handled = true;
            menu.IsOpen = true;
        }

        private List<GrammarIssue>? _grammarIssues;

        private static MenuItem ActionItem(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            return item;
        }

        private GrammarIssue? FindGrammarIssueAt(DocumentTextMap map, int globalOffset)
        {
            if (_grammarIssues is null || globalOffset < 0) return null;
            return _grammarIssues.FirstOrDefault(i =>
                globalOffset >= i.Offset && globalOffset < i.Offset + Math.Max(i.Length, 1));
        }

        private static (int Start, int End, string Word) WordBoundsAt(string text, int index)
        {
            static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c is '\'' or '’' or '-' or '‐';
            var start = index;
            var end = index;
            while (start > 0 && IsWordChar(text[start - 1])) start--;
            while (end < text.Length && IsWordChar(text[end])) end++;
            return (start, end, text[start..end]);
        }

        private void ReplaceWord(Run run, int start, int end, string replacement)
        {
            var s = run.ContentStart.GetPositionAtOffset(start);
            var e2 = run.ContentStart.GetPositionAtOffset(end);
            if (s is null || e2 is null) return;
            new TextRange(s, e2).Text = replacement;
        }

        private void RecheckWord(string word)
        {
            _knownWords.Remove(word);
            _unknownWords.Remove(word);
            _proofTimer.Stop();
            _proofTimer.Start();
        }
    }
}
