using System.Windows.Documents;
using WriterForge.Editor.Documents;

namespace WriterForge.Editor.Services
{
    /// <summary>
    /// Persists document annotations that RTF cannot carry: named paragraph
    /// style keys, footnote/endnote marker links, and note ids. Records are
    /// keyed by paragraph ordinal (BlockTree.AllParagraphs order) plus, for
    /// markers, the character offset inside the paragraph — stable because the
    /// saved document is exactly what gets reloaded.
    /// </summary>
    internal static class DocumentAnnotations
    {
        /// <summary>Collect style tags and footnote markers for saving.</summary>
        public static void Collect(FlowDocument document, DocumentMetadata metadata)
        {
            var paragraphs = BlockTree.AllParagraphs(document).ToList();
            var indexOf = paragraphs.Select((p, i) => (p, i)).ToDictionary(x => x.p, x => x.i);

            var styles = new List<ParaStyleRecord>();
            for (var i = 0; i < paragraphs.Count; i++)
            {
                var p = paragraphs[i];
                var key = EditorTags.GetStyleKey(p);
                var noteId = EditorTags.GetFootnoteId(p);
                if (key is not null || noteId != 0)
                    styles.Add(new ParaStyleRecord { Index = i, StyleKey = key ?? "", FootnoteId = noteId });
            }
            metadata.ParagraphStyles = styles.Count > 0 ? styles : null;

            var markers = new List<FootnoteMarkerRecord>();
            for (var i = 0; i < paragraphs.Count; i++)
            {
                var p = paragraphs[i];
                foreach (var run in DocumentTextMap.EnumerateRuns(p.Inlines))
                {
                    var id = EditorTags.GetFootnoteId(run);
                    if (id == 0) continue;
                    var offset = p.ContentStart.GetOffsetToPosition(run.ContentStart);
                    if (offset >= 0)
                        markers.Add(new FootnoteMarkerRecord { ParagraphIndex = i, Offset = offset, Id = id });
                }
            }
            metadata.FootnoteMarkers = markers.Count > 0 ? markers : null;
        }

        /// <summary>
        /// Re-apply collected annotations to a freshly loaded document.
        /// Returns false when the paragraph count doesn't match (document was
        /// structurally altered — annotations are skipped rather than misapplied).
        /// </summary>
        public static bool Apply(FlowDocument document, DocumentMetadata metadata)
        {
            var paragraphs = BlockTree.AllParagraphs(document).ToList();

            // Validate everything up-front: a structurally different document
            // gets no annotations rather than misapplied ones.
            if (metadata.ParagraphStyles is { } styles &&
                styles.Any(s => s.Index < 0 || s.Index >= paragraphs.Count)) return false;
            if (metadata.FootnoteMarkers is { } markers &&
                markers.Any(m => m.ParagraphIndex < 0 || m.ParagraphIndex >= paragraphs.Count)) return false;

            if (metadata.ParagraphStyles is { Count: > 0 } tagged)
            {
                foreach (var s in tagged)
                {
                    var p = paragraphs[s.Index];
                    if (s.StyleKey.Length > 0) EditorTags.SetStyleKey(p, s.StyleKey);
                    if (s.FootnoteId != 0) EditorTags.SetFootnoteId(p, s.FootnoteId);
                }
            }

            if (metadata.FootnoteMarkers is { Count: > 0 } marks)
            {
                foreach (var m in marks)
                {
                    var p = paragraphs[m.ParagraphIndex];
                    var pos = p.ContentStart.GetPositionAtOffset(m.Offset);
                    var run = pos?.Parent as Run ?? FindRunAt(p, pos);
                    if (run is not null) EditorTags.SetFootnoteId(run, m.Id);
                }
            }

            return true;
        }

        private static Run? FindRunAt(Paragraph paragraph, TextPointer? pos)
        {
            if (pos is null) return null;
            foreach (var run in DocumentTextMap.EnumerateRuns(paragraph.Inlines))
            {
                if (pos.CompareTo(run.ContentStart) >= 0 && pos.CompareTo(run.ContentEnd) <= 0)
                    return run;
            }
            return null;
        }
    }
}
