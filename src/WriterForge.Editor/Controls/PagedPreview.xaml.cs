using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using WriterForge.Editor.Services;

namespace WriterForge.Editor.Controls
{
    /// <summary>
    /// Paginated view: renders <see cref="PageModel"/>s as page sheets with
    /// headers, footers, page numbers and page-bottom footnotes. In editable
    /// mode each page body is a RichTextBox slice; edits are merged back into
    /// the master document by the host.
    /// </summary>
    public partial class PagedPreview : UserControl
    {
        private FlowDocument? _source;
        private readonly List<RichTextBox> _bodyEditors = new();
        private readonly List<TextBox> _headerBoxes = new();
        private readonly List<TextBox> _footerBoxes = new();
        private bool _editable;
        private bool _hfSync;
        private string _headerText = "";
        private string _footerText = "";

        /// <summary>Raised when any page-body editor's text changes.</summary>
        public event EventHandler? PagesEdited;

        /// <summary>Raised when a page header/footer is edited: (header, footer) raw text.</summary>
        public event Action<string?, string?>? HeaderFooterEdited;

        public PagedPreview()
        {
            InitializeComponent();
        }

        /// <summary>Render the given pages. Slices are rebuilt from the live document.</summary>
        internal void ShowPages(
            IReadOnlyList<PageModel> pages, FlowDocument source,
            IReadOnlyDictionary<Paragraph, int> ordinals, bool editable)
        {
            _source = source;
            _editable = editable;
            _bodyEditors.Clear();
            _headerBoxes.Clear();
            _footerBoxes.Clear();
            _headerText = pages.Count > 0 ? pages[0].Header ?? "" : "";
            _footerText = pages.Count > 0 ? pages[0].Footer ?? "" : "";
            PagesPanel.Children.Clear();
            for (var i = 0; i < pages.Count; i++)
                PagesPanel.Children.Add(BuildPage(pages[i], pages.Count, i, ordinals));
        }

        /// <summary>The body-slice documents of all pages, in page order.</summary>
        internal IReadOnlyList<FlowDocument> GetPageDocuments() =>
            _bodyEditors.Select(e => e.Document).ToList();

        /// <summary>
        /// Caret position of the focused page editor as (pageIndex, sourceOrdinal, offsetInSlice).
        /// Returns null when no page editor has focus.
        /// </summary>
        internal (int Page, int Ordinal, int Offset)? GetCaretInfo()
        {
            if (Keyboard.FocusedElement is not RichTextBox rtb) return null;
            var page = _bodyEditors.IndexOf(rtb);
            if (page < 0 || rtb.CaretPosition?.Paragraph is not { } para) return (page, -1, 0);
            var ordinal = EditorTags.GetSourceOrdinal(para);
            var offset = para.ContentStart.GetOffsetToPosition(rtb.CaretPosition);
            return (page, ordinal, offset);
        }

        /// <summary>Restore caret to a source ordinal after rebuild (searches all pages).</summary>
        internal void SetCaret(int page, int ordinal, int offset)
        {
            // Prefer the recorded page, then scan the rest — repagination may move it.
            for (var i = 0; i < _bodyEditors.Count; i++)
            {
                var idx = (page + i) % _bodyEditors.Count;
                var rtb = _bodyEditors[idx];
                foreach (var para in BlockTree.AllParagraphs(rtb.Document))
                {
                    if (EditorTags.GetSourceOrdinal(para) != ordinal) continue;
                    var len = para.ContentStart.GetOffsetToPosition(para.ContentEnd);
                    var pos = para.ContentStart.GetPositionAtOffset(Math.Min(offset, len));
                    if (pos is not null)
                    {
                        rtb.CaretPosition = pos;
                        rtb.Focus();
                        para.BringIntoView();
                    }
                    return;
                }
            }
        }

        private UIElement BuildPage(
            PageModel page, int pageCount, int index,
            IReadOnlyDictionary<Paragraph, int> ordinals)
        {
            var border = new Border
            {
                Width = page.Size.Width,
                Height = page.Size.Height,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 28),
                SnapsToDevicePixels = true,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    Opacity = 0.25,
                    BlurRadius = 8,
                    ShadowDepth = 2,
                },
            };

            var grid = new Grid { Margin = page.Padding };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // header
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // body
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // footnotes
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // footer

            var headerBox = new TextBox
            {
                Text = page.Header ?? "",
                FontSize = 10,
                Foreground = Brushes.Gray,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                TextAlignment = TextAlignment.Center,
                Width = page.ContentWidth,
            };
            headerBox.TextChanged += HeaderBox_TextChanged;
            _headerBoxes.Add(headerBox);
            Grid.SetRow(headerBox, 0);
            grid.Children.Add(headerBox);

            var sliceDoc = _source is null
                ? new FlowDocument()
                : PageSlicer.SliceBody(page, _source, ordinals);

            FrameworkElement bodyHost;
            if (_editable)
            {
                var rtb = new RichTextBox
                {
                    Document = sliceDoc,
                    Padding = new Thickness(0),
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    FontFamily = new FontFamily("Georgia"),
                    FontSize = 14,
                    AcceptsReturn = true,
                };
                rtb.TextChanged += (_, _) => PagesEdited?.Invoke(this, EventArgs.Empty);
                _bodyEditors.Add(rtb);
                bodyHost = rtb;
            }
            else
            {
                bodyHost = new FlowDocumentScrollViewer
                {
                    Document = sliceDoc,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Focusable = false,
                    Background = Brushes.Transparent,
                };
            }
            Grid.SetRow(bodyHost, 1);
            grid.Children.Add(bodyHost);

            if (page.Footnotes.Count > 0 && _source is not null)
            {
                var notesHost = new StackPanel();
                notesHost.Children.Add(new Border
                {
                    Height = 1,
                    Background = Brushes.LightGray,
                    Margin = new Thickness(0, 2, 0, 2),
                });
                notesHost.Children.Add(new FlowDocumentScrollViewer
                {
                    Document = PageSlicer.SliceFootnotes(page),
                    VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Focusable = false,
                    Background = Brushes.Transparent,
                    Height = page.FootnoteAreaHeight - 6,
                });
                Grid.SetRow(notesHost, 2);
                grid.Children.Add(notesHost);
            }

            var footerPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 2),
            };
            var footerBox = new TextBox
            {
                Text = page.Footer ?? "",
                Width = page.ContentWidth * 0.5,
                FontSize = 10,
                Foreground = Brushes.Gray,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                TextAlignment = TextAlignment.Center,
            };
            footerBox.TextChanged += FooterBox_TextChanged;
            _footerBoxes.Add(footerBox);
            footerPanel.Children.Add(footerBox);
            var numText = page.Footer is { Length: > 0 }
                ? $"    —    Page {index + 1} of {pageCount}"
                : $"Page {index + 1} of {pageCount}";
            footerPanel.Children.Add(new TextBlock
            {
                Text = numText,
                FontSize = 10,
                Foreground = Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetRow(footerPanel, 3);
            grid.Children.Add(footerPanel);

            border.Child = grid;
            return border;
        }

        private static string? Norm(string? s) =>
            string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private void HeaderBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_hfSync || sender is not TextBox box) return;
            _headerText = box.Text;
            _hfSync = true;
            try
            {
                foreach (var b in _headerBoxes) b.Text = _headerText;
            }
            finally { _hfSync = false; }
            HeaderFooterEdited?.Invoke(Norm(_headerText), Norm(_footerText));
        }

        private void FooterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_hfSync || sender is not TextBox box) return;
            _footerText = box.Text;
            _hfSync = true;
            try
            {
                foreach (var b in _footerBoxes) b.Text = _footerText;
            }
            finally { _hfSync = false; }
            HeaderFooterEdited?.Invoke(Norm(_headerText), Norm(_footerText));
        }
    }
}
