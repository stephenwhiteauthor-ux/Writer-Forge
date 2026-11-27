using System.Windows;
using System.Windows.Documents;

namespace WriterForge.Editor.Services
{
    /// <summary>
    /// Builds per-page FlowDocuments from <see cref="PageModel"/> line ranges,
    /// so each page can be rendered by a document viewer. Slice paragraphs and
    /// tables are stamped with their source ordinal for edit merge-back.
    /// </summary>
    internal static class PageSlicer
    {
        /// <summary>Document containing the body-content slices that fall on this page.</summary>
        public static FlowDocument SliceBody(
            PageModel page, FlowDocument source, IReadOnlyDictionary<Paragraph, int> ordinals)
        {
            var doc = new FlowDocument { PagePadding = new Thickness(0) };
            var tableOrds = BlockTree.FlowTables(source)
                .Select((t, i) => (t, i))
                .ToDictionary(x => x.t, x => x.i);

            // Group contiguous lines into per-paragraph slice ranges.
            var groups = new List<(double Top, Paragraph Para, int Start, int End)>();
            Paragraph? current = null;
            var start = 0;
            var end = 0;
            var top = 0.0;
            foreach (var line in page.Lines)
            {
                if (!ReferenceEquals(line.Paragraph, current))
                {
                    if (current is not null) groups.Add((top, current, start, end));
                    current = line.Paragraph;
                    start = line.StartOffset;
                    end = line.StartOffset + line.Length;
                    top = line.Top;
                }
                else
                {
                    end = Math.Max(end, line.StartOffset + line.Length);
                }
            }
            if (current is not null) groups.Add((top, current, start, end));

            // Merge paragraph groups and table placements in vertical order.
            var gi = 0;
            var ti = 0;
            while (gi < groups.Count || ti < page.Tables.Count)
            {
                if (ti < page.Tables.Count &&
                    (gi >= groups.Count || page.Tables[ti].Top <= groups[gi].Top))
                {
                    var placement = page.Tables[ti++];
                    var tableCopy = DocumentCopier.Copy(placement.Table);
                    if (tableOrds.TryGetValue(placement.Table, out var tOrd))
                        EditorTags.SetSourceOrdinal(tableCopy, tOrd);
                    doc.Blocks.Add(tableCopy);
                }
                else
                {
                    var g = groups[gi++];
                    var textLen = DocumentCopier.TextLength(g.Para);
                    var sliceEnd = Math.Min(g.End, textLen);
                    var copy = g.Start <= 0 && sliceEnd >= textLen
                        ? DocumentCopier.Copy(g.Para)
                        : DocumentCopier.SliceParagraph(g.Para, g.Start, sliceEnd);
                    if (ordinals.TryGetValue(g.Para, out var ordinal))
                        EditorTags.SetSourceOrdinal(copy, ordinal);
                    doc.Blocks.Add(copy);
                }
            }
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
