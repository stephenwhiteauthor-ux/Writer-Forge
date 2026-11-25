using System.Text.RegularExpressions;
using System.Windows.Documents;

namespace WriterForge.Editor.Services
{
    public sealed record DocumentStatistics(
        int Words,
        int Characters,
        int CharactersNoSpaces,
        int Paragraphs,
        int Sentences,
        int Pages,
        double ReadingMinutes,
        double SpeakingMinutes);

    public static class DocumentStatsService
    {
        private const int WordsPerPage = 250;
        private const double ReadingWpm = 265;
        private const double SpeakingWpm = 140;

        private static readonly Regex WordPattern = new(@"[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*", RegexOptions.Compiled);
        private static readonly Regex SentencePattern = new(@"[.!?…]+(?=\s|$)", RegexOptions.Compiled);

        public static DocumentStatistics Analyze(FlowDocument document)
        {
            var text = new TextRange(document.ContentStart, document.ContentEnd).Text;

            var words = WordPattern.Matches(text).Count;
            var sentences = Math.Max(1, SentencePattern.Matches(text).Count);
            var pages = Math.Max(1, (int)Math.Ceiling(words / (double)WordsPerPage));

            return new DocumentStatistics(
                Words: words,
                Characters: text.Length,
                CharactersNoSpaces: text.Count(c => !char.IsWhiteSpace(c)),
                Paragraphs: BlockTree.AllParagraphs(document).Count(),
                Sentences: sentences,
                Pages: pages,
                ReadingMinutes: words / ReadingWpm,
                SpeakingMinutes: words / SpeakingWpm);
        }

        public static string FormatTime(double minutes)
        {
            if (minutes < 1) return $"{minutes * 60:F0} sec";
            var h = (int)minutes / 60;
            var m = (int)minutes % 60;
            return h > 0 ? $"{h}h {m}m" : $"{m}m {minutes * 60 % 60:F0}s";
        }
    }
}
