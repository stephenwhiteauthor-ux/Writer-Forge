using System.Windows;
using System.Windows.Documents;

namespace WriterForge.Editor.Services
{
    /// <summary>
    /// Builds per-page FlowDocuments from <see cref="PageModel"/> line ranges,
    /// so each page can be rendered by a read-only WPF document viewer.
    /// </summary>
    internal static class PageSlicer
    {
        /// <summary>Document containing the body-text slices that fall on this page.</summary>
        public static FlowDocument SliceBody(PageModel page, FlowDocument source)
        {
            var doc = new FlowDocument { PagePadding = new Thickness(0) };

            Paragraph? current = null;
            var start = 0;
            var end = 0;

            void Flush()
            {
                if (current is null) return;
                var textLen = DocumentCopier.TextLength(current);
                var sliceEnd = Math.Min(end, textLen);
                var copy = start <= 0 && sliceEnd >= textLen
                    ? DocumentCopier.Copy(current)
                    : DocumentCopier.SliceParagraph(current, start, sliceEnd);
                doc.Blocks.Add(copy);
            }

            foreach (var line in page.Lines)
            {
                if (!ReferenceEquals(line.Paragraph, current))
                {
                    Flush();
                    current = line.Paragraph;
                    start = line.StartOffset;
                    end = line.StartOffset + line.Length;
                }
                else
                {
                    end = Math.Max(end, line.StartOffset + line.Length);
                }
            }
            Flush();
            return doc;
        }

        /// <summary>Document containing this page's footnote paragraphs, in marker order.</summary>
        public static FlowDocument SliceFootnotes(PageModel page)
        {
            var doc = new FlowDocument { PagePadding = new Thickness(0) };
            var seen = new HashSet<Paragraph>();
            foreach (var placement in page.Footnotes)
            foreach (var line in placement.Lines)
            {
                if (line.Paragraph is not null && seen.Add(line.Paragraph))
                    doc.Blocks.Add(DocumentCopier.Copy(line.Paragraph));
            }
            return doc;
        }
    }
}
