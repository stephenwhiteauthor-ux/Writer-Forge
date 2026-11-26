using System.Windows;
using System.Windows.Documents;

namespace WriterForge.Editor.Windows
{
    /// <summary>Paginated read-only preview (FixedDocument with headers/footers).</summary>
    public partial class PrintPreviewWindow : Window
    {
        public PrintPreviewWindow(FixedDocument document)
        {
            InitializeComponent();
            Viewer.Document = document;
        }
    }
}
