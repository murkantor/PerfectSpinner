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
        // ── Windows playback (NAudio) — main channel ─────────────────────────
        private WaveOutEvent? _waveOut;
        private WaveStream?   _reader;

        // ── Windows playback (NAudio) — spin-start channel ───────────────────
        //    Dedicated channel so the spin-start sound is never interrupted by ticks.
        private WaveOutEvent? _spinStartWaveOut;
        private WaveStream?   _spinStartReader;

        // ── Windows playback (NAudio) — two tick channels (A and B) ─────────
        //    Sound 1 always plays on channel A, sound 2 always plays on channel B.
        //    Neither channel ever stops the other, so fast spins don't cut sounds off.
        private WaveOutEvent? _tickWaveOutA;
        private WaveStream?   _tickReaderA;
        private WaveOutEvent? _tickWaveOutB;
        private WaveStream?   _tickReaderB;

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

        /// <summary>
        /// Plays the spin-start sound on its own dedicated channel.
        /// Tick sounds and winner sounds are unaffected.
        /// </summary>
        public void PlaySpinStartSound(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            if (!OperatingSystem.IsWindows()) return;

            try
            {
                _spinStartWaveOut?.Stop();
                _spinStartWaveOut?.Dispose();
                _spinStartReader?.Dispose();

                _spinStartReader  = new AudioFileReader(path);
                _spinStartWaveOut = new WaveOutEvent();
                _spinStartWaveOut.Init(_spinStartReader);
                _spinStartWaveOut.PlaybackStopped += (_, _) =>
                {
                    _spinStartWaveOut?.Dispose(); _spinStartWaveOut = null;
                    _spinStartReader?.Dispose();  _spinStartReader  = null;
                };
                _spinStartWaveOut.Play();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioService] Spin-start playback failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Plays a tick sound on one of two independent channels.
        /// <paramref name="channelB"/> selects which channel to use — alternate this on every
        /// crossing so that neither sound ever stops the other, even at high spin speeds.
        /// Does not interrupt the main winner-sound channel.
        /// </summary>
        public void PlayTickSound(string path, bool channelB)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            if (!OperatingSystem.IsWindows()) return; // macOS/Linux: skip rather than spawn processes

            try
            {
                if (channelB)
                {
                    // Restart channel B only (channel A keeps playing)
                    _tickWaveOutB?.Stop();
                    _tickWaveOutB?.Dispose();
                    _tickReaderB?.Dispose();
                    _tickReaderB  = new AudioFileReader(path);
                    _tickWaveOutB = new WaveOutEvent();
                    _tickWaveOutB.Init(_tickReaderB);
                    _tickWaveOutB.PlaybackStopped += (_, _) =>
                    {
                        _tickWaveOutB?.Dispose(); _tickWaveOutB = null;
                        _tickReaderB?.Dispose();  _tickReaderB  = null;
                    };
                    _tickWaveOutB.Play();
                }
                else
                {
                    // Restart channel A only (channel B keeps playing)
                    _tickWaveOutA?.Stop();
                    _tickWaveOutA?.Dispose();
                    _tickReaderA?.Dispose();
                    _tickReaderA  = new AudioFileReader(path);
                    _tickWaveOutA = new WaveOutEvent();
                    _tickWaveOutA.Init(_tickReaderA);
                    _tickWaveOutA.PlaybackStopped += (_, _) =>
                    {
                        _tickWaveOutA?.Dispose(); _tickWaveOutA = null;
                        _tickReaderA?.Dispose();  _tickReaderA  = null;
                    };
                    _tickWaveOutA.Play();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioService] Tick playback failed: {ex.Message}");
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

        public void Dispose()
        {
            StopCurrent();
            _spinStartWaveOut?.Stop(); _spinStartWaveOut?.Dispose(); _spinStartReader?.Dispose();
            _tickWaveOutA?.Stop(); _tickWaveOutA?.Dispose(); _tickReaderA?.Dispose();
            _tickWaveOutB?.Stop(); _tickWaveOutB?.Dispose(); _tickReaderB?.Dispose();
        }
    }
}
