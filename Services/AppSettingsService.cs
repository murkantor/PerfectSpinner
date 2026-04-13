using System;
using System.IO;
using System.Text.Json;
using PerfectSpinner.Models;

namespace PerfectSpinner.Services
{
    /// <summary>
    /// Persists <see cref="AppSettings"/> to %AppData%\PerfectSpinner\appsettings.json.
    /// All I/O is synchronous and best-effort (failures are silently swallowed).
    /// </summary>
    public sealed class AppSettingsService
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PerfectSpinner", "appsettings.json");

        /// <summary>Directory where per-wheel session JSON files are stored.</summary>
        public static string SessionDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PerfectSpinner", "session");

        public AppSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return new AppSettings();
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        public void Save(AppSettings settings)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, Options));
            }
            catch { /* best-effort */ }
        }
    }
}
