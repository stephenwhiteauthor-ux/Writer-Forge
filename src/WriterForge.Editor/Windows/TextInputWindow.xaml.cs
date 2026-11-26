using System.Windows;

namespace WriterForge.Editor.Windows
{
    /// <summary>Simple single-line text prompt dialog.</summary>
    public partial class TextInputWindow : Window
    {
        public TextInputWindow(string title, string prompt, string initial = "")
        {
            InitializeComponent();
            Title = title;
            PromptText.Text = prompt;
            InputBox.Text = initial;
            Loaded += (_, _) => { InputBox.Focus(); InputBox.SelectAll(); };
        }

        public string Value => InputBox.Text.Trim();

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (Value.Length == 0) return;
            DialogResult = true;
            Close();
        }
    }
}
