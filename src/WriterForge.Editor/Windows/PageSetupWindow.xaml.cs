using System.Windows;
using System.Windows.Controls;

namespace WriterForge.Editor.Windows
{
    /// <summary>Page size, orientation, and margin settings for the document.</summary>
    public partial class PageSetupWindow : Window
    {
        private const double DipsPerInch = 96.0;

        public PageSetupWindow()
        {
            InitializeComponent();
        }

        /// <summary>Page size in DIPs after OK.</summary>
        public Size PageSize { get; private set; }

        /// <summary>Page margins in DIPs after OK.</summary>
        public Thickness Margins { get; private set; }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var (w, h) = ((SizeBox.SelectedItem as ComboBoxItem)?.Tag as string) switch
            {
                "a4" => (8.27, 11.69),
                "legal" => (8.5, 14.0),
                _ => (8.5, 11.0),
            };
            if (LandscapeButton.IsChecked == true) (w, h) = (h, w);
            PageSize = new Size(w * DipsPerInch, h * DipsPerInch);

            var marginInches = double.TryParse((MarginBox.SelectedItem as ComboBoxItem)?.Tag as string, out var m)
                ? m
                : 1.0;
            var dip = marginInches * DipsPerInch;
            Margins = new Thickness(dip);

            DialogResult = true;
            Close();
        }
    }
}
