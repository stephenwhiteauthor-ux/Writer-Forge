using System.Windows.Controls;

namespace WriterForge.Editor.Controls
{
    /// <summary>
    /// The embeddable manuscript editor. Lives in a class library so
    /// WriterForge.App — and any future host application — can embed it.
    /// </summary>
    public partial class EditorControl : UserControl
    {
        public EditorControl()
        {
            InitializeComponent();
        }

        /// <summary>Access to the underlying document for services (spell check, stats, export).</summary>
        public RichTextBox DocumentSurface => Document;
    }
}
