using System;
using System.Diagnostics;
using System.IO;

namespace GoldenSpinner.Services
{
    /// <summary>
    /// Cross-platform audio playback using system audio tools.
    /// Windows  – PowerShell SoundPlayer (WAV) or shell-open (MP3)
    /// macOS    – afplay (WAV + MP3)
    /// Linux    – aplay (WAV) / mpg123 (MP3); install packages as needed
    /// For richer cross-platform MP3 support consider adding LibVLCSharp.
    /// </summary>
    public sealed class AudioService : IDisposable
    {
        private Process? _currentProcess;

        /// <summary>Plays the file at <paramref name="path"/> asynchronously (fire-and-forget).</summary>
        public void PlaySound(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            StopCurrent();

            try
            {
                if (OperatingSystem.IsWindows())
                    PlayWindows(path);
                else if (OperatingSystem.IsMacOS())
                    _currentProcess = StartProcess("afplay", $"\"{path}\"");
                else if (OperatingSystem.IsLinux())
                    PlayLinux(path);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioService] Playback failed: {ex.Message}");
            }
        }

        /// <summary>Stops any currently playing sound.</summary>
        public void StopCurrent()
        {
            try { _currentProcess?.Kill(entireProcessTree: true); } catch { }
            _currentProcess?.Dispose();
            _currentProcess = null;
        }

        private void PlayWindows(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();

            if (ext == ".wav")
            {
                // Use PowerShell's built-in SoundPlayer for WAV (no extra packages needed)
                var escaped = path.Replace("'", "''");
                _currentProcess = StartProcess("powershell.exe",
                    $"-NoProfile -NonInteractive -Command \"(New-Object Media.SoundPlayer '{escaped}').PlaySync()\"");
            }
            else
            {
                // Shell-open MP3/other formats with the default media player
                _currentProcess = StartProcess("cmd.exe", $"/c start \"\" /b \"{path}\"");
            }
        }

        private void PlayLinux(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var (cmd, args) = ext == ".mp3"
                ? ("mpg123", $"\"{path}\"")
                : ("aplay", $"\"{path}\"");
            _currentProcess = StartProcess(cmd, args);
        }

        private static Process? StartProcess(string filename, string arguments)
        {
            try
            {
                return Process.Start(new ProcessStartInfo(filename, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioService] Could not start '{filename}': {ex.Message}");
                return null;
            }
        }

        public void Dispose() => StopCurrent();
    }
}
