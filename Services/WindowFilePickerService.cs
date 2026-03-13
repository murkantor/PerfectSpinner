using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace GoldenSpinner.Services
{
    /// <summary>
    /// Implements <see cref="IFilePickerService"/> using Avalonia's cross-platform
    /// <see cref="IStorageProvider"/> API.  Must be constructed with the host <see cref="Window"/>
    /// so it can resolve the <see cref="TopLevel"/> and its storage provider.
    /// </summary>
    public sealed class WindowFilePickerService : IFilePickerService
    {
        private readonly Window _window;

        public WindowFilePickerService(Window window) => _window = window;

        // ── helpers ──────────────────────────────────────────────────────────

        private IStorageProvider? StorageProvider =>
            TopLevel.GetTopLevel(_window)?.StorageProvider;

        private static readonly FilePickerFileType ImageFilter = new("Images")
        {
            Patterns = ["*.png", "*.jpg", "*.jpeg"],
            MimeTypes = ["image/png", "image/jpeg"]
        };

        private static readonly FilePickerFileType AudioFilter = new("Audio")
        {
            Patterns = ["*.wav", "*.mp3"],
            MimeTypes = ["audio/wav", "audio/mpeg"]
        };

        private static readonly FilePickerFileType LayoutFilter = new("Wheel Layout")
        {
            Patterns = ["*.json"],
            MimeTypes = ["application/json"]
        };

        // ── IFilePickerService ────────────────────────────────────────────────

        public Task<string?> OpenImageFileAsync() =>
            PickOpenFileAsync("Select Image", [ImageFilter]);

        public Task<string?> OpenSoundFileAsync() =>
            PickOpenFileAsync("Select Sound", [AudioFilter]);

        public Task<string?> OpenLayoutFileAsync() =>
            PickOpenFileAsync("Load Wheel Layout", [LayoutFilter]);

        public async Task<string?> SaveLayoutFileAsync(string defaultName)
        {
            var provider = StorageProvider;
            if (provider == null) return null;

            var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Wheel Layout",
                DefaultExtension = "json",
                SuggestedFileName = defaultName,
                FileTypeChoices = [LayoutFilter]
            });

            return file?.TryGetLocalPath();
        }

        // ── private helpers ───────────────────────────────────────────────────

        private async Task<string?> PickOpenFileAsync(string title, IReadOnlyList<FilePickerFileType> types)
        {
            var provider = StorageProvider;
            if (provider == null) return null;

            var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = types
            });

            return files.FirstOrDefault()?.TryGetLocalPath();
        }
    }
}
