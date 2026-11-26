using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace WriterForge.Editor.Services
{
    /// <summary>
    /// Wraps a FlowDocument's paginated output in a FixedDocument, adding
    /// running header/footer text and page numbers in the margin areas.
    /// Used for printing and print preview.
    /// </summary>
    public static class PrintableDocument
    {
        private const double HeaderHeight = 22;
        private const double FooterHeight = 22;

        public static FixedDocument Compose(FlowDocument source, string? header, string? footer)
        {
            var paginator = ((IDocumentPaginatorSource)source).DocumentPaginator;
            var fixedDocument = new FixedDocument();
            var hasHeader = !string.IsNullOrWhiteSpace(header);
            var hasFooter = !string.IsNullOrWhiteSpace(footer) || true; // page number always shown

            for (var i = 0; i < paginator.PageCount; i++)
            {
                var page = paginator.GetPage(i);
                var fixedPage = new FixedPage
                {
                    Width = page.Size.Width,
                    Height = page.Size.Height,
                };

                var view = new DocumentPageView
                {
                    DocumentPaginator = paginator,
                    PageNumber = i,
                    Width = page.Size.Width,
                    Height = page.Size.Height,
                };
                fixedPage.Children.Add(view);

                var marginLeft = source.PagePadding.Left;
                var marginRight = page.Size.Width - source.PagePadding.Right;
                var contentWidth = marginRight - marginLeft;

                if (hasHeader)
                {
                    fixedPage.Children.Add(new TextBlock
                    {
                        Text = header,
                        FontSize = 10,
                        Foreground = Brushes.Gray,
                        TextAlignment = TextAlignment.Center,
                        Width = contentWidth,
                        Margin = new Thickness(marginLeft, 4, 0, 0),
                    });
                }

                var footerText = string.IsNullOrWhiteSpace(footer)
                    ? $"Page {i + 1} of {paginator.PageCount}"
                    : $"{footer}    —    Page {i + 1} of {paginator.PageCount}";
                fixedPage.Children.Add(new TextBlock
                {
                    Text = footerText,
                    FontSize = 10,
                    Foreground = Brushes.Gray,
                    TextAlignment = TextAlignment.Center,
                    Width = contentWidth,
                    Margin = new Thickness(marginLeft, page.Size.Height - FooterHeight - source.PagePadding.Bottom + 4, 0, 0),
                });

                var pageContent = new PageContent { Child = fixedPage };
                fixedDocument.Pages.Add(pageContent);
            }

            return fixedDocument;
        }
    }
}
