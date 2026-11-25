using System.Text.RegularExpressions;
using System.Windows.Documents;

namespace WriterForge.Editor.Services
{
    /// <summary>A single finding: a category, text span, and human-readable detail.</summary>
    public sealed record AnalysisFinding(string Category, int Offset, int Length, string Detail);

    /// <summary>Results of a writing-analysis pass over the document.</summary>
    public sealed class AnalysisReport
    {
        public required List<AnalysisFinding> Findings { get; init; }
        public required Dictionary<string, int> Counts { get; init; }
        public int Total => Findings.Count;
    }

    /// <summary>
    /// Writer-focused analysis: repeated words, repeated sentence/paragraph
    /// starters, duplicate paragraphs, adverbs, and approximate passive voice.
    /// Offsets are into <see cref="DocumentTextMap.Text"/>.
    /// </summary>
    internal static class WriterAnalysisService
    {
        private static readonly Regex RepeatWord = new(
            @"\b(\p{L}[\p{L}'’\-]*)[ \t]+\1\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex Word = new(
            @"[\p{L}][\p{L}'’\-]*",
            RegexOptions.Compiled);

        private static readonly Regex Adverb = new(
            @"\b[\p{Ll}]{3,}ly\b",
            RegexOptions.Compiled);

        private static readonly Regex AuxVerb = new(
            @"\b(?:am|is|are|was|were|be|been|being)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly HashSet<string> NonAdverbs = new(StringComparer.OrdinalIgnoreCase)
        {
            "family", "italy", "july", "holy", "apply", "reply", "supply",
            "ally", "belly", "jelly", "rally", "bully", "silly", "lily",
        };

        private static readonly HashSet<string> IrregularParticiples = new(StringComparer.OrdinalIgnoreCase)
        {
            "made", "taken", "given", "seen", "known", "written", "done",
            "built", "broken", "chosen", "driven", "eaten", "fallen", "grown",
            "hidden", "ridden", "risen", "shaken", "spoken", "stolen", "sworn",
            "torn", "thrown", "worn", "drawn", "flown", "forgotten", "gotten",
            "heard", "held", "kept", "left", "lost", "met", "paid", "said",
            "sold", "sent", "spent", "told", "taught", "thought", "bought",
            "brought", "caught", "fought", "sought", "stood", "understood",
            "wound", "found", "bound", "ground", "hung", "slid", "sped",
        };

        private const int MaxFindings = 500;

        public static AnalysisReport Analyze(FlowDocument document)
        {
            var map = DocumentTextMap.Build(document);
            var findings = new List<AnalysisFinding>();

            FindRepeatedWords(map.Text, findings);
            FindSentenceStarters(map.Text, findings);
            FindParagraphStarters(map.Text, findings);
            FindDuplicateParagraphs(document, map, findings);
            FindAdverbs(map.Text, findings);
            FindPassiveVoice(map.Text, findings);

            var counts = findings
                .GroupBy(f => f.Category)
                .ToDictionary(g => g.Key, g => g.Count());

            if (findings.Count > MaxFindings)
                findings = findings.Take(MaxFindings).ToList();

            return new AnalysisReport { Findings = findings, Counts = counts };
        }

        private static void Add(List<AnalysisFinding> findings, string category, int offset, int length, string detail)
        {
            if (offset >= 0 && length > 0 && findings.Count < MaxFindings)
                findings.Add(new AnalysisFinding(category, offset, length, detail));
        }

        // "the the" — consecutive identical words within a paragraph.
        private static void FindRepeatedWords(string text, List<AnalysisFinding> findings)
        {
            foreach (Match m in RepeatWord.Matches(text))
            {
                var word = m.Groups[1].Value;
                if (word.Length < 2) continue;
                Add(findings, "Repeated word", m.Index, m.Length, $"\"{word}\" repeated");
            }
        }

        // First word of every sentence; words that start 3+ sentences are flagged.
        private static void FindSentenceStarters(string text, List<AnalysisFinding> findings)
        {
            var starters = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            foreach (Match sentence in Regex.Matches(text, @"[^.!?\n]+(?:[.!?]+|$)"))
            {
                var wm = Word.Match(sentence.Value.TrimStart());
                if (!wm.Success) continue;
                var word = wm.Value;
                if (word.Length < 2) continue;
                var offset = sentence.Index + sentence.Value.IndexOf(wm.Value, StringComparison.Ordinal);
                if (!starters.TryGetValue(word, out var list))
                    starters[word] = list = new List<int>();
                list.Add(offset);
            }

            foreach (var (word, offsets) in starters)
            {
                if (offsets.Count < 3) continue;
                foreach (var offset in offsets)
                    Add(findings, "Repeated sentence starter", offset, word.Length, $"\"{word}\" starts {offsets.Count} sentences");
            }
        }

        // First word of every paragraph; words starting 3+ paragraphs are flagged.
        private static void FindParagraphStarters(string text, List<AnalysisFinding> findings)
        {
            var starters = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            var offset = 0;
            foreach (var line in text.Split('\n'))
            {
                var wm = Word.Match(line.TrimStart());
                if (wm.Success && wm.Value.Length >= 2)
                {
                    var wordOffset = offset + line.IndexOf(wm.Value, StringComparison.Ordinal);
                    if (!starters.TryGetValue(wm.Value, out var list))
                        starters[wm.Value] = list = new List<int>();
                    list.Add(wordOffset);
                }
                offset += line.Length + 1;
            }

            foreach (var (word, offsets) in starters)
            {
                if (offsets.Count < 3) continue;
                foreach (var o in offsets)
                    Add(findings, "Repeated paragraph starter", o, word.Length, $"\"{word}\" starts {offsets.Count} paragraphs");
            }
        }

        // Paragraphs with identical normalised text appearing more than once.
        private static void FindDuplicateParagraphs(FlowDocument document, DocumentTextMap map, List<AnalysisFinding> findings)
        {
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var offset = 0;
            foreach (var line in map.Text.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length >= 40)
                {
                    var key = Regex.Replace(trimmed, @"\s+", " ");
                    if (!seen.TryAdd(key, offset))
                    {
                        var firstOffset = seen[key];
                        Add(findings, "Duplicate paragraph", offset + line.IndexOf(trimmed, StringComparison.Ordinal),
                            trimmed.Length, "Identical to an earlier paragraph");
                        _ = firstOffset;
                    }
                }
                offset += line.Length + 1;
            }
        }

        // -ly adverbs; reports one finding per unique word at its first occurrence.
        private static void FindAdverbs(string text, List<AnalysisFinding> findings)
        {
            var counts = new Dictionary<string, (int Count, int FirstOffset)>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Adverb.Matches(text))
            {
                var word = m.Value;
                if (NonAdverbs.Contains(word)) continue;
                if (!counts.TryGetValue(word, out var c))
                    counts[word] = c = (0, m.Index);
                counts[word] = (c.Count + 1, c.FirstOffset);
            }
            foreach (var (word, (count, firstOffset)) in counts.OrderByDescending(kv => kv.Value.Count).Take(50))
                Add(findings, "Adverb", firstOffset, word.Length, $"\"{word}\" used {count}×");
        }

        // Approximate passive voice: a form of "be" followed within 3 words by
        // a past participle (ends -ed/-en or an irregular participle).
        private static void FindPassiveVoice(string text, List<AnalysisFinding> findings)
        {
            var reportedUntil = -1;
            foreach (Match aux in AuxVerb.Matches(text))
            {
                if (aux.Index < reportedUntil) continue;
                var windowEnd = Math.Min(text.Length, aux.Index + aux.Length + 80);
                var window = text.Substring(aux.Index + aux.Length, windowEnd - aux.Index - aux.Length);
                var words = Word.Matches(window).Cast<Match>().Take(4).ToList();
                var participle = words.FirstOrDefault(w => IsParticiple(w.Value));
                if (participle is null) continue;
                var length = aux.Length + (participle.Index + participle.Length);
                Add(findings, "Passive voice", aux.Index, length, $"Possible passive: \"{aux.Value} … {participle.Value}\"");
                reportedUntil = aux.Index + length;
            }
        }

        private static bool IsParticiple(string word)
        {
            if (IrregularParticiples.Contains(word)) return true;
            if (word.Length >= 5 && word.EndsWith("ed", StringComparison.OrdinalIgnoreCase)) return true;
            if (word.Length >= 5 && word.EndsWith("en", StringComparison.OrdinalIgnoreCase)
                && !word.EndsWith("men", StringComparison.OrdinalIgnoreCase)
                && !word.EndsWith("ten", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
