using System.Globalization;
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
        private int _customStyleCounter;
        private readonly List<CustomStyleRecord> _customStyles = new();

        // Proofing (spell + grammar underlines, suggestions)
        private static readonly Regex ProofWordPattern = new(@"[\p{L}][\p{L}'’\-]*", RegexOptions.Compiled);

        private readonly DispatcherTimer _proofTimer;
        private readonly DispatcherTimer _pageTimer;
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

        /// <summary>Raised when the formatting-state snapshot changes (caret/selection moved).</summary>
        public event EventHandler<EditorToolbarState>? ToolbarStateChanged;

        /// <summary>Raised when the set of paragraph styles changes (custom style added).</summary>
        public event EventHandler? ParagraphStylesChanged;

        /// <summary>True when the document has unsaved changes.</summary>
        public bool IsDirty { get; private set; }

        /// <summary>Metadata stored inside .wforge packages.</summary>
        public DocumentMetadata Metadata { get; private set; } = new();

        /// <summary>Current word count of the document.</summary>
        public int WordCount =>
            WordPattern.Matches(new TextRange(Document.Document.ContentStart, Document.Document.ContentEnd).Text).Count;

        public EditorControl()
        {
            InitializeComponent();
            Document.TextChanged += Document_TextChanged;
            Document.SelectionChanged += Document_SelectionChanged;
            Document.PreviewMouseRightButtonDown += Document_PreviewMouseRightButtonDown;
            Loaded += (_, _) => { SyncToolbar(); UpdateStatus(); };

            _proofTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _proofTimer.Tick += (_, _) => { _proofTimer.Stop(); RunProofing(); };

            _pageTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            _pageTimer.Tick += (_, _) => { _pageTimer.Stop(); UpdatePageCount(); };
        }

        /// <summary>Access to the underlying document for services (spell check, stats, export).</summary>
        public RichTextBox DocumentSurface => Document;

        // ---------- Document lifecycle ----------

        public void NewDocument()
        {
            _suspendDirty = true;
            try
            {
                Document.Document = new FlowDocument(new Paragraph());
            }
            finally
            {
                _suspendDirty = false;
            }
            Metadata = new DocumentMetadata();
            SetDirty(false);
            ResetProofing();
            SyncToolbar();
        }

        public void LoadDocument(string path)
        {
            _suspendDirty = true;
            try
            {
                if (path.EndsWith(".wforge", StringComparison.OrdinalIgnoreCase))
                {
                    var (document, metadata) = WforgePackage.Load(path);
                    DocumentAnnotations.Apply(document, metadata);
                    RestoreCustomStyles(metadata);
                    Document.Document = document;
                    Metadata = metadata;
                    ApplyImportedStyles();
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
            SyncToolbar();
        }

        public void SaveDocument(string path)
        {
            if (path.EndsWith(".wforge", StringComparison.OrdinalIgnoreCase))
            {
                DocumentAnnotations.Collect(Document.Document, Metadata);
                Metadata.CustomStyles = _customStyles.Count > 0
                    ? new List<CustomStyleRecord>(_customStyles)
                    : null;
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
            PdfExportService.Export(Document.Document, path, title, Metadata.Header, Metadata.Footer);
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
            var chars = text.TrimEnd('\r', '\n').Length;
            var charsNoSpaces = text.Count(c => !char.IsWhiteSpace(c));
            StatusText.Text = $"Words: {words}    Characters: {chars} (no spaces: {charsNoSpaces})    Paragraphs: {CountParagraphs()}";
            PlaceholderText.Visibility = string.IsNullOrWhiteSpace(text)
                ? Visibility.Visible
                : Visibility.Collapsed;
            _pageTimer.Stop();
            _pageTimer.Start();
        }

        /// <summary>Paragraph count for the status bar: all paragraphs minus a trailing empty one.</summary>
        private int CountParagraphs()
        {
            var paras = BlockTree.AllParagraphs(Document.Document).ToList();
            if (paras.Count == 0) return 0;
            var last = paras[^1];
            var lastEmpty = !DocumentTextMap.EnumerateRuns(last.Inlines).Any(r => r.Text.Trim().Length > 0);
            return paras.Count - (lastEmpty ? 1 : 0);
        }

        /// <summary>Recompute the live page count (debounced; engine is too heavy per keystroke).</summary>
        private void UpdatePageCount()
        {
            try
            {
                var pages = PaginationEngine.Paginate(Document.Document);
                var footnotes = pages.Sum(p => p.Footnotes.Count);
                PageCountText.Text = $"Pages: {pages.Count}" + (footnotes > 0 ? $"    Notes: {footnotes}" : "");
            }
            catch
            {
                PageCountText.Text = "";
            }
        }

        // ---------- Toolbar sync ----------

        /// <summary>Recompute and publish the current formatting state to ribbon hosts.</summary>
        public void SyncToolbar()
        {
            var sel = Document.Selection;
            var paragraph = sel.Start.Paragraph;
            var list = paragraph is { } p ? BlockTree.ParentList(p) : null;

            ToolbarStateChanged?.Invoke(this, new EditorToolbarState
            {
                CanUndo = Document.CanUndo,
                CanRedo = Document.CanRedo,
                Bold = GetValue(sel, TextElement.FontWeightProperty) is FontWeight w && w == FontWeights.Bold,
                Italic = GetValue(sel, TextElement.FontStyleProperty) is FontStyle s && s == FontStyles.Italic,
                Underline = HasDecoration(sel, TextDecorationLocation.Underline),
                Strikethrough = HasDecoration(sel, TextDecorationLocation.Strikethrough),
                Subscript = GetValue(sel, Inline.BaselineAlignmentProperty) is BaselineAlignment b && b == BaselineAlignment.Subscript,
                Superscript = GetValue(sel, Inline.BaselineAlignmentProperty) is BaselineAlignment b2 && b2 == BaselineAlignment.Superscript,
                FontFamily = (GetValue(sel, TextElement.FontFamilyProperty) as FontFamily)?.Source,
                FontSize = GetValue(sel, TextElement.FontSizeProperty) as double?,
                StyleKey = paragraph is null ? null : EditorTags.GetStyleKey(paragraph),
                Alignment = paragraph?.TextAlignment,
                Bullets = list?.MarkerStyle == TextMarkerStyle.Disc,
                Numbers = list?.MarkerStyle == TextMarkerStyle.Decimal,
            });
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

        // ---------- Ribbon commands ----------

        private void ApplyProperty(DependencyProperty property, object? value)
        {
            Document.Selection.ApplyPropertyValue(property, value);
        }

        /// <summary>Undo the last edit.</summary>
        public void Undo()
        {
            if (Document.CanUndo) Document.Undo();
            SyncToolbar();
        }

        /// <summary>Redo the last undone edit.</summary>
        public void Redo()
        {
            if (Document.CanRedo) Document.Redo();
            SyncToolbar();
        }

        /// <summary>Apply a named paragraph style (key from <see cref="GetParagraphStyles"/>).</summary>
        public void SetStyle(string key)
        {
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

        /// <summary>Apply a font family to the selection (and typing default when collapsed).</summary>
        public void SetFontFamily(string name)
        {
            ApplyFontName(name);
            MacroRecorder.Record("Font", name);
        }

        private void ApplyFontName(string name)
        {
            var family = new FontFamily(name);
            ApplyProperty(TextElement.FontFamilyProperty, family);
            if (Document.Selection.IsEmpty) Document.FontFamily = family;
        }

        /// <summary>Apply a font size to the selection (and typing default when collapsed).</summary>
        public void SetFontSize(double size)
        {
            ApplyFontSize(size);
            MacroRecorder.Record("Size", size.ToString(CultureInfo.InvariantCulture));
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

        /// <summary>Toggle bold on the selection.</summary>
        public void ToggleBold() { ToggleFontWeight(); SyncToolbar(); MacroRecorder.Record("Bold"); }

        private void ToggleFontStyle()
        {
            var current = GetValue(Document.Selection, TextElement.FontStyleProperty);
            var style = current is FontStyle s && s == FontStyles.Italic ? FontStyles.Normal : FontStyles.Italic;
            ApplyProperty(TextElement.FontStyleProperty, style);
        }

        /// <summary>Toggle italic on the selection.</summary>
        public void ToggleItalic() { ToggleFontStyle(); SyncToolbar(); MacroRecorder.Record("Italic"); }

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

        /// <summary>Toggle underline on the selection.</summary>
        public void ToggleUnderline() { ToggleDecoration(TextDecorationLocation.Underline); SyncToolbar(); MacroRecorder.Record("Underline"); }

        /// <summary>Toggle strikethrough on the selection.</summary>
        public void ToggleStrikethrough() { ToggleDecoration(TextDecorationLocation.Strikethrough); SyncToolbar(); MacroRecorder.Record("Strike"); }

        private void ToggleBaseline(BaselineAlignment target, BaselineAlignment off)
        {
            var current = GetValue(Document.Selection, Inline.BaselineAlignmentProperty);
            var value = current is BaselineAlignment b && b == target ? off : target;
            ApplyProperty(Inline.BaselineAlignmentProperty, value);
        }

        /// <summary>Toggle subscript on the selection.</summary>
        public void ToggleSubscript() { ToggleBaseline(BaselineAlignment.Subscript, BaselineAlignment.Baseline); SyncToolbar(); MacroRecorder.Record("Subscript"); }

        /// <summary>Toggle superscript on the selection.</summary>
        public void ToggleSuperscript() { ToggleBaseline(BaselineAlignment.Superscript, BaselineAlignment.Baseline); SyncToolbar(); MacroRecorder.Record("Superscript"); }

        /// <summary>Set paragraph alignment on the selection.</summary>
        public void SetAlignment(TextAlignment align)
        {
            ApplyProperty(Paragraph.TextAlignmentProperty, align);
            SyncToolbar();
            MacroRecorder.Record("Align", align.ToString());
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

        /// <summary>Show the font-colour palette anchored to <paramref name="target"/>.</summary>
        public void ShowFontColorMenu(FrameworkElement target) => ShowColorMenu(target, true);

        /// <summary>Show the highlight-colour palette anchored to <paramref name="target"/>.</summary>
        public void ShowHighlightMenu(FrameworkElement target) => ShowColorMenu(target, false);

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

        /// <summary>Set line spacing multiplier ("1", "1.15", "1.5", "2").</summary>
        public void SetLineSpacing(string tag)
        {
            PlaySpacing(tag);
            MacroRecorder.Record("Spacing", tag);
        }

        private void PlaySpacing(string tag)
        {
            if (Document?.Document is null || !double.TryParse(tag, out var spacing)) return;
            var size = GetValue(Document.Selection, TextElement.FontSizeProperty) is double s && !double.IsNaN(s)
                ? s
                : Document.FontSize;
            var family = GetValue(Document.Selection, TextElement.FontFamilyProperty) as FontFamily ?? Document.FontFamily;
            var lineHeight = size * (family?.LineSpacing ?? 1.2) * spacing;
            foreach (var p in SelectedParagraphs())
                p.LineHeight = lineHeight;
        }

        /// <summary>Set space-before-paragraph in points ("" = auto).</summary>
        public void SetSpaceBefore(string tag)
        {
            PlayParaSpacing(true, tag);
            MacroRecorder.Record("SpaceBefore", tag);
        }

        /// <summary>Set space-after-paragraph in points ("" = auto).</summary>
        public void SetSpaceAfter(string tag)
        {
            PlayParaSpacing(false, tag);
            MacroRecorder.Record("SpaceAfter", tag);
        }

        private void PlayParaSpacing(bool before, string tag)
        {
            if (Document?.Document is null) return;
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

        /// <summary>Adjust left indent of selected paragraphs by <paramref name="delta"/> DIPs.</summary>
        public void Indent(double delta)
        {
            PlayIndent(delta);
            MacroRecorder.Record("Indent", delta.ToString(CultureInfo.InvariantCulture));
        }

        private void PlayIndent(double delta)
        {
            if (Document?.Document is null) return;
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

        /// <summary>Show the image picker and insert the chosen file.</summary>
        public void PromptInsertImage()
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

        private void InsertInlineAtCaret(Inline container)
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

        /// <summary>Remove all direct formatting from the selection.</summary>
        public void ClearFormatting()
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

        /// <summary>Apply page size, margins, and running header/footer text.</summary>
        public void ApplyPageSetup(Size pageSize, Thickness margins, string? header, string? footer)
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
            Metadata.Header = string.IsNullOrWhiteSpace(header) ? null : header.Trim();
            Metadata.Footer = string.IsNullOrWhiteSpace(footer) ? null : footer.Trim();
            SetDirty(true);
        }

        /// <summary>Run the pagination engine; returns page/footnote counts for diagnostics.</summary>
        public string GetPaginationDebug()
        {
            var pages = PaginationEngine.Paginate(Document.Document);
            var footnotes = pages.Sum(p => p.Footnotes.Count);
            var lines = pages.Sum(p => p.Lines.Count);
            return $"Pages: {pages.Count}    Lines: {lines}    Page-footnotes: {footnotes}";
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
            // Proofing is best-effort: a failure here must never crash typing.
            try
            {
                RunProofingCore();
            }
            catch
            {
            }
        }

        private void RunProofingCore()
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
                    try
                    {
                        if (cts.IsCancellationRequested) return;
                        if (_proofText != text) return; // document changed since the check started
                        _grammarCheckedText = text;
                        ApplyGrammarUnderlines(issues, map);
                    }
                    catch
                    {
                    }
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

        /// <summary>Toggle a bulleted list on the selected paragraphs.</summary>
        public void ToggleBullets() { ToggleList(TextMarkerStyle.Disc); MacroRecorder.Record("Bullets"); }

        /// <summary>Toggle a numbered list on the selected paragraphs.</summary>
        public void ToggleNumbered() { ToggleList(TextMarkerStyle.Decimal); MacroRecorder.Record("Numbers"); }

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

        // ---------- Tables ----------

        /// <summary>Insert a rows×cols table before the caret's paragraph.</summary>
        public void InsertTable(int rows, int cols)
        {
            rows = Math.Clamp(rows, 1, 50);
            cols = Math.Clamp(cols, 1, 12);

            var group = new TableRowGroup();
            Paragraph? firstCellParagraph = null;
            for (var r = 0; r < rows; r++)
            {
                var row = new TableRow();
                for (var c = 0; c < cols; c++)
                {
                    var cell = NewTableCell();
                    row.Cells.Add(cell);
                    firstCellParagraph ??= cell.Blocks.OfType<Paragraph>().FirstOrDefault();
                }
                group.Rows.Add(row);
            }
            var table = new Table { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5) };
            table.RowGroups.Add(group);

            var caretParagraph = Document.CaretPosition.Paragraph;
            var blocks = caretParagraph is null ? null : ParentBlocksOf(caretParagraph);
            if (blocks is null)
            {
                Document.Document.Blocks.Add(table);
            }
            else if (caretParagraph is not null)
            {
                blocks.InsertBefore(caretParagraph, table);
            }

            if (firstCellParagraph is not null)
                Document.CaretPosition = firstCellParagraph.ContentStart;
            SyncToolbar();
        }

        private static TableCell NewTableCell()
        {
            var cell = new TableCell
            {
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(0.5),
            };
            cell.Blocks.Add(new Paragraph());
            return cell;
        }

        private (Table Table, TableRowGroup Group, TableRow Row, TableCell Cell, int RowIndex, int ColIndex)? TableContextAtCaret()
        {
            var cell = Document.CaretPosition.Paragraph?.Parent as TableCell;
            var row = cell?.Parent as TableRow;
            var group = row?.Parent as TableRowGroup;
            var table = group?.Parent as Table;
            if (cell is null || row is null || group is null || table is null) return null;
            return (table, group, row, cell, group.Rows.IndexOf(row), row.Cells.IndexOf(cell));
        }

        /// <summary>Insert a table row above or below the caret row.</summary>
        public bool InsertTableRow(bool below)
        {
            var ctx = TableContextAtCaret();
            if (ctx is null) return false;
            var (_, group, row, _, rowIndex, _) = ctx.Value;
            var newRow = new TableRow();
            for (var i = 0; i < row.Cells.Count; i++)
                newRow.Cells.Add(NewTableCell());
            group.Rows.Insert(below ? rowIndex + 1 : rowIndex, newRow);
            return true;
        }

        /// <summary>Insert a table column left or right of the caret column.</summary>
        public bool InsertTableColumn(bool right)
        {
            var ctx = TableContextAtCaret();
            if (ctx is null) return false;
            var (_, _, _, _, _, colIndex) = ctx.Value;
            foreach (TableRow row in ctx.Value.Group.Rows)
                row.Cells.Insert(Math.Min(right ? colIndex + 1 : colIndex, row.Cells.Count), NewTableCell());
            return true;
        }

        /// <summary>Delete the caret's table row.</summary>
        public bool DeleteTableRow()
        {
            var ctx = TableContextAtCaret();
            if (ctx is null || ctx.Value.Group.Rows.Count <= 1) return false;
            ctx.Value.Group.Rows.RemoveAt(ctx.Value.RowIndex);
            return true;
        }

        /// <summary>Delete the caret's table column.</summary>
        public bool DeleteTableColumn()
        {
            var ctx = TableContextAtCaret();
            if (ctx is null) return false;
            var (_, _, _, _, _, colIndex) = ctx.Value;
            foreach (TableRow row in ctx.Value.Group.Rows)
                if (row.Cells.Count > 1)
                    row.Cells.RemoveAt(Math.Min(colIndex, row.Cells.Count - 1));
            return true;
        }

        // ---------- Footnotes / endnotes ----------

        /// <summary>
        /// Insert a footnote (or endnote) at the caret: a superscript marker
        /// plus a numbered note paragraph at the end of the document.
        /// </summary>
        public void InsertFootnote(bool endnote = false)
        {
            var id = FootnoteService.NextId(Document.Document);
            var number = FootnoteService.Collect(Document.Document).Count + 1;

            var marker = new Run { Text = number.ToString() };
            marker.SetValue(Inline.BaselineAlignmentProperty, BaselineAlignment.Superscript);
            EditorTags.SetFootnoteId(marker, id);
            InsertInlineAtCaret(marker);

            var notePara = new Paragraph { Margin = new Thickness(24, 2, 0, 2), FontSize = 10 };
            EditorTags.SetStyleKey(notePara, endnote ? FootnoteService.EndnoteStyleKey : FootnoteService.FootnoteStyleKey);
            EditorTags.SetFootnoteId(notePara, id);
            notePara.Inlines.Add(new Run($"{number}. "));
            notePara.Inlines.Add(new Run());
            Document.Document.Blocks.Add(notePara);
            SetDirty(true);
        }

        /// <summary>Delete the footnote whose marker or note contains the caret.</summary>
        public bool DeleteFootnoteAtCaret()
        {
            var caretParagraph = Document.CaretPosition.Paragraph;
            int id;
            Paragraph? noteToRemove = null;

            if (caretParagraph is not null && FootnoteService.IsNoteParagraph(caretParagraph))
            {
                id = EditorTags.GetFootnoteId(caretParagraph);
                noteToRemove = caretParagraph;
            }
            else if (Document.CaretPosition.Parent is Run markerRun
                     && EditorTags.GetFootnoteId(markerRun) != 0)
            {
                id = EditorTags.GetFootnoteId(markerRun);
            }
            else
            {
                return false;
            }

            if (noteToRemove is not null && ParentBlocksOf(noteToRemove) is { } noteBlocks)
                noteBlocks.Remove(noteToRemove);

            foreach (var run in DocumentTextMap.EnumerateRuns(Document.Document)
                         .Where(r => EditorTags.GetFootnoteId(r) == id)
                         .ToList())
            {
                switch (run.Parent)
                {
                    case Paragraph p: p.Inlines.Remove(run); break;
                    case Span span: span.Inlines.Remove(run); break;
                }
            }
            foreach (var p in BlockTree.AllParagraphs(Document.Document)
                         .Where(p => EditorTags.GetFootnoteId(p) == id)
                         .ToList())
            {
                if (ParentBlocksOf(p) is { } blocks) blocks.Remove(p);
            }

            FootnoteService.Renumber(Document.Document);
            SetDirty(true);
            return true;
        }

        /// <summary>Renumber all footnote markers and note prefixes.</summary>
        public void RenumberFootnotes()
        {
            FootnoteService.Renumber(Document.Document);
            SetDirty(true);
        }

        /// <summary>Toggle all notes between footnote and endnote style.</summary>
        public void ConvertNotes()
        {
            FootnoteService.ConvertAll(Document.Document);
            SetDirty(true);
        }

        // ---------- Paragraph styles ----------

        /// <summary>Built-in paragraph styles (Normal through Caption).</summary>
        public static readonly (string Name, string Key)[] BuiltInStyles =
        {
            ("Normal", "StyleNormal"),
            ("Heading 1", "StyleHeading1"),
            ("Heading 2", "StyleHeading2"),
            ("Heading 3", "StyleHeading3"),
            ("Quote", "StyleQuote"),
            ("Caption", "StyleCaption"),
        };

        /// <summary>All selectable paragraph styles: built-ins plus session custom styles.</summary>
        public IReadOnlyList<(string Name, string Key)> GetParagraphStyles() =>
            BuiltInStyles.Concat(_customStyles.Select(r => (r.Name, r.Key))).ToList();

        // ---------- Table of contents & custom styles ----------

        private static string PlainText(Paragraph paragraph) =>
            string.Concat(DocumentTextMap.EnumerateRuns(paragraph.Inlines).Select(r => r.Text));

        /// <summary>
        /// Insert a table of contents (heading text only — page numbers require
        /// the paginated view) at the caret. Returns the number of entries.
        /// </summary>
        public int InsertToc()
        {
            var headings = BlockTree.AllParagraphs(Document.Document)
                .Select(p => (Para: p, Key: EditorTags.GetStyleKey(p), Text: PlainText(p).Trim()))
                .Where(x => x.Key is "Heading1" or "Heading2" or "Heading3" && x.Text.Length > 0)
                .ToList();
            if (headings.Count == 0) return 0;

            var blocks = new List<Block>
            {
                new Paragraph(new Run("Contents") { FontSize = 20, FontWeight = FontWeights.Bold }),
            };
            foreach (var h in headings)
            {
                var level = h.Key == "Heading1" ? 0 : h.Key == "Heading2" ? 1 : 2;
                var para = new Paragraph(new Run(h.Text));
                para.Margin = new Thickness(level * 24, 2, 0, 2);
                blocks.Add(para);
            }

            var caretParagraph = Document.CaretPosition.Paragraph;
            var parent = caretParagraph is null ? null : ParentBlocksOf(caretParagraph);
            if (parent is null)
            {
                foreach (var b in blocks) Document.Document.Blocks.Add(b);
            }
            else
            {
                foreach (var b in blocks) parent.InsertBefore(caretParagraph!, b);
            }
            return headings.Count;
        }

        /// <summary>
        /// Create a new paragraph style from the current selection's formatting
        /// and apply it. Returns false on empty selection.
        /// Custom styles live for the session (not persisted across restarts).
        /// </summary>
        public bool SaveSelectionAsStyle()
        {
            var sel = Document.Selection;
            if (sel.IsEmpty) return false;

            var dialog = new TextInputWindow("New Style", "Style name:", "My Style")
            {
                Owner = Window.GetWindow(this),
            };
            if (dialog.ShowDialog() != true) return false;
            var name = dialog.Value;

            var key = $"Custom{++_customStyleCounter}";
            var style = new Style(typeof(Paragraph));
            foreach (var prop in new[]
            {
                TextElement.FontFamilyProperty,
                TextElement.FontSizeProperty,
                TextElement.FontWeightProperty,
                TextElement.FontStyleProperty,
                Paragraph.TextAlignmentProperty,
            })
            {
                var value = sel.GetPropertyValue(prop);
                if (!ReferenceEquals(value, DependencyProperty.UnsetValue) && value is not null)
                    style.Setters.Add(new Setter(prop, value));
            }

            Resources.Add(key, style);
            _customStyles.Add(BuildStyleRecord(name, key, sel));
            ParagraphStylesChanged?.Invoke(this, EventArgs.Empty);
            ApplyParagraphStyle(key);
            return true;
        }

        /// <summary>Snapshot the selection's formatting into a persistable style record.</summary>
        private static CustomStyleRecord BuildStyleRecord(string name, string key, TextSelection sel)
        {
            var family = sel.GetPropertyValue(TextElement.FontFamilyProperty) as FontFamily;
            var size = sel.GetPropertyValue(TextElement.FontSizeProperty) as double?;
            var weight = sel.GetPropertyValue(TextElement.FontWeightProperty) as FontWeight?;
            var fontStyle = sel.GetPropertyValue(TextElement.FontStyleProperty) as FontStyle?;
            var align = sel.GetPropertyValue(Paragraph.TextAlignmentProperty) as TextAlignment?;

            return new CustomStyleRecord
            {
                Name = name,
                Key = key,
                FontFamily = family?.Source,
                FontSize = size,
                Bold = weight == FontWeights.Bold,
                Italic = fontStyle == FontStyles.Italic,
                Alignment = align?.ToString(),
            };
        }

        /// <summary>Recreate custom styles saved in .wforge metadata after a load.</summary>
        private void RestoreCustomStyles(DocumentMetadata metadata)
        {
            if (metadata.CustomStyles is not { Count: > 0 } records) return;

            foreach (var r in records)
            {
                var style = new Style(typeof(Paragraph));
                if (r.FontFamily is not null)
                    style.Setters.Add(new Setter(TextElement.FontFamilyProperty, new FontFamily(r.FontFamily)));
                if (r.FontSize is not null)
                    style.Setters.Add(new Setter(TextElement.FontSizeProperty, r.FontSize.Value));
                style.Setters.Add(new Setter(TextElement.FontWeightProperty,
                    r.Bold ? FontWeights.Bold : FontWeights.Normal));
                style.Setters.Add(new Setter(TextElement.FontStyleProperty,
                    r.Italic ? FontStyles.Italic : FontStyles.Normal));
                if (Enum.TryParse<TextAlignment>(r.Alignment, out var align))
                    style.Setters.Add(new Setter(Paragraph.TextAlignmentProperty, align));

                if (!Resources.Contains(r.Key))
                    Resources.Add(r.Key, style);
                if (!_customStyles.Any(c => c.Key == r.Key))
                    _customStyles.Add(r);
                if (r.Key.StartsWith("Custom", StringComparison.Ordinal) &&
                    int.TryParse(r.Key["Custom".Length..], out var n))
                    _customStyleCounter = Math.Max(_customStyleCounter, n);
            }
            ParagraphStylesChanged?.Invoke(this, EventArgs.Empty);
        }

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
