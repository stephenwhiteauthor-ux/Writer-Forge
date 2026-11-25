using System.Windows;
using WriterForge.Editor.Services;

namespace WriterForge.Editor.Windows
{
    /// <summary>Per-user application settings dialog.</summary>
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings _settings;

        public SettingsWindow(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings;
            LanguageToolBox.Text = settings.LanguageToolUrl;
            SpellCheckBox.IsChecked = settings.SpellCheckEnabled;
            GrammarCheckBox.IsChecked = settings.GrammarCheckEnabled;
        }

        /// <summary>The edited settings after OK is pressed.</summary>
        public AppSettings Settings => _settings;

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            _settings.LanguageToolUrl = LanguageToolBox.Text.Trim();
            _settings.SpellCheckEnabled = SpellCheckBox.IsChecked == true;
            _settings.GrammarCheckEnabled = GrammarCheckBox.IsChecked == true;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private async void Test_Click(object sender, RoutedEventArgs e)
        {
            var url = LanguageToolBox.Text.Trim();
            var previous = GrammarCheckService.BaseAddress;
            if (url.Length > 0) GrammarCheckService.BaseAddress = url;

            TestStatus.Text = "Testing…";
            TestStatus.Foreground = System.Windows.Media.Brushes.Gray;
            IsEnabled = false;
            try
            {
                var (ok, error) = await GrammarCheckService.PingAsync();
                TestStatus.Text = ok ? "Connection OK" : $"Failed: {error}";
                TestStatus.Foreground = ok
                    ? System.Windows.Media.Brushes.DarkGreen
                    : System.Windows.Media.Brushes.DarkRed;
            }
            finally
            {
                GrammarCheckService.BaseAddress = previous;
                IsEnabled = true;
            }
        }
    }
}
