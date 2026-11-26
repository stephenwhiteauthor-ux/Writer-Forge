using System.Windows;
using System.Windows.Controls;

namespace WriterForge.Editor.Windows
{
    /// <summary>Rows/columns picker for table insertion.</summary>
    public partial class InsertTableWindow : Window
    {
        public InsertTableWindow()
        {
            InitializeComponent();
        }

        public int Rows { get; private set; } = 3;
        public int Columns { get; private set; } = 3;

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            Rows = Clamp(RowsBox.Text, 3);
            Columns = Clamp(ColsBox.Text, 10);
            DialogResult = true;
            Close();
        }

        private static int Clamp(string text, int max) =>
            int.TryParse(text, out var v) ? Math.Clamp(v, 1, max) : 3;
    }
}
