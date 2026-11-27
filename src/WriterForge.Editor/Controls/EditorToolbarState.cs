using System.Windows;

namespace WriterForge.Editor.Controls
{
    /// <summary>
    /// Snapshot of the editor's formatting state, raised whenever the caret
    /// or selection changes. The ribbon (in the app shell) binds to this.
    /// </summary>
    public sealed class EditorToolbarState
    {
        public bool CanUndo { get; init; }
        public bool CanRedo { get; init; }

        public bool Bold { get; init; }
        public bool Italic { get; init; }
        public bool Underline { get; init; }
        public bool Strikethrough { get; init; }
        public bool Subscript { get; init; }
        public bool Superscript { get; init; }

        public string? FontFamily { get; init; }
        public double? FontSize { get; init; }
        public string? StyleKey { get; init; }
        public TextAlignment? Alignment { get; init; }

        public bool Bullets { get; init; }
        public bool Numbers { get; init; }

        /// <summary>Caret is inside a table cell (drives the contextual Table tab).</summary>
        public bool InTable { get; init; }
    }
}
