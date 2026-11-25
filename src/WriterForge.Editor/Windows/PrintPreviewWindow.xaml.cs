using System.Windows;
using System.Windows.Documents;

namespace WriterForge.Editor.Windows
{
    /// <summary>Paginated read-only preview of the document.</summary>
    public partial class PrintPreviewWindow : Window
    {
        public PrintPreviewWindow(FlowDocument document)
        {
            InitializeComponent();
            Viewer.Document = document;
        }
    }
}
