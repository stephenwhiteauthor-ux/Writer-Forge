using System.IO;
using System.Text.Json;

namespace WriterForge.Editor.Services
{
    /// <summary>
    /// Per-user application settings, persisted as JSON under
    /// %APPDATA%\WriterForge\settings.json.
    /// </summary>
    public sealed class AppSettings
    {
        /// <summary>LanguageTool server base URL (…/v2). Empty = public API.</summary>
        public string LanguageToolUrl { get; set; } = "";

        public bool SpellCheckEnabled { get; set; } = true;
        public bool GrammarCheckEnabled { get; set; } = true;

        private static string SettingsDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WriterForge");

        private static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch
            {
                // Corrupt settings fall back to defaults.
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(SettingsDirectory);
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // Settings persistence failure is non-fatal.
            }
        }
    }
}
