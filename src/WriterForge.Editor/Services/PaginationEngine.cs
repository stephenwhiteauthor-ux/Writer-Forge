using System.Windows;
using System.Windows.Documents;

namespace WriterForge.Editor.Services
{
    /// <summary>
    /// Paginates a FlowDocument into <see cref="PageModel"/>s. Line breaking is
    /// done by WPF's own layout engine via <see cref="DocumentMeasurer"/>;
    /// this class only flows measured lines onto fixed-size pages.
    /// Footnotes are placed at the bottom of the page containing their marker
    /// via an iterative reserve-and-repaginate pass.
    /// </summary>
    internal static class PaginationEngine
    {
        private const double DefaultPageWidth = 816;   // US Letter at 96dpi
        private const double DefaultPageHeight = 1056;
        private const double NoteGap = 6;

        public static List<PageModel> Paginate(FlowDocument document, string? header = null, string? footer = null)
        {
            FootnoteService.Renumber(document);

            var width = document.PageWidth > 0 ? document.PageWidth : DefaultPageWidth;
            var height = document.PageHeight > 0 ? document.PageHeight : DefaultPageHeight;
            var pad = document.PagePadding;
            var contentWidth = width - pad.Left - pad.Right;

            var measured = DocumentMeasurer.Measure(document, contentWidth);
            var notes = NoteMeasures(document, measured);
            var numbers = NoteNumbers(document);

            // Iterate: reservations depend on which page each marker lands,
            // which depends on reservations. Converges quickly in practice.
            var reservations = new Dictionary<int, double>();
            List<PageModel> pages = new();
            for (var iter = 0; iter < 3; iter++)
            {
                pages = PaginateCore(document, measured, width, height, pad,
                    i => reservations.TryGetValue(i, out var r) ? r : 0);
                var next = ComputeReservations(pages, notes);
                if (SameReservations(reservations, next)) break;
                reservations = next;
            }

            AttachFootnotes(pages, notes, numbers);
            foreach (var page in pages)
            {
                page.Header = header;
                page.Footer = footer;
            }
            return pages;
        }

        // ---------- core flow ----------

        private static List<PageModel> PaginateCore(
            FlowDocument document, MeasuredDocument measured,
            double width, double height, Thickness pad,
            Func<int, double> reservedHeight)
        {
            var pages = new List<PageModel>();
            PageModel? page = null;
            var y = 0.0;

            PageModel NewPage()
            {
                var p = new PageModel
                {
                    Index = pages.Count,
                    Size = new Size(width, height),
                    Padding = pad,
                };
                pages.Add(p);
                return p;
            }

            bool PageHasContent(PageModel? p) =>
                p is not null && (p.Lines.Count > 0 || p.Tables.Count > 0);

            foreach (var block in BlockTree.FlowBlocks(document))
            {
                // Tables are atomic: they move whole to the next page rather
                // than split (a table taller than a page overflows — rare).
                if (block is Table table)
                {
                    if (!measured.ByTable.TryGetValue(table, out var tm)) continue;
                    if (PageHasContent(page) &&
                        y + tm.Height > page!.ContentHeight - reservedHeight(page.Index))
                    {
                        page = null;
                        y = 0;
                    }
                    page ??= NewPage();
                    page.Tables.Add(new TablePlacement { Table = table, Top = y, Height = tm.Height });
                    y += tm.Height;
                    continue;
                }

                if (block is not Paragraph para) continue;

                // Footnote paragraphs render in the page-bottom area, not the body flow.
                // (Endnote paragraphs stay in the flow — they collect at the document end.)
                if (EditorTags.GetStyleKey(para) == FootnoteService.FootnoteStyleKey) continue;
                if (!measured.ByParagraph.TryGetValue(para, out var pm)) continue;

                // Space before: suppressed at the top of a fresh page.
                if (para.Margin.Top > 0 && PageHasContent(page))
                    y += para.Margin.Top;

                for (var i = 0; i < pm.Lines.Count; i++)
                {
                    var ml = pm.Lines[i];
                    var lineHeight = ml.Height;
                    if (PageHasContent(page) &&
                        y + lineHeight > page!.ContentHeight - reservedHeight(page.Index))
                    {
                        page = null; // break to a new page
                        y = 0;
                    }
                    page ??= NewPage();

                    var model = new LineModel
                    {
                        Paragraph = para,
                        Top = y,
                        Height = lineHeight,
                        StartOffset = ml.StartOffset,
                        Length = ml.Length,
                        Baseline = ml.GlyphExtent * 0.8,
                        IsLastInParagraph = i == pm.Lines.Count - 1,
                    };
                    foreach (var m in pm.Markers)
                        if (m.Offset >= ml.StartOffset && m.Offset < ml.StartOffset + Math.Max(ml.Length, 1))
                            model.Markers.Add(m);
                    foreach (var s in pm.Scripts)
                        if (s.Span.Start < ml.StartOffset + Math.Max(ml.Length, 1) &&
                            s.Span.Start + s.Span.Length > ml.StartOffset)
                            model.Scripts.Add(s);
                    page.Lines.Add(model);
                    y += lineHeight;
                }

                if (para.Margin.Bottom > 0) y += para.Margin.Bottom;
            }

            page ??= NewPage(); // empty document still gets one page
            return pages;
        }

        // ---------- footnotes ----------

        private sealed class NoteMeasure
        {
            public required int Id;
            public required Paragraph Paragraph;
            public required ParagraphMeasure Measure;
            public double Height;
        }

        private static Dictionary<int, NoteMeasure> NoteMeasures(
            FlowDocument document, MeasuredDocument measured)
        {
            var notes = new Dictionary<int, NoteMeasure>();
            foreach (var p in BlockTree.AllParagraphs(document))
            {
                var id = EditorTags.GetFootnoteId(p);
                if (id == 0 || !FootnoteService.IsNoteParagraph(p) || notes.ContainsKey(id)) continue;
                if (!measured.ByParagraph.TryGetValue(p, out var pm)) continue;
                var height = pm.Lines.Sum(l => l.Height) + 2;
                notes[id] = new NoteMeasure { Id = id, Paragraph = p, Measure = pm, Height = height };
            }
            return notes;
        }

        private static Dictionary<int, int> NoteNumbers(FlowDocument document)
        {
            var map = new Dictionary<int, int>();
            var n = 0;
            foreach (var f in FootnoteService.Collect(document))
                map[f.Id] = ++n;
            return map;
        }

        /// <summary>Marker ids present on each page, in first-appearance order.</summary>
        private static List<int> PageMarkerIds(PageModel page)
        {
            var ids = new List<int>();
            var seen = new HashSet<int>();
            foreach (var line in page.Lines)
                foreach (var m in line.Markers)
                    if (seen.Add(m.Id)) ids.Add(m.Id);
            return ids;
        }

        private static Dictionary<int, double> ComputeReservations(
            List<PageModel> pages, Dictionary<int, NoteMeasure> notes)
        {
            var map = new Dictionary<int, double>();
            foreach (var page in pages)
            {
                var h = 0.0;
                var any = false;
                foreach (var id in PageMarkerIds(page))
                    if (notes.TryGetValue(id, out var note)) { h += note.Height; any = true; }
                if (any) map[page.Index] = h + NoteGap;
            }
            return map;
        }

        private static bool SameReservations(Dictionary<int, double> a, Dictionary<int, double> b)
        {
            if (a.Count != b.Count) return false;
            foreach (var (k, v) in a)
                if (!b.TryGetValue(k, out var bv) || Math.Abs(bv - v) > 0.5) return false;
            return true;
        }

        private static void AttachFootnotes(
            List<PageModel> pages, Dictionary<int, NoteMeasure> notes, Dictionary<int, int> numbers)
        {
            foreach (var page in pages)
            {
                var ids = PageMarkerIds(page);
                if (ids.Count == 0) continue;

                var total = ids.Where(notes.ContainsKey).Sum(id => notes[id].Height) + NoteGap;
                if (total <= 0) continue;

                var y = page.ContentHeight - total;
                page.FootnoteAreaHeight = total;
                foreach (var id in ids)
                {
                    if (!notes.TryGetValue(id, out var note)) continue;
                    var placement = new FootnotePlacement
                    {
                        Id = id,
                        Number = numbers.TryGetValue(id, out var num) ? num : id,
                        Top = y,
                        Height = note.Height,
                        Lines = new List<LineModel>(),
                    };
                    foreach (var ml in note.Measure.Lines)
                    {
                        var lm = new LineModel
                        {
                            Paragraph = note.Paragraph,
                            Top = y,
                            Height = ml.Height,
                            StartOffset = ml.StartOffset,
                            Length = ml.Length,
                            Baseline = ml.GlyphExtent * 0.8,
                            IsLastInParagraph = false,
                        };
                        placement.Lines.Add(lm);
                        y += ml.Height;
                    }
                    page.Footnotes.Add(placement);
                    y += 2;
                }
            }
        }
    }
}
