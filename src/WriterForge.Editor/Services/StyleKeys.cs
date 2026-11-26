namespace WriterForge.Editor.Services
{
    /// <summary>
    /// Normalises paragraph style keys to heading levels. Tags may be stored
    /// either as "Heading1" (docx-imported/legacy) or "StyleHeading1"
    /// (ribbon-applied resource key) — both map to the same level.
    /// </summary>
    internal static class StyleKeys
    {
        /// <summary>Heading level 1–3, or 0 for non-heading paragraphs.</summary>
        public static int HeadingLevel(string? key) => key switch
        {
            "Heading1" or "StyleHeading1" => 1,
            "Heading2" or "StyleHeading2" => 2,
            "Heading3" or "StyleHeading3" => 3,
            _ => 0,
        };

        /// <summary>Style tag for a heading level (1–3); null for body text.</summary>
        public static string? KeyForLevel(int level) => level switch
        {
            1 => "StyleHeading1",
            2 => "StyleHeading2",
            3 => "StyleHeading3",
            _ => null,
        };

        /// <summary>True for footnote/endnote paragraph styles (excluded from outline/page flow).</summary>
        public static bool IsNoteStyle(string? key) =>
            key is FootnoteService.FootnoteStyleKey or FootnoteService.EndnoteStyleKey;
    }
}
