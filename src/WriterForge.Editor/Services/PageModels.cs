using System.Windows;
using System.Windows.Documents;
using System.Windows.Media.TextFormatting;

namespace WriterForge.Editor.Services
{
    /// <summary>One laid-out page of the document.</summary>
    internal sealed class PageModel
    {
        public required int Index { get; init; }
        public required Size Size { get; init; }
        public required Thickness Padding { get; init; }
        /// <summary>Lines in vertical order; <see cref="LineModel.Top"/> is relative to the padding box.</summary>
        public List<LineModel> Lines { get; } = new();
        /// <summary>Footnote/endnote placements pinned to the bottom of the page.</summary>
        public List<FootnotePlacement> Footnotes { get; } = new();
        /// <summary>Reserved height at the bottom for footnotes (0 when none).</summary>
        public double FootnoteAreaHeight { get; set; }
        /// <summary>Running header/footer text (resolved later with page numbers).</summary>
        public string? Header { get; set; }
        public string? Footer { get; set; }

        public double ContentWidth => Size.Width - Padding.Left - Padding.Right;
        public double ContentHeight => Size.Height - Padding.Top - Padding.Bottom;
        /// <summary>Y (in padding-box coords) below which body text must not flow.</summary>
        public double ContentBottom => ContentHeight - FootnoteAreaHeight;
    }

    /// <summary>A single typeset line belonging to a paragraph.</summary>
    internal sealed class LineModel
    {
        public required Paragraph Paragraph { get; init; }
        /// <summary>Y position relative to the page's padding box.</summary>
        public required double Top { get; set; }
        public required double Height { get; init; }
        /// <summary>Char offsets into the paragraph's plain text.</summary>
        public int StartOffset { get; init; }
        public int Length { get; init; }
        /// <summary>Distance from line top to text baseline (approximation).</summary>
        public double Baseline { get; init; }
        /// <summary>True when this is the paragraph's last line.</summary>
        public bool IsLastInParagraph { get; init; }
        /// <summary>Footnote markers whose position falls inside this line.</summary>
        public List<MarkerSpan> Markers { get; } = new();
        /// <summary>Script (super/subscript) ranges overlapping this line, in paragraph-text coords.</summary>
        public List<(CharSpan Span, BaselineAlignment Kind)> Scripts { get; } = new();
    }

    /// <summary>A footnote/endnote rendered at the bottom of a page.</summary>
    internal sealed class FootnotePlacement
    {
        public required int Id { get; init; }
        public required int Number { get; init; }
        /// <summary>Vertical position of the note's first line (padding-box coords).</summary>
        public required double Top { get; set; }
        public required double Height { get; init; }
        /// <summary>The note's typeset lines.</summary>
        public required List<LineModel> Lines { get; init; }
    }
}
