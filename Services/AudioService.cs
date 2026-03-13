using System;
using System.Diagnostics;
using System.IO;
using NAudio.Wave;

namespace GoldenSpinner.Services
{
    /// <summary>
    /// Audio playback service.
    /// Windows  – NAudio (WaveOutEvent + AudioFileReader): supports WAV and MP3
    ///            natively using Windows' built-in codecs, no extra processes.
    /// macOS    – afplay (WAV + MP3)
    /// Linux    – aplay (WAV) / mpg123 (MP3); install packages as needed
    /// </summary>
    public sealed class AudioService : IDisposable
    {
        // ── Windows playback (NAudio) ─────────────────────────────────────────
        private WaveOutEvent? _waveOut;
        private WaveStream?   _reader;

        // ── macOS / Linux playback (external process) ─────────────────────────
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
            // Stop NAudio playback
            _waveOut?.Stop();
            _waveOut?.Dispose();
            _waveOut = null;
            _reader?.Dispose();
            _reader = null;

            // Stop process-based playback (macOS / Linux)
            try { _currentProcess?.Kill(entireProcessTree: true); } catch { }
            _currentProcess?.Dispose();
            _currentProcess = null;
        }

        // ── Platform implementations ──────────────────────────────────────────

        private void PlayWindows(string path)
        {
            // AudioFileReader handles both WAV and MP3 (and more) using Windows
            // built-in ACM/Media Foundation codecs — no external process needed.
            _reader  = new AudioFileReader(path);
            _waveOut = new WaveOutEvent();
            _waveOut.Init(_reader);

            // Clean up automatically when playback finishes naturally.
            _waveOut.PlaybackStopped += OnPlaybackStopped;
            _waveOut.Play();
        }

        private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            _waveOut?.Dispose();
            _waveOut = null;
            _reader?.Dispose();
            _reader = null;
        }

        private void PlayLinux(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var (cmd, args) = ext == ".mp3"
                ? ("mpg123", $"\"{path}\"")
                : ("aplay",  $"\"{path}\"");
            _currentProcess = StartProcess(cmd, args);
        }

        private static Process? StartProcess(string filename, string arguments)
        {
            try
            {
                return Process.Start(new ProcessStartInfo(filename, arguments)
                {
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true
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
