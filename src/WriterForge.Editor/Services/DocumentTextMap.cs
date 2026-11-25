using System.Windows.Documents;

namespace WriterForge.Editor.Services
{
    /// <summary>
    /// A plain-text view of a FlowDocument plus a map from character offsets
    /// back to document positions. Paragraphs are joined with "\n".
    /// Used for grammar-check offsets and proofing underlines.
    /// </summary>
    internal sealed class DocumentTextMap
    {
        private sealed class Segment
        {
            public required Run Run;
            public int Start;
        }

        public required string Text { get; init; }
        private List<Segment> Segments { get; set; } = new();

        public static DocumentTextMap Build(FlowDocument document)
        {
            var sb = new System.Text.StringBuilder();
            var segments = new List<Segment>();

            var paragraphs = BlockTree.AllParagraphs(document).ToList();
            for (var i = 0; i < paragraphs.Count; i++)
            {
                AppendParagraph(paragraphs[i], sb, segments);
                if (i < paragraphs.Count - 1) sb.Append('\n');
            }

            return new DocumentTextMap { Text = sb.ToString(), Segments = segments };
        }

        private static void AppendParagraph(Paragraph paragraph, System.Text.StringBuilder sb, List<Segment> segments)
        {
            foreach (var run in EnumerateRuns(paragraph.Inlines))
            {
                if (run.Text.Length == 0) continue;
                segments.Add(new Segment { Run = run, Start = sb.Length });
                sb.Append(run.Text);
            }
        }

        /// <summary>Create a TextRange covering <paramref name="length"/> chars at <paramref name="offset"/>, clamped to run boundaries.</summary>
        public TextRange? RangeForOffset(int offset, int length)
        {
            if (offset < 0 || length <= 0) return null;

            // Find the last segment starting at or before the offset.
            Segment? segment = null;
            foreach (var s in Segments)
            {
                if (s.Start <= offset) segment = s;
                else break;
            }
            if (segment is null) return null;

            var localStart = offset - segment.Start;
            var available = segment.Run.Text.Length - localStart;
            if (available <= 0) return null;

            var take = Math.Min(length, available);
            var start = segment.Run.ContentStart.GetPositionAtOffset(localStart);
            var end = segment.Run.ContentStart.GetPositionAtOffset(localStart + take);
            if (start is null || end is null) return null;
            return new TextRange(start, end);
        }

        /// <summary>
        /// Create a TextRange spanning <paramref name="length"/> chars at
        /// <paramref name="offset"/>, possibly across run boundaries.
        /// Returns null if either end cannot be resolved.
        /// </summary>
        public TextRange? RangeForSpan(int offset, int length)
        {
            if (offset < 0 || length <= 0) return null;
            var start = PositionAt(offset);
            var end = PositionAt(offset + length - 1);
            if (start is null || end is null) return null;
            var endPos = end.GetPositionAtOffset(1) ?? end;
            return new TextRange(start, endPos);
        }

        private TextPointer? PositionAt(int offset)
        {
            Segment? segment = null;
            foreach (var s in Segments)
            {
                if (s.Start <= offset) segment = s;
                else break;
            }
            if (segment is null) return null;
            var local = offset - segment.Start;
            return local < segment.Run.Text.Length
                ? segment.Run.ContentStart.GetPositionAtOffset(local)
                : null;
        }

        /// <summary>Global offset of a position inside a run, or -1.</summary>
        public int OffsetOf(Run run, int localIndex)
        {
            foreach (var s in Segments)
                if (ReferenceEquals(s.Run, run))
                    return s.Start + localIndex;
            return -1;
        }

        internal static IEnumerable<Run> EnumerateRuns(IEnumerable<Inline> inlines)
        {
            foreach (var inline in inlines)
            {
                if (inline is Run run)
                    yield return run;
                else if (inline is Span span)
                    foreach (var nested in EnumerateRuns(span.Inlines))
                        yield return nested;
                else if (inline is Hyperlink hyperlink)
                    foreach (var nested in EnumerateRuns(hyperlink.Inlines))
                        yield return nested;
            }
        }

        internal static IEnumerable<Run> EnumerateRuns(FlowDocument document)
        {
            foreach (var block in document.Blocks)
            {
                if (block is Paragraph paragraph)
                    foreach (var run in EnumerateRuns(paragraph.Inlines))
                        yield return run;
                else if (block is Section section)
                    foreach (var p in section.Blocks.OfType<Paragraph>())
                        foreach (var run in EnumerateRuns(p.Inlines))
                            yield return run;
            }
        }
    }
}
