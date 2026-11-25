using System.Windows;
using Microsoft.Win32;
using WriterForge.Editor.Controls;
using WriterForge.Editor.Services;

namespace WriterForge.Editor.Windows
{
    /// <summary>
    /// Record, save, load, and replay editor action macros (.wmacro files).
    /// </summary>
    public partial class MacroRecorderWindow : Window
    {
        private readonly EditorControl _editor;
        private List<MacroAction> _pending = new();
        private readonly System.Windows.Threading.DispatcherTimer _refreshTimer;

        public MacroRecorderWindow(EditorControl editor)
        {
            InitializeComponent();
            _editor = editor;

            _refreshTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(400),
            };
            _refreshTimer.Tick += (_, _) => RefreshList();
            _refreshTimer.Start();
            Closed += (_, _) =>
            {
                _refreshTimer.Stop();
                if (MacroRecorder.IsRecording) MacroRecorder.Cancel();
            };
            RefreshList();
        }

        private void Record_Click(object sender, RoutedEventArgs e)
        {
            if (!MacroRecorder.IsRecording)
            {
                MacroRecorder.Start();
                RecordButton.Content = "■ Stop";
                StatusText.Text = "Recording — use the formatting toolbar, then Stop.";
            }
            else
            {
                _pending = MacroRecorder.Stop();
                RecordButton.Content = "● Record";
                StatusText.Text = _pending.Count == 0
                    ? "Recording stopped — no actions captured."
                    : $"Recording stopped — {_pending.Count} action(s) captured.";
                RefreshList();
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var actions = MacroRecorder.IsRecording ? MacroRecorder.Actions : _pending;
            if (actions.Count == 0)
            {
                StatusText.Text = "Nothing to save — record or load actions first.";
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Save Macro",
                Filter = "WriterForge macro (*.wmacro)|*.wmacro",
                DefaultExt = ".wmacro",
                FileName = NameBox.Text.Trim(),
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                MacroRecorder.Save(dialog.FileName, NameBox.Text.Trim(), actions);
                StatusText.Text = $"Saved {actions.Count} action(s) to {System.IO.Path.GetFileName(dialog.FileName)}.";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Save failed: {ex.Message}";
            }
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Open Macro",
                Filter = "WriterForge macro (*.wmacro)|*.wmacro",
            };
            if (dialog.ShowDialog(this) != true) return;

            var loaded = MacroRecorder.Load(dialog.FileName);
            if (loaded is null)
            {
                StatusText.Text = "Could not read that macro file.";
                return;
            }

            NameBox.Text = loaded.Value.Name;
            _pending = loaded.Value.Actions;
            StatusText.Text = $"Loaded {loaded.Value.Actions.Count} action(s).";
            RefreshList();
        }

        private void Play_Click(object sender, RoutedEventArgs e)
        {
            if (_pending.Count == 0)
            {
                StatusText.Text = "No actions to play — record or open a macro first.";
                return;
            }
            _editor.PlayMacro(_pending);
            StatusText.Text = $"Played {_pending.Count} action(s).";
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            _pending = new List<MacroAction>();
            StatusText.Text = "Cleared.";
            RefreshList();
        }

        private void RefreshList()
        {
            var source = MacroRecorder.IsRecording ? MacroRecorder.Actions : _pending;
            ActionsList.ItemsSource = source.Select(a =>
                a.Arg is null ? a.Name : $"{a.Name}: {a.Arg}").ToList();
        }
    }
}
