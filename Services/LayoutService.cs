using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using GoldenSpinner.Models;

namespace GoldenSpinner.Services
{
    /// <summary>
    /// Saves and loads <see cref="WheelLayout"/> objects as JSON files.
    /// Asset paths inside the layout are stored as-is (absolute paths) and
    /// must still exist on disk when the layout is reloaded.
    /// </summary>
    public sealed class LayoutService
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>Serializes <paramref name="layout"/> and writes it to <paramref name="filePath"/>.</summary>
        public async Task SaveAsync(WheelLayout layout, string filePath)
        {
            var json = JsonSerializer.Serialize(layout, SerializerOptions);
            await File.WriteAllTextAsync(filePath, json);
        }

        /// <summary>Reads and deserializes the JSON at <paramref name="filePath"/>.</summary>
        /// <returns>The loaded layout, or null if parsing fails.</returns>
        public async Task<WheelLayout?> LoadAsync(string filePath)
        {
            if (!File.Exists(filePath)) return null;

            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                return JsonSerializer.Deserialize<WheelLayout>(json, SerializerOptions);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LayoutService] Load failed: {ex.Message}");
                return null;
            }
        }
    }
}
