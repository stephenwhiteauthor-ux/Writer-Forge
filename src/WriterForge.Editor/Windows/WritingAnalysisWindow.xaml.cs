using System.Windows;
using WriterForge.Editor.Controls;
using WriterForge.Editor.Services;

namespace WriterForge.Editor.Windows
{
    /// <summary>
    /// Runs the writing-analysis pass and lists findings; can highlight them
    /// in the document.
    /// </summary>
    public partial class WritingAnalysisWindow : Window
    {
        private sealed record Row(string Display);

        private readonly EditorControl _editor;
        private AnalysisReport? _report;

        public WritingAnalysisWindow(EditorControl editor)
        {
            InitializeComponent();
            _editor = editor;
            Loaded += (_, _) => RunAnalysis();
        }

        private void RunAnalysis()
        {
            _report = _editor.AnalyzeDocument();
            var counts = string.Join("    ",
                _report.Counts.OrderByDescending(kv => kv.Value)
                    .Select(kv => $"{kv.Key}: {kv.Value}"));
            SummaryText.Text = _report.Total == 0
                ? "No issues found."
                : $"{_report.Total} finding(s) — {counts}";
            FindingsList.ItemsSource = _report.Findings
                .Select(f => new Row($"{f.Category}: {f.Detail}"))
                .ToList();
        }

        private void Highlight_Click(object sender, RoutedEventArgs e)
        {
            if (_report is null) return;
            _editor.HighlightFindings(_report.Findings);
        }

        private void Clear_Click(object sender, RoutedEventArgs e) => _editor.ClearAnalysisHighlights();
    }
}
