using System.Windows.Documents;

namespace WriterForge.Editor.Services
{
    /// <summary>
    /// Footnote/endnote model. A footnote is a superscript marker run in the
    /// text (tagged with <see cref="EditorTags.FootnoteIdProperty"/>) plus a
    /// note paragraph at the end of the document (StyleKey "Footnote" or
    /// "Endnote"). Notes currently render at the document end; page-bottom
    /// placement awaits the pagination engine.
    /// </summary>
    internal static class FootnoteService
    {
        public const string FootnoteStyleKey = "Footnote";
        public const string EndnoteStyleKey = "Endnote";

        public sealed record Footnote(int Id, Run Marker, Paragraph Note);

        /// <summary>All footnotes/endnotes in document order (by marker position).</summary>
        public static List<Footnote> Collect(FlowDocument document)
        {
            var markers = new List<(int Id, Run Run)>();
            foreach (var run in DocumentTextMap.EnumerateRuns(document))
            {
                var id = EditorTags.GetFootnoteId(run);
                if (id != 0) markers.Add((id, run));
            }

            var notes = BlockTree.AllParagraphs(document)
                .Where(p => EditorTags.GetStyleKey(p) is FootnoteStyleKey or EndnoteStyleKey)
                .ToDictionary(p => EditorTags.GetFootnoteId(p), p => p);

            var result = new List<Footnote>();
            var seen = new HashSet<int>();
            foreach (var (id, run) in markers)
            {
                if (!seen.Add(id)) continue;
                notes.TryGetValue(id, out var note);
                result.Add(new Footnote(id, run, note!));
            }
            // Notes whose markers were deleted still get listed last.
            foreach (var (id, note) in notes)
                if (seen.Add(id))
                    result.Add(new Footnote(id, null!, note));
            return result;
        }

        /// <summary>Next unused footnote id.</summary>
        public static int NextId(FlowDocument document)
        {
            var max = 0;
            foreach (var run in DocumentTextMap.EnumerateRuns(document))
                max = Math.Max(max, EditorTags.GetFootnoteId(run));
            foreach (var p in BlockTree.AllParagraphs(document))
                max = Math.Max(max, EditorTags.GetFootnoteId(p));
            return max + 1;
        }

        /// <summary>Rewrite marker numbers and note prefixes as 1..n in document order.</summary>
        public static void Renumber(FlowDocument document)
        {
            var footnotes = Collect(document);
            for (var i = 0; i < footnotes.Count; i++)
            {
                var n = i + 1;
                var f = footnotes[i];
                if (f.Marker is not null) f.Marker.Text = n.ToString();
                if (f.Note is not null)
                {
                    var prefix = f.Note.Inlines.OfType<Run>().FirstOrDefault();
                    if (prefix is not null) prefix.Text = $"{n}. ";
                }
            }
        }

        /// <summary>Ordered note texts for output (PDF/print), numbered 1..n.</summary>
        public static List<string> NotesForOutput(FlowDocument document)
        {
            Renumber(document);
            return Collect(document)
                .Select(f => f.Note is null ? "" : PlainText(f.Note).TrimStart())
                .Where(t => t.Length > 0)
                .ToList();
        }

        /// <summary>"Endnotes" if the document uses endnote style, else "Footnotes".</summary>
        public static string NotesHeading(FlowDocument document) =>
            BlockTree.AllParagraphs(document).Any(p => EditorTags.GetStyleKey(p) == EndnoteStyleKey)
                ? "Endnotes"
                : "Footnotes";

        /// <summary>Toggle every note paragraph between Footnote and Endnote style.</summary>
        public static void ConvertAll(FlowDocument document)
        {
            var toEndnote = !BlockTree.AllParagraphs(document)
                .Any(p => EditorTags.GetStyleKey(p) == EndnoteStyleKey);
            foreach (var p in BlockTree.AllParagraphs(document))
            {
                var key = EditorTags.GetStyleKey(p);
                if (key == FootnoteStyleKey || key == EndnoteStyleKey)
                    EditorTags.SetStyleKey(p, toEndnote ? EndnoteStyleKey : FootnoteStyleKey);
            }
        }

        public static bool IsNoteParagraph(Paragraph p) =>
            EditorTags.GetStyleKey(p) is FootnoteStyleKey or EndnoteStyleKey;

        private static string PlainText(Paragraph p) =>
            string.Concat(DocumentTextMap.EnumerateRuns(p.Inlines).Select(r => r.Text));
    }
}
