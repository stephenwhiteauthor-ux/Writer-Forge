using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Documents;

namespace WriterForge.Editor.Services
{
    /// <summary>
    /// Typography cleanup tools. Each method rewrites run text in place and
    /// returns the number of replacements made.
    /// </summary>
    public static class TypographyTools
    {
        private static readonly Regex EllipsisPattern = new(@"(\.\s*){3,}|\.\.\s*\.\.\s*\.", RegexOptions.Compiled);
        private static readonly Regex MultiSpacePattern = new(@"[ \t]{2,}", RegexOptions.Compiled);
        private static readonly Regex SpaceBeforePunctuation = new(@"\s+([,.;:!?])", RegexOptions.Compiled);
        private static readonly Regex SpaceAfterOpening = new(@"([(\[“‘])\s+", RegexOptions.Compiled);

        /// <summary>Convert straight quotes/apostrophes to curly ones. State is tracked across runs.</summary>
        public static int SmartQuotes(FlowDocument document)
        {
            var changes = 0;
            var inDouble = false;
            var inSingle = false;

            foreach (var run in EnumerateRuns(document))
            {
                var text = run.Text;
                if (string.IsNullOrEmpty(text)) continue;

                var sb = new StringBuilder(text.Length);
                var changed = false;

                for (var i = 0; i < text.Length; i++)
                {
                    var c = text[i];
                    if (c == '"')
                    {
                        sb.Append(inDouble ? '”' : '“');
                        inDouble = !inDouble;
                        changed = true;
                    }
                    else if (c == '\'')
                    {
                        var prev = i > 0 ? text[i - 1] : '\0';

                        if (char.IsLetterOrDigit(prev))
                        {
                            // Apostrophe (don't) or possessive (dogs') — never toggles quote state.
                            sb.Append('’');
                            changed = true;
                        }
                        else
                        {
                            sb.Append(inSingle ? '’' : '‘');
                            inSingle = !inSingle;
                            changed = true;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }

                if (changed)
                {
                    run.Text = sb.ToString();
                    changes++;
                }
            }

            return changes;
        }

        /// <summary>Normalise runs of dots (with optional spaces) to a single ellipsis character.</summary>
        public static int NormalizeEllipsis(FlowDocument document)
        {
            return RewriteRuns(document, text =>
            {
                var result = EllipsisPattern.Replace(text, "…");
                return (result, CountChanges(text, result));
            });
        }

        /// <summary>Collapse multiple spaces/tabs and fix space around punctuation.</summary>
        public static int CleanWhitespace(FlowDocument document)
        {
            return RewriteRuns(document, text =>
            {
                var result = text;
                result = MultiSpacePattern.Replace(result, " ");
                result = SpaceBeforePunctuation.Replace(result, "$1");
                result = SpaceAfterOpening.Replace(result, "$1");
                result = result.TrimEnd();
                return (result, CountChanges(text, result));
            });
        }

        /// <summary>Remove paragraphs that contain only whitespace.</summary>
        public static int RemoveEmptyParagraphs(FlowDocument document)
        {
            var removed = 0;
            foreach (var paragraph in document.Blocks.OfType<Paragraph>().ToList())
            {
                var text = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text;
                if (string.IsNullOrWhiteSpace(text))
                {
                    document.Blocks.Remove(paragraph);
                    removed++;
                }
            }
            return removed;
        }

        /// <summary>Run every cleanup tool and return the total number of changes.</summary>
        public static int RunAll(FlowDocument document)
        {
            var total = 0;
            total += SmartQuotes(document);
            total += NormalizeEllipsis(document);
            total += CleanWhitespace(document);
            total += RemoveEmptyParagraphs(document);
            return total;
        }

        // ---------- internals ----------

        private delegate (string Result, int Changes) Rewrite(string text);

        private static int RewriteRuns(FlowDocument document, Rewrite rewrite)
        {
            var changes = 0;
            foreach (var run in EnumerateRuns(document))
            {
                var (result, count) = rewrite(run.Text);
                if (count > 0)
                {
                    run.Text = result;
                    changes += count;
                }
            }
            return changes;
        }

        private static int CountChanges(string before, string after)
        {
            // Approximate: number of characters that differ in position isn't reliable
            // after regex edits, so count removed/inserted characters instead.
            return Math.Abs(before.Length - after.Length) + CountCharDifferences(before, after);
        }

        private static int CountCharDifferences(string a, string b)
        {
            var count = 0;
            var min = Math.Min(a.Length, b.Length);
            for (var i = 0; i < min; i++)
                if (a[i] != b[i]) count++;
            return count;
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
                {
                    yield return run;
                }
                else if (inline is Span span)
                {
                    foreach (var nested in EnumerateRuns(span.Inlines))
                        yield return nested;
                }
                else if (inline is Hyperlink hyperlink)
                {
                    foreach (var nested in EnumerateRuns(hyperlink.Inlines))
                        yield return nested;
                }
            }
        }
    }
}
