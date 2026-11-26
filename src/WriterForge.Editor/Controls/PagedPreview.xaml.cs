using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WriterForge.Editor.Services;

namespace WriterForge.Editor.Controls
{
    /// <summary>
    /// Read-only paginated view: renders <see cref="PageModel"/>s as page
    /// sheets with headers, footers, page numbers and page-bottom footnotes.
    /// Editing stays in the normal document view.
    /// </summary>
    public partial class PagedPreview : UserControl
    {
        private FlowDocument? _source;

        public PagedPreview()
        {
            InitializeComponent();
        }

        /// <summary>Render the given pages. Slices are rebuilt from the live document.</summary>
        internal void ShowPages(IReadOnlyList<PageModel> pages, FlowDocument source)
        {
            _source = source;
            PagesPanel.Children.Clear();
            for (var i = 0; i < pages.Count; i++)
                PagesPanel.Children.Add(BuildPage(pages[i], pages.Count, i));
        }

        private UIElement BuildPage(PageModel page, int pageCount, int index)
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

            // Header
            var header = new TextBlock
            {
                Text = page.Header ?? "",
                FontSize = 10,
                Foreground = Brushes.Gray,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 2, 0, 4),
            };
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            // Body
            var bodyHost = new FlowDocumentScrollViewer
            {
                Document = _source is null ? new FlowDocument() : PageSlicer.SliceBody(page, _source),
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false,
                Background = Brushes.Transparent,
            };
            Grid.SetRow(bodyHost, 1);
            grid.Children.Add(bodyHost);

            // Footnotes
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

            // Footer
            var footerText = page.Footer is { Length: > 0 } f
                ? $"{f}    —    Page {index + 1} of {pageCount}"
                : $"Page {index + 1} of {pageCount}";
            var footer = new TextBlock
            {
                Text = footerText,
                FontSize = 10,
                Foreground = Brushes.Gray,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 4, 0, 2),
            };
            Grid.SetRow(footer, 3);
            grid.Children.Add(footer);

            border.Child = grid;
            return border;
        }
    }
}
