using System;
using System.Diagnostics;
using System.IO;
using NAudio.Wave;

namespace PerfectSpinner.Services
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
        //    Readers and WaveOutEvents are kept alive between crossings; only the path
        //    change triggers a full reload.  Each crossing just seeks to 0 and replays,
        //    eliminating the per-crossing file-open and device-handle creation cost.
        private WaveOutEvent? _tickWaveOutA;
        private WaveStream?   _tickReaderA;
        private string?       _tickPathA;
        private WaveOutEvent? _tickWaveOutB;
        private WaveStream?   _tickReaderB;
        private string?       _tickPathB;

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
        /// The WaveOutEvent and reader are kept alive between crossings; each call just
        /// seeks back to position 0 and replays, avoiding per-crossing file I/O and
        /// audio device handle creation.
        /// </summary>
        public void PlayTickSound(string path, bool channelB)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            if (!OperatingSystem.IsWindows()) return;

            try
            {
                if (channelB)
                    PlayTickChannel(path, ref _tickWaveOutB, ref _tickReaderB, ref _tickPathB);
                else
                    PlayTickChannel(path, ref _tickWaveOutA, ref _tickReaderA, ref _tickPathA);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioService] Tick playback failed: {ex.Message}");
            }
        }

        private static void PlayTickChannel(
            string path,
            ref WaveOutEvent? waveOut,
            ref WaveStream?   reader,
            ref string?       loadedPath)
        {
            // Reload only when the path has changed.
            if (loadedPath != path)
            {
                waveOut?.Stop();
                waveOut?.Dispose();
                reader?.Dispose();
                waveOut     = null;
                reader      = null;
                loadedPath  = null;

                reader    = new AudioFileReader(path);
                waveOut   = new WaveOutEvent();
                waveOut.Init(reader);
                loadedPath = path;
            }

            // Seek to the start and play from the beginning of the sound.
            waveOut!.Stop();
            reader!.Position = 0;
            waveOut.Play();
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
            _tickPathA = null; _tickPathB = null;
        }
    }
}
