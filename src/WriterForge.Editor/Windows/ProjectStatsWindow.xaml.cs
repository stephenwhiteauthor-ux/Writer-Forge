using System.Windows;
using WriterForge.Editor.Services;

namespace WriterForge.Editor.Windows
{
    /// <summary>Daily word counts, streaks, and writing-target progress.</summary>
    public partial class ProjectStatsWindow : Window
    {
        private sealed record DayRow(string Date, int Words, double BarWidth);

        public ProjectStatsWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => Refresh();
        }

        private void Refresh()
        {
            var report = WritingStatsService.GetReport();
            TodayText.Text = report.WordsToday.ToString("N0");
            StreakText.Text = report.StreakDays.ToString();
            AllTimeText.Text = report.WordsAllTime.ToString("N0");
            WeekText.Text = report.Last7Days.ToString("N0");

            var pct = report.DailyTargetWords > 0
                ? Math.Min(100, report.WordsToday * 100.0 / report.DailyTargetWords)
                : 0;
            TargetBar.Value = pct;
            TargetText.Text = $"Daily target: {report.WordsToday:N0} / {report.DailyTargetWords:N0} words ({pct:F0}%)";
            TargetBox.Text = report.DailyTargetWords.ToString();

            var max = report.RecentDays.Count > 0 ? report.RecentDays.Max(d => d.Words) : 0;
            DaysList.ItemsSource = report.RecentDays
                .Select(d => new DayRow(d.Date, d.Words, max > 0 ? d.Words * 300.0 / max : 0))
                .ToList();
        }

        private void SetTarget_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TargetBox.Text, out var target))
            {
                WritingStatsService.DailyTargetWords = target;
                Refresh();
            }
        }
    }
}
