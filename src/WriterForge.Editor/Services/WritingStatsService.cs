using System.IO;
using System.Text.Json;

namespace WriterForge.Editor.Services
{
    /// <summary>
    /// Tracks words written per day across all documents. On each save the
    /// word-count delta for that document is added to today's total, so
    /// autosaves accumulate naturally. Stored per-user in
    /// %APPDATA%\WriterForge\writing_stats.json.
    /// </summary>
    public static class WritingStatsService
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WriterForge", "writing_stats.json");

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private sealed class Store
        {
            public int DailyTargetWords { get; set; } = 500;
            public Dictionary<string, int> Days { get; set; } = new();
            public Dictionary<string, int> LastCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }

        public sealed record DayStat(string Date, int Words);

        public sealed record Report(
            int WordsToday,
            int WordsAllTime,
            int StreakDays,
            int DailyTargetWords,
            int Last7Days,
            IReadOnlyList<DayStat> RecentDays);

        /// <summary>Record a document's word count after a save.</summary>
        public static void RecordSave(string documentKey, int wordCount)
        {
            var store = Load();
            var today = DateTime.Today.ToString("yyyy-MM-dd");
            var last = store.LastCounts.TryGetValue(documentKey, out var c) ? c : 0;
            var delta = wordCount - last;
            store.LastCounts[documentKey] = wordCount;

            if (delta != 0)
            {
                store.Days.TryGetValue(today, out var dayTotal);
                store.Days[today] = Math.Max(0, dayTotal + delta);
            }
            Save(store);
        }

        public static int DailyTargetWords
        {
            get => Load().DailyTargetWords;
            set
            {
                var store = Load();
                store.DailyTargetWords = Math.Max(0, value);
                Save(store);
            }
        }

        public static Report GetReport()
        {
            var store = Load();
            var today = DateTime.Today;
            var days = store.Days;

            int WordsOn(DateTime d) =>
                days.TryGetValue(d.ToString("yyyy-MM-dd"), out var w) ? w : 0;

            var todayWords = WordsOn(today);
            var allTime = days.Values.Sum();
            var last7 = Enumerable.Range(0, 7).Sum(i => WordsOn(today.AddDays(-i)));

            var streak = 0;
            var cursor = todayWords > 0 ? today : today.AddDays(-1);
            while (WordsOn(cursor) > 0)
            {
                streak++;
                cursor = cursor.AddDays(-1);
            }

            var recent = Enumerable.Range(0, 30)
                .Select(i => today.AddDays(-i))
                .Select(d => new DayStat(d.ToString("MMM d"), WordsOn(d)))
                .Reverse()
                .ToList();

            return new Report(todayWords, allTime, streak, store.DailyTargetWords, last7, recent);
        }

        private static Store Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<Store>(File.ReadAllText(FilePath)) ?? new Store();
            }
            catch { }
            return new Store();
        }

        private static void Save(Store store)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(store, JsonOptions));
            }
            catch { }
        }
    }
}
