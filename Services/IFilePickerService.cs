using System.Threading.Tasks;

namespace PerfectSpinner.Services
{
    /// <summary>
    /// Abstraction over platform file-picker dialogs.
    /// Implemented by <see cref="WindowFilePickerService"/> using Avalonia's StorageProvider.
    /// The ViewModel depends on this interface so it stays free of UI references.
    /// </summary>
    public interface IFilePickerService
    {
        Task<string?> OpenImageFileAsync();
        Task<string?> OpenConfettiFileAsync();
        Task<string?> OpenSoundFileAsync();
        Task<string?> OpenLayoutFileAsync();
        Task<string?> SaveLayoutFileAsync(string defaultName);
    }
}
