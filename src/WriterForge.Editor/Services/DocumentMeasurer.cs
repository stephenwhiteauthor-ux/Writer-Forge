using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media.TextFormatting;

namespace WriterForge.Editor.Services
{
    /// <summary>Char offset + footnote id for a marker run.</summary>
    internal readonly record struct MarkerSpan(int Offset, int Id);

    /// <summary>Half-open char range [Start, Start+Length).</summary>
    internal readonly record struct CharSpan(int Start, int Length);
    /// <summary>Measured line within a paragraph (positions relative to paragraph start).</summary>
    internal sealed class MeasuredLine
    {
        /// <summary>Offset from the paragraph's first line top.</summary>
        public required double RelTop { get; init; }
        /// <summary>Line pitch (top of next line minus this top); estimate for the last line.</summary>
        public required double Height { get; init; }
        /// <summary>Char offsets into the paragraph's plain text (LineBreak/inline-object = 1 char).</summary>
        public int StartOffset { get; init; }
        public int Length { get; init; }
        /// <summary>Glyph extent top→bottom within the line (baseline proxy for rendering).</summary>
        public double GlyphExtent { get; init; }
    }

    /// <summary>Layout measurement for one source paragraph.</summary>
    internal sealed class ParagraphMeasure
    {
        public required Paragraph Source { get; init; }
        public required List<MeasuredLine> Lines { get; init; }
        /// <summary>False when the RTF clone diverged (e.g. dropped images) — lines are estimates.</summary>
        public bool Reliable { get; init; }
        public List<MarkerSpan> Markers { get; } = new();
        public List<(CharSpan Span, BaselineAlignment Kind)> Scripts { get; } = new();
        /// <summary>Effective font size, for fallback estimates.</summary>
        public double FontSize { get; init; }
    }

    /// <summary>Per-document measurement cache produced by <see cref="DocumentMeasurer"/>.</summary>
    internal sealed class MeasuredDocument
    {
        public required IReadOnlyDictionary<Paragraph, ParagraphMeasure> ByParagraph { get; init; }
        public double ColumnWidth { get; init; }
    }

    /// <summary>
    /// Measures the whole document by cloning it into a hidden RichTextBox at
    /// column width and reading per-character glyph rects — the same layout
    /// engine WPF uses, so line breaks, fonts and scripts are exact.
    /// </summary>
    internal static class DocumentMeasurer
    {
        public static MeasuredDocument Measure(FlowDocument source, double columnWidth)
        {
            var clone = CloneDocument(source);
            clone.PagePadding = new Thickness(0);

            var host = new System.Windows.Controls.RichTextBox
            {
                Document = clone,
                Width = columnWidth,
                Padding = new Thickness(0),
                IsReadOnly = true,
            };
            host.Measure(new Size(columnWidth, double.PositiveInfinity));
            host.Arrange(new Rect(0, 0, columnWidth, host.DesiredSize.Height));
            host.UpdateLayout();

            var sourceParas = BlockTree.AllParagraphs(source).ToList();
            var cloneParas = BlockTree.AllParagraphs(clone).ToList();
            var countsMatch = sourceParas.Count == cloneParas.Count;

            var map = new Dictionary<Paragraph, ParagraphMeasure>();
            for (var i = 0; i < sourceParas.Count; i++)
            {
                var sp = sourceParas[i];
                var reliable = countsMatch && TextEquals(sp, cloneParas[i]);
                map[sp] = MeasureParagraph(sp, reliable ? cloneParas[i] : null);
            }
            return new MeasuredDocument { ByParagraph = map, ColumnWidth = columnWidth };
        }

        private static FlowDocument CloneDocument(FlowDocument source)
        {
            var clone = new FlowDocument();
            using var ms = new MemoryStream();
            var range = new TextRange(source.ContentStart, source.ContentEnd);
            range.Save(ms, DataFormats.Rtf);
            ms.Position = 0;
            var target = new TextRange(clone.ContentStart, clone.ContentEnd);
            target.Load(ms, DataFormats.Rtf);
            return clone;
        }

        private static ParagraphMeasure MeasureParagraph(Paragraph source, Paragraph? clone)
        {
            var fontSize = source.GetValue(System.Windows.Documents.TextElement.FontSizeProperty) is double s ? s : 14.0;
            var measure = new ParagraphMeasure
            {
                Source = source,
                Lines = new List<MeasuredLine>(),
                Reliable = clone is not null,
                FontSize = fontSize,
            };
            CollectMarkersAndScripts(source, measure);

            if (clone is null)
            {
                measure.Lines.AddRange(EstimateLines(source, fontSize));
                return measure;
            }

            var start = clone.ContentStart;
            var end = clone.ContentEnd;
            var length = start.GetOffsetToPosition(end);
            if (length <= 0)
            {
                measure.Lines.Add(new MeasuredLine { RelTop = 0, Height = fontSize * 1.2, StartOffset = 0, Length = 0, GlyphExtent = fontSize });
                return measure;
            }

            // Read every char rect and cluster by vertical position into lines.
            var tol = Math.Max(3.0, fontSize * 0.45);
            var clusters = new List<List<(int Index, Rect Rect)>>();
            var current = new List<(int, Rect)>();
            var clusterTop = double.MinValue;
            for (var i = 0; i < length; i++)
            {
                var pos = start.GetPositionAtOffset(i);
                var rect = pos.GetCharacterRect(System.Windows.Documents.LogicalDirection.Forward);
                if (current.Count > 0 && rect.Y > clusterTop + tol)
                {
                    clusters.Add(current);
                    current = new List<(int, Rect)>();
                }
                if (current.Count == 0) clusterTop = rect.Y;
                current.Add((i, rect));
            }
            if (current.Count > 0) clusters.Add(current);

            var tops = clusters.Select(c => c.Min(x => x.Rect.Y)).ToList();
            var extents = clusters.Select(c => c.Max(x => x.Rect.Bottom) - c.Min(x => x.Rect.Y)).ToList();
            for (var i = 0; i < clusters.Count; i++)
            {
                var c = clusters[i];
                var height = i + 1 < tops.Count
                    ? tops[i + 1] - tops[i]
                    : (clusters.Count > 1 ? tops[i] - tops[i - 1] : fontSize * 1.2);
                measure.Lines.Add(new MeasuredLine
                {
                    RelTop = tops[i] - tops[0],
                    Height = Math.Max(height, fontSize * 0.8),
                    StartOffset = c[0].Index,
                    Length = c.Count,
                    GlyphExtent = extents[i],
                });
            }
            return measure;
        }

        /// <summary>Crude height estimate for paragraphs whose clone diverged.</summary>
        private static List<MeasuredLine> EstimateLines(Paragraph source, double fontSize)
        {
            var text = PlainText(source);
            var lineHeight = fontSize * 1.2;
            var charsPerLine = Math.Max(20, (int)(60 / (fontSize * 0.5)) * 10); // ~60 chars at 10pt-ish
            var lines = Math.Max(1, (int)Math.Ceiling(text.Length / (double)charsPerLine));
            var result = new List<MeasuredLine>();
            for (var i = 0; i < lines; i++)
            {
                var start = (int)((long)i * text.Length / lines);
                var end = (int)((long)(i + 1) * text.Length / lines);
                result.Add(new MeasuredLine
                {
                    RelTop = i * lineHeight,
                    Height = lineHeight,
                    StartOffset = start,
                    Length = Math.Max(0, end - start),
                    GlyphExtent = fontSize,
                });
            }
            return result;
        }

        private static void CollectMarkersAndScripts(Paragraph paragraph, ParagraphMeasure measure)
        {
            var pos = 0;
            foreach (var (text, run) in InlineTexts(paragraph.Inlines))
            {
                if (run is not null)
                {
                    var fid = EditorTags.GetFootnoteId(run);
                    if (fid != 0 && text.Length > 0)
                        measure.Markers.Add(new MarkerSpan(pos, fid));
                    if (run.BaselineAlignment is BaselineAlignment.Superscript or BaselineAlignment.Subscript && text.Length > 0)
                        measure.Scripts.Add((new CharSpan(pos, text.Length), run.BaselineAlignment));
                }
                pos += text.Length;
            }
        }

        /// <summary>Flatten inlines to (text, owning run) pairs; LineBreak/object = one char.</summary>
        private static IEnumerable<(string Text, Run? Run)> InlineTexts(InlineCollection inlines)
        {
            foreach (var inline in inlines)
            {
                switch (inline)
                {
                    case Run run: yield return (run.Text, run); break;
                    case Span span:
                        foreach (var t in InlineTexts(span.Inlines)) yield return t;
                        break;
                    case LineBreak: yield return ("\n", null); break;
                    case InlineUIContainer: yield return (" ", null); break;
                }
            }
        }

        private static string PlainText(Paragraph paragraph) =>
            string.Concat(InlineTexts(paragraph.Inlines).Select(t => t.Text));

        private static bool TextEquals(Paragraph a, Paragraph b) =>
            PlainText(a) == PlainText(b);
    }
}
