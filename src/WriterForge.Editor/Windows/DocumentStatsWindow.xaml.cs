using System.Windows;
using WriterForge.Editor.Services;

namespace WriterForge.Editor.Windows
{
    /// <summary>Read-only document statistics dialog.</summary>
    public partial class DocumentStatsWindow : Window
    {
        public DocumentStatsWindow(DocumentStatistics stats)
        {
            InitializeComponent();
            StatsText.Text =
                $"Words:            {stats.Words:N0}\n" +
                $"Characters:       {stats.Characters:N0}\n" +
                $"Characters (no spaces): {stats.CharactersNoSpaces:N0}\n" +
                $"Paragraphs:       {stats.Paragraphs:N0}\n" +
                $"Sentences:        {stats.Sentences:N0}\n" +
                $"Pages (est.):     {stats.Pages:N0}\n" +
                $"Reading time:     {DocumentStatsService.FormatTime(stats.ReadingMinutes)}\n" +
                $"Speaking time:    {DocumentStatsService.FormatTime(stats.SpeakingMinutes)}";
        }
    }
}
