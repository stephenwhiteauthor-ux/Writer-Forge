using System.Net.Http;
using System.Text.Json;
using PleOps.LanguageTool.Client;
using PleOps.LanguageTool.Client.TextCheck;

namespace WriterForge.Editor.Services
{
    public sealed class GrammarIssue
    {
        public string? Message { get; set; }
        public string? ShortMessage { get; set; }
        public int Offset { get; set; }
        public int Length { get; set; }
        public List<string> Replacements { get; set; } = new();
    }

    /// <summary>
    /// Grammar checking through a LanguageTool server.
    ///
    /// Primary path: PleOps.LanguageTool.Client (the NuGet package).
    /// Fallback path: the same REST endpoint via HttpClient — needed because the public
    /// server reports a non-numeric version that the typed client cannot parse.
    ///
    /// LanguageTool is a service, not an offline library. Point <see cref="BaseAddress"/>
    /// at a self-hosted server (e.g. http://localhost:8010/v2) for unlimited/offline use.
    /// </summary>
    public static class GrammarCheckService
    {
        public const string PublicApi = "https://api.languagetool.org/v2";
        private const int MaxTextLength = 20000;

        private static readonly object SyncRoot = new();
        private static LanguageToolClient? client;
        private static bool preferRestFallback;
        private static bool? isAvailable;
        private static DateTime lastCallUtc = DateTime.MinValue;
        private static DateTime lastFailureUtc = DateTime.MinValue;

        /// <summary>LanguageTool server base URL (…/v2/check is appended for REST calls).</summary>
        public static string BaseAddress { get; set; } = PublicApi;

        /// <summary>Minimum milliseconds between calls to the shared public API.</summary>
        public static int MinIntervalMs { get; set; } = 3000;

        public static bool IsAvailable =>
            (isAvailable ?? true) || DateTime.UtcNow - lastFailureUtc > TimeSpan.FromSeconds(60);

        public static string? LastError { get; private set; }

        public static List<GrammarIssue> Check(string text)
        {
            var issues = new List<GrammarIssue>();
            LastError = null;

            if (string.IsNullOrWhiteSpace(text)) return issues;

            var payload = text.Length > MaxTextLength ? text[..MaxTextLength] : text;

            try
            {
                Throttle();

                if (!preferRestFallback)
                {
                    try
                    {
                        issues = CheckWithClient(payload);
                        isAvailable = true;
                        return issues;
                    }
                    catch (FormatException)
                    {
                        // Non-numeric server version: the typed client cannot deserialise it.
                        preferRestFallback = true;
                    }
                }

                issues = CheckWithRest(payload);
                isAvailable = true;
            }
            catch (Exception ex)
            {
                var root = ex is AggregateException aggregate ? aggregate.GetBaseException() : ex;
                isAvailable = false;
                lastFailureUtc = DateTime.UtcNow;
                LastError = "LanguageTool unavailable at " + BaseAddress + ": " + root.Message;
            }

            return issues;
        }

        public static void MarkUnavailable(string? reason)
        {
            isAvailable = false;
            if (!string.IsNullOrEmpty(reason)) LastError = reason;
        }

        /// <summary>Ping the LanguageTool server's /languages endpoint.</summary>
        public static async Task<(bool Ok, string? Error)> PingAsync()
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                using var response = await http.GetAsync(BaseAddress.TrimEnd('/') + "/languages");
                response.EnsureSuccessStatusCode();
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private static List<GrammarIssue> CheckWithClient(string text)
        {
            var request = Task.Run(() => GetClient().CheckPlainTextAsync(text, "en-US", false));

            var finished = Task.WhenAny(request, Task.Delay(TimeSpan.FromSeconds(15))).GetAwaiter().GetResult();
            if (!ReferenceEquals(finished, request))
                throw new TimeoutException("LanguageTool did not respond within 15 seconds.");

            var result = request.GetAwaiter().GetResult();
            var issues = new List<GrammarIssue>();

            if (result?.Matches is not null)
            {
                foreach (var match in result.Matches)
                {
                    issues.Add(new GrammarIssue
                    {
                        Message = match.Message,
                        ShortMessage = match.ShortMessage,
                        Offset = match.MatchOffset,
                        Length = match.MatchLength,
                        Replacements = match.Replacements?.ToList() ?? new List<string>(),
                    });
                }
            }

            return issues;
        }

        private static List<GrammarIssue> CheckWithRest(string text)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

            var form = new Dictionary<string, string>
            {
                ["text"] = text,
                ["language"] = "en-US",
            };
            using var content = new FormUrlEncodedContent(form);
            using var response = http.PostAsync(BaseAddress.TrimEnd('/') + "/check", content).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();

            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
            var issues = new List<GrammarIssue>();

            if (doc.RootElement.TryGetProperty("matches", out var matches) && matches.ValueKind == JsonValueKind.Array)
            {
                foreach (var match in matches.EnumerateArray())
                {
                    issues.Add(new GrammarIssue
                    {
                        Message = match.TryGetProperty("message", out var msg) ? msg.GetString() : null,
                        ShortMessage = match.TryGetProperty("shortMessage", out var sm) ? sm.GetString() : null,
                        Offset = match.TryGetProperty("offset", out var off) ? off.GetInt32() : 0,
                        Length = match.TryGetProperty("length", out var len) ? len.GetInt32() : 0,
                        Replacements = match.TryGetProperty("replacements", out var repl) && repl.ValueKind == JsonValueKind.Array
                            ? repl.EnumerateArray()
                                .Select(r => r.TryGetProperty("value", out var v) ? v.GetString() : null)
                                .Where(v => v is not null)
                                .Cast<string>()
                                .ToList()
                            : new List<string>(),
                    });
                }
            }

            return issues;
        }

        private static void Throttle()
        {
            if (MinIntervalMs <= 0) return;
            if (!string.Equals(BaseAddress, PublicApi, StringComparison.OrdinalIgnoreCase)) return;

            var wait = MinIntervalMs - (int)(DateTime.UtcNow - lastCallUtc).TotalMilliseconds;
            if (wait > 0) Thread.Sleep(wait);
            lastCallUtc = DateTime.UtcNow;
        }

        private static LanguageToolClient GetClient()
        {
            lock (SyncRoot)
            {
                return client ??= LanguageToolClientFactory.Create(BaseAddress);
            }
        }
    }
}
