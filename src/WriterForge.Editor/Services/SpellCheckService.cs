using System.IO;
using WeCantSpell.Hunspell;

namespace WriterForge.Editor.Services
{
    /// <summary>
    /// Hunspell-based spell checker. Looks for en_US.aff/.dic under
    /// Dictionaries/ (or Global/Dictionaries/) in the application base directory.
    /// </summary>
    public sealed class SpellCheckService
    {
        private static SpellCheckService? _instance;
        private static readonly object SyncRoot = new();

        public static SpellCheckService Instance
        {
            get
            {
                lock (SyncRoot)
                {
                    return _instance ??= new SpellCheckService();
                }
            }
        }

        private readonly WordList? _hunspell;
        private readonly HashSet<string> _sessionIgnored = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _userDictionary = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _userDictionaryPath;

        public bool IsAvailable => _hunspell is not null;

        private SpellCheckService()
        {
            _userDictionaryPath = Path.Combine(AppContext.BaseDirectory, "Dictionaries", "spell_whitelist.txt");
            LoadUserDictionary();

            var dictDir = FindDictionaryDirectory();
            if (dictDir is null) return;

            var aff = Path.Combine(dictDir, "en_US.aff");
            var dic = Path.Combine(dictDir, "en_US.dic");
            if (!File.Exists(aff) || !File.Exists(dic)) return;

            try
            {
                _hunspell = WordList.CreateFromFiles(dic, aff);
            }
            catch
            {
                _hunspell = null;
            }
        }

        private static string? FindDictionaryDirectory()
        {
            var baseDir = AppContext.BaseDirectory;
            foreach (var candidate in new[]
            {
                Path.Combine(baseDir, "Dictionaries"),
                Path.Combine(baseDir, "Global", "Dictionaries"),
            })
            {
                if (File.Exists(Path.Combine(candidate, "en_US.aff")) &&
                    File.Exists(Path.Combine(candidate, "en_US.dic")))
                {
                    return candidate;
                }
            }
            return null;
        }

        private void LoadUserDictionary()
        {
            try
            {
                if (!File.Exists(_userDictionaryPath)) return;
                foreach (var line in File.ReadAllLines(_userDictionaryPath))
                {
                    var word = line.Trim();
                    if (word.Length > 0) _userDictionary.Add(word);
                }
            }
            catch
            {
                // A missing/unreadable user dictionary shouldn't break spell checking.
            }
        }

        /// <summary>True when the word should NOT be flagged.</summary>
        public bool Check(string word)
        {
            if (_sessionIgnored.Contains(word) || _userDictionary.Contains(word)) return true;
            if (_hunspell is null) return false;
            try
            {
                return _hunspell.Check(word);
            }
            catch
            {
                return false;
            }
        }

        public List<string> Suggest(string word)
        {
            var suggestions = new List<string>();
            if (_hunspell is null) return suggestions;
            try
            {
                var results = _hunspell.Suggest(word);
                if (results is not null) suggestions.AddRange(results);
            }
            catch
            {
                // Suggestion failures are non-fatal.
            }
            return suggestions;
        }

        /// <summary>Ignore a word for the rest of the session.</summary>
        public void Ignore(string word) => _sessionIgnored.Add(word);

        /// <summary>Add a word to the persistent user dictionary.</summary>
        public void AddToDictionary(string word)
        {
            if (string.IsNullOrWhiteSpace(word)) return;
            _userDictionary.Add(word);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_userDictionaryPath)!);
                File.AppendAllText(_userDictionaryPath, word + Environment.NewLine);
            }
            catch
            {
                // Persistence failure is non-fatal; the word stays ignored this session.
            }
        }
    }
}
