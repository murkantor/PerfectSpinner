using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using GoldenSpinner.Models;

namespace GoldenSpinner.Services
{
    /// <summary>
    /// Saves and loads <see cref="WheelLayout"/> objects.
    ///
    /// Two formats are supported:
    ///
    ///   .json  — plain JSON; asset paths are stored as absolute paths on disk.
    ///            Layouts are not portable if the user moves files.
    ///
    ///   .zip   — self-contained bundle.  Structure:
    ///              layout.json       (relative asset paths: "img/foo.png", "snd/bar.mp3")
    ///              img/              (copies of every referenced image)
    ///              snd/              (copies of every referenced sound)
    ///            Fully portable — share the .zip and everything just works.
    ///
    /// When a .zip is loaded, assets are extracted to a per-session temp folder under
    /// %TEMP%/GoldenSpinner/<guid>/ so that existing image/sound loading code is unchanged.
    /// </summary>
    public sealed class LayoutService
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // ── JSON ─────────────────────────────────────────────────────────────

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

        // ── ZIP ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Saves <paramref name="layout"/> as a self-contained ZIP bundle.
        /// Asset files are copied into <c>img/</c> and <c>snd/</c> entries;
        /// the embedded <c>layout.json</c> uses relative paths.
        /// Missing or inaccessible asset files are silently skipped.
        /// </summary>
        public async Task SaveZipAsync(WheelLayout layout, string filePath)
        {
            // Build mapping: absolute source path → zip entry name (deduped)
            var imageMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var soundMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Build a parallel list of slices with relative paths for the embedded JSON.
            var zipSlices = new List<WheelSlice>(layout.Slices.Count);
            foreach (var slice in layout.Slices)
            {
                zipSlices.Add(new WheelSlice
                {
                    Id          = slice.Id,
                    Label       = slice.Label,
                    ColorHex    = slice.ColorHex,
                    Weight      = slice.Weight,
                    IsActive    = slice.IsActive,
                    WinnerLabel = slice.WinnerLabel,
                    ImagePath   = AssignEntry(slice.ImagePath, "img", imageMap),
                    SoundPath   = AssignEntry(slice.SoundPath, "snd", soundMap),
                });
            }

            var zipLayout = new WheelLayout
            {
                Name                  = layout.Name,
                Slices                = zipSlices,
                SpinDurationSeconds   = layout.SpinDurationSeconds,
                Friction              = layout.Friction,
                SliceImageMode        = layout.SliceImageMode,
                ShowLabels            = layout.ShowLabels,
                ShowPointerLabel      = layout.ShowPointerLabel,
                LabelFontIndex        = layout.LabelFontIndex,
                LabelFontSize         = layout.LabelFontSize,
                LabelColorStyle       = layout.LabelColorStyle,
                LabelBold             = layout.LabelBold,
                ChromaKeyColor        = layout.ChromaKeyColor,
                UseWeightedSlices     = layout.UseWeightedSlices,
                GlobalWeight          = layout.GlobalWeight,
                LogSpins              = layout.LogSpins,
                WinnerMessageTemplate = layout.WinnerMessageTemplate,
                DefaultSoundPath      = AssignEntry(layout.DefaultSoundPath, "snd", soundMap),
                BrightenWinner        = layout.BrightenWinner,
                DarkenLosers          = layout.DarkenLosers,
                InvertLoserText       = layout.InvertLoserText,
                BorderColorStyle      = layout.BorderColorStyle,
                BlackoutWheelMode     = layout.BlackoutWheelMode,
                ShowConfetti          = layout.ShowConfetti,
                ConfettiImagePath     = AssignEntry(layout.ConfettiImagePath, "img", imageMap),
                SpinStartSoundPath    = AssignEntry(layout.SpinStartSoundPath, "snd", soundMap),
                TickSound1Path        = AssignEntry(layout.TickSound1Path, "snd", soundMap),
                TickSound2Path        = AssignEntry(layout.TickSound2Path, "snd", soundMap),
            };

            await using var zipStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false);

            // Write layout.json
            var json      = JsonSerializer.Serialize(zipLayout, SerializerOptions);
            var jsonEntry = archive.CreateEntry("layout.json", CompressionLevel.Fastest);
            await using (var s = jsonEntry.Open())
            await using (var w = new StreamWriter(s, Encoding.UTF8, leaveOpen: true))
                await w.WriteAsync(json);

            // Write asset files (images/audio are already compressed — skip re-compression)
            foreach (var (src, entryName) in imageMap)
                await CopyFileToZip(archive, src, entryName);
            foreach (var (src, entryName) in soundMap)
                await CopyFileToZip(archive, src, entryName);
        }

        /// <summary>
        /// Loads a ZIP bundle, extracting its contents to a temp folder and remapping
        /// asset paths so the rest of the app sees normal absolute file paths.
        /// </summary>
        public async Task<WheelLayout?> LoadZipAsync(string filePath)
        {
            if (!File.Exists(filePath)) return null;

            try
            {
                var tempDir = Path.Combine(
                    Path.GetTempPath(), "GoldenSpinner", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);

                // Extract everything — images, sounds, layout.json
                using (var zipStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var archive  = new ZipArchive(zipStream, ZipArchiveMode.Read))
                    archive.ExtractToDirectory(tempDir, overwriteFiles: true);

                var jsonPath = Path.Combine(tempDir, "layout.json");
                if (!File.Exists(jsonPath)) return null;

                var json   = await File.ReadAllTextAsync(jsonPath);
                var layout = JsonSerializer.Deserialize<WheelLayout>(json, SerializerOptions);
                if (layout == null) return null;

                // Remap relative paths ("img/foo.png") → absolute temp paths
                foreach (var slice in layout.Slices)
                {
                    slice.ImagePath = ResolveAsset(tempDir, slice.ImagePath);
                    slice.SoundPath = ResolveAsset(tempDir, slice.SoundPath);
                }
                layout.DefaultSoundPath    = ResolveAsset(tempDir, layout.DefaultSoundPath);
                layout.SpinStartSoundPath  = ResolveAsset(tempDir, layout.SpinStartSoundPath);
                layout.TickSound1Path      = ResolveAsset(tempDir, layout.TickSound1Path);
                layout.TickSound2Path      = ResolveAsset(tempDir, layout.TickSound2Path);
                layout.ConfettiImagePath   = ResolveAsset(tempDir, layout.ConfettiImagePath);

                return layout;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LayoutService] LoadZip failed: {ex.Message}");
                return null;
            }
        }

        // ── private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Returns a zip entry name for <paramref name="srcPath"/> inside
        /// <paramref name="folder"/>, registering it in <paramref name="map"/>.
        /// Returns null if the source file does not exist.
        /// Deduplicates by appending _1, _2 … when two different files share a basename.
        /// </summary>
        private static string? AssignEntry(string? srcPath, string folder,
            Dictionary<string, string> map)
        {
            if (string.IsNullOrEmpty(srcPath) || !File.Exists(srcPath)) return null;

            // Same source file already registered — reuse the same entry name.
            if (map.TryGetValue(srcPath, out var existing)) return existing;

            var baseName  = Path.GetFileNameWithoutExtension(srcPath);
            var ext       = Path.GetExtension(srcPath);
            var entryName = $"{folder}/{baseName}{ext}";
            int idx       = 1;

            // Deduplicate: two different files with the same basename
            while (map.ContainsValue(entryName))
                entryName = $"{folder}/{baseName}_{idx++}{ext}";

            map[srcPath] = entryName;
            return entryName;
        }

        private static async Task CopyFileToZip(ZipArchive archive, string srcPath, string entryName)
        {
            if (!File.Exists(srcPath)) return;
            var entry = archive.CreateEntry(entryName, CompressionLevel.NoCompression);
            await using var entryStream = entry.Open();
            await using var fileStream  = new FileStream(
                srcPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            await fileStream.CopyToAsync(entryStream);
        }

        private static string? ResolveAsset(string tempDir, string? relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return null;
            var abs = Path.Combine(tempDir,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(abs) ? abs : null;
        }
    }
}
