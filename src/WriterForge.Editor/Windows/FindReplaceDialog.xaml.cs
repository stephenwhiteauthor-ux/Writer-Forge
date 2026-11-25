using System.Text.RegularExpressions;
using System.Windows;
using WriterForge.Editor.Services;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace WriterForge.Editor.Windows
{
    /// <summary>
    /// Find and replace dialog. Searches within individual runs, so matches
    /// spanning formatting boundaries are not found (v1 limitation).
    /// </summary>
    public partial class FindReplaceDialog : Window
    {
        private static readonly SolidColorBrush MatchBrush = new(Color.FromRgb(255, 242, 204));
        private static readonly SolidColorBrush CurrentBrush = new(Color.FromRgb(255, 204, 128));

        private readonly RichTextBox _editor;
        private readonly List<TextRange> _matches = new();
        private int _currentIndex = -1;
        private string _signature = "";

        public FindReplaceDialog(RichTextBox editor)
        {
            InitializeComponent();
            _editor = editor;
            Owner = Window.GetWindow(editor);
            FindBox.Focus();
            FindBox.SelectAll();
        }

        // ---------- Searching ----------

        private string Signature =>
            $"{FindBox.Text}|{MatchCaseBox.IsChecked}|{WholeWordBox.IsChecked}|{RegexBox.IsChecked}";

        private void EnsureMatches()
        {
            if (Signature == _signature) return;
            _signature = Signature;
            _matches.Clear();
            _currentIndex = -1;

            var query = FindBox.Text;
            if (query.Length == 0) { UpdateStatus(); return; }

            try
            {
                _matches.AddRange(FindAll(query));
            }
            catch (ArgumentException)
            {
                UpdateStatus("Invalid regular expression");
                return;
            }
            UpdateStatus();
        }

        private List<TextRange> FindAll(string query)
        {
            var matches = new List<TextRange>();
            var matchCase = MatchCaseBox.IsChecked == true;
            var wholeWord = WholeWordBox.IsChecked == true;
            var useRegex = RegexBox.IsChecked == true;

            foreach (var run in EnumerateRuns(_editor.Document))
            {
                var text = run.Text;
                if (string.IsNullOrEmpty(text)) continue;

                if (useRegex)
                {
                    var pattern = wholeWord ? $@"\b(?:{query})\b" : query;
                    var options = matchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
                    foreach (Match m in Regex.Matches(text, pattern, options))
                        AddRange(matches, run, m.Index, m.Length);
                }
                else
                {
                    var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                    var index = 0;
                    while (index <= text.Length - query.Length)
                    {
                        var found = text.IndexOf(query, index, comparison);
                        if (found < 0) break;
                        if (!wholeWord || IsWholeWord(text, found, query.Length))
                            AddRange(matches, run, found, query.Length);
                        index = found + Math.Max(1, query.Length);
                    }
                }
            }
            return matches;
        }

        private static void AddRange(List<TextRange> matches, Run run, int start, int length)
        {
            var s = run.ContentStart.GetPositionAtOffset(start);
            var e = run.ContentStart.GetPositionAtOffset(start + length);
            if (s is not null && e is not null)
                matches.Add(new TextRange(s, e));
        }

        private static bool IsWholeWord(string text, int index, int length)
        {
            bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
            var before = index > 0 && IsWordChar(text[index - 1]);
            var after = index + length < text.Length && IsWordChar(text[index + length]);
            return !before && !after;
        }

        private static IEnumerable<Run> EnumerateRuns(FlowDocument document)
        {
            foreach (var paragraph in BlockTree.AllParagraphs(document))
                foreach (var run in EnumerateRuns(paragraph.Inlines))
                    yield return run;
        }

        private static IEnumerable<Run> EnumerateRuns(IEnumerable<Inline> inlines)
        {
            foreach (var inline in inlines)
            {
                if (inline is Run run)
                    yield return run;
                else if (inline is Span span)
                    foreach (var nested in EnumerateRuns(span.Inlines))
                        yield return nested;
                else if (inline is Hyperlink link)
                    foreach (var nested in EnumerateRuns(link.Inlines))
                        yield return nested;
            }
        }

        // ---------- Highlighting / navigation ----------

        private void HighlightCurrent()
        {
            foreach (var range in _matches)
                range.ApplyPropertyValue(TextElement.BackgroundProperty, MatchBrush);

            if (_currentIndex >= 0 && _currentIndex < _matches.Count)
            {
                var current = _matches[_currentIndex];
                current.ApplyPropertyValue(TextElement.BackgroundProperty, CurrentBrush);
                _editor.Selection.Select(current.Start, current.End);
            }
            UpdateStatus();
        }

        private void ClearHighlights()
        {
            foreach (var range in _matches)
                range.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Transparent);
            _matches.Clear();
            _currentIndex = -1;
        }

        private void UpdateStatus(string? message = null)
        {
            StatusText.Text = message ?? (_matches.Count == 0
                ? "No matches"
                : $"{_currentIndex + 1} of {_matches.Count}");
        }

        // ---------- Button handlers ----------

        private void FindNext_Click(object sender, RoutedEventArgs e)
        {
            EnsureMatches();
            if (_matches.Count == 0) return;
            _currentIndex = (_currentIndex + 1) % _matches.Count;
            HighlightCurrent();
        }

        private void Replace_Click(object sender, RoutedEventArgs e)
        {
            EnsureMatches();
            if (_currentIndex < 0 || _currentIndex >= _matches.Count) return;

            _matches[_currentIndex].Text = ReplaceBox.Text;
            _signature = ""; // force rebuild — document changed
            EnsureMatches();
            if (_matches.Count > 0)
            {
                _currentIndex = Math.Min(_currentIndex, _matches.Count - 1);
                HighlightCurrent();
            }
        }

        private void ReplaceAll_Click(object sender, RoutedEventArgs e)
        {
            EnsureMatches();
            var count = _matches.Count;
            if (count == 0) return;

            foreach (var range in _matches)
                range.Text = ReplaceBox.Text;

            _signature = "";
            EnsureMatches();
            _currentIndex = -1;
            HighlightCurrent();
            UpdateStatus($"Replaced {count} occurrence{(count == 1 ? "" : "s")}");
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosed(EventArgs e)
        {
            ClearHighlights();
            base.OnClosed(e);
        }
    }
}
