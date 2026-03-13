using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GoldenSpinner.Models;
using GoldenSpinner.Services;

namespace GoldenSpinner.ViewModels
{
    /// <summary>
    /// Primary ViewModel.  Owns the slice collection, animation state,
    /// and all commands exposed to the view.
    /// </summary>
    public partial class MainWindowViewModel : ViewModelBase
    {
        // ── Services ──────────────────────────────────────────────────────────

        private readonly IFilePickerService _picker;
        private readonly LayoutService _layoutService;
        private readonly AudioService _audioService;

        // ── Observable state ─────────────────────────────────────────────────

        [ObservableProperty] private ObservableCollection<WheelSliceViewModel> _slices = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSelectedSlice))]
        [NotifyCanExecuteChangedFor(nameof(RemoveSliceCommand))]
        [NotifyCanExecuteChangedFor(nameof(MoveUpCommand))]
        [NotifyCanExecuteChangedFor(nameof(MoveDownCommand))]
        [NotifyCanExecuteChangedFor(nameof(AssignImageCommand))]
        [NotifyCanExecuteChangedFor(nameof(RemoveImageCommand))]
        [NotifyCanExecuteChangedFor(nameof(AssignSoundCommand))]
        [NotifyCanExecuteChangedFor(nameof(RemoveSoundCommand))]
        private WheelSliceViewModel? _selectedSlice;

        [ObservableProperty] private double _currentRotation;
        [ObservableProperty] private int _winnerIndex = -1;
        [ObservableProperty] private string _winnerMessage = string.Empty;

        /// <summary>
        /// "Speed" setting (1–30).  Controls the peak angular velocity of the wheel:
        /// higher = faster spin = longer free-spin coast.
        /// Peak velocity = SpinDurationSeconds × 180 °/s.
        /// The name is kept as SpinDurationSeconds for save-file compatibility.
        /// </summary>
        [ObservableProperty] private decimal _spinDurationSeconds = 4m;

        /// <summary>
        /// Friction level 1–10 applied during the free-spin coast.
        ///   1 = near-frictionless (coasts a long time)
        ///  10 = heavy friction   (stops quickly)
        /// </summary>
        [ObservableProperty] private int _friction = 5;

        /// <summary>
        /// Background colour of the Spinner window used for OBS chromakey capture.
        /// Any valid CSS hex string. Defaults to broadcast-safe green.
        /// </summary>
        [ObservableProperty] private string _chromaKeyColor = "#00B140";

        // ── Derived ───────────────────────────────────────────────────────────

        public bool HasSelectedSlice => SelectedSlice != null;

        // ── Physics animation state ───────────────────────────────────────────
        //
        // Spin is now driven by a velocity simulation rather than a fixed-target
        // easing curve.  Phases (all times in seconds from _animStart):
        //
        //  [0 → _windUpDuration]   Phase 1 – wind-up: backward, ease-in
        //  [_windUpDuration → _accelEndTime]  Phase 2 – acceleration: backward→peak
        //  [_accelEndTime → _fullSpeedEndTime] Phase 3 – full speed: cruise at peak
        //  [_fullSpeedEndTime → _halfSpeedEndTime] Phase 4 – engine-off: linear ½ peak
        //  [_halfSpeedEndTime → stop]  Phase 5 – free spin: exponential friction decay
        //
        // Winner is read from CurrentRotation when velocity drops below threshold.

        private DispatcherTimer?  _animTimer;
        private DateTimeOffset    _animStart;
        private DateTimeOffset    _lastTickTime;
        private TaskCompletionSource? _spinTcs;
        private bool              _spinCancelled;

        private double _peakVelocity;       // deg/s at maximum forward speed
        private double _windUpDuration;     // seconds of backward wind-up phase
        private double _accelEndTime;       // seconds: acceleration phase ends
        private double _fullSpeedEndTime;   // seconds: full-speed cruise ends
        private double _halfSpeedEndTime;   // seconds: powered phase ends
        private double _windUpSpeed;        // backward deg/s at peak of wind-up
        private double _currentVelocity;    // deg/s — negative means backward
        private bool   _inFreeSpin;

        // ── Default slice colours (rotate through these when adding slices) ───

        private static readonly string[] PaletteColors =
        [
            "#E74C3C", "#3498DB", "#2ECC71", "#F39C12",
            "#9B59B6", "#1ABC9C", "#E67E22", "#34495E",
            "#E91E63", "#00BCD4", "#8BC34A", "#FF5722"
        ];

        // ── Constructor ───────────────────────────────────────────────────────

        public MainWindowViewModel(
            IFilePickerService picker,
            LayoutService layoutService,
            AudioService audioService)
        {
            _picker = picker;
            _layoutService = layoutService;
            _audioService = audioService;

            AddDefaultSlices();
        }

        // ── CanExecute predicates ─────────────────────────────────────────────

        private bool CanSpinWheel() => Slices.Count >= 1;
        private bool HasSelection() => SelectedSlice != null;
        private bool CanMoveUp() => SelectedSlice != null && Slices.IndexOf(SelectedSlice) > 0;
        private bool CanMoveDown() => SelectedSlice != null && Slices.IndexOf(SelectedSlice) < Slices.Count - 1;
        private bool HasSelectionImagePath() => SelectedSlice?.ImagePath != null;
        private bool HasSelectionSoundPath() => SelectedSlice?.SoundPath != null;

        // ── Commands ──────────────────────────────────────────────────────────

        /// <summary>
        /// Starts the physics-based spin.  Awaits until the wheel comes to a
        /// complete stop (free-spin coast included), keeping the button disabled
        /// for the full duration.
        ///
        /// Animation phases:
        ///   1. Wind-up  — wheel reverses (2–5 % of powered duration)
        ///   2. Accel    — from reverse through zero to peak velocity (up to 10 %)
        ///   3. Cruise   — full peak velocity (10 % → 80 %)
        ///   4. Engine off — linear drop to ½ peak (80 % → 100 %)
        ///   5. Free spin — exponential friction decay until velocity &lt; 0.5 °/s
        ///
        /// Winner is whichever slice is under the pointer when the wheel stops.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanSpinWheel))]
        private async Task SpinWheelAsync()
        {
            if (Slices.Count == 0) return;

            WinnerMessage = string.Empty;
            WinnerIndex   = -1;

            var rng           = new Random();
            var totalDuration = Math.Max(1.0, (double)SpinDurationSeconds);

            // Peak velocity scales with the Speed setting (higher = faster wheel).
            _peakVelocity = totalDuration * 180.0;   // deg/s

            // Wind-up: random 2–5 % of the powered duration, constant backward speed.
            _windUpDuration = (0.02 + rng.NextDouble() * 0.03) * totalDuration;
            _windUpSpeed    = 60.0;   // deg/s backward

            // Phase time boundaries (seconds from _animStart).
            _accelEndTime     = 0.10 * totalDuration;
            _fullSpeedEndTime = 0.80 * totalDuration;
            _halfSpeedEndTime = totalDuration;

            _inFreeSpin      = false;
            _currentVelocity = 0.0;
            _spinCancelled   = false;

            _animStart    = DateTimeOffset.UtcNow;
            _lastTickTime = _animStart;

            _spinTcs = new TaskCompletionSource();

            _animTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)   // ~60 fps
            };
            _animTimer.Tick += OnAnimationTick;
            _animTimer.Start();

            await _spinTcs.Task;

            if (_spinCancelled) return;

            // Winner = slice under the pointer at rest.
            var n            = Slices.Count;
            var sliceDeg     = 360.0 / n;
            var pointerAngle = ((360.0 - CurrentRotation % 360.0) % 360.0 + 360.0) % 360.0;
            var winnerIdx    = (int)(pointerAngle / sliceDeg) % n;

            var winner = Slices[winnerIdx];
            WinnerIndex   = winnerIdx;
            WinnerMessage = $"🎉  {winner.Label}!";

            if (!string.IsNullOrEmpty(winner.SoundPath))
                _audioService.PlaySound(winner.SoundPath);
        }

        [RelayCommand]
        private void ResetWheel()
        {
            // Stop any running animation cleanly.
            if (_animTimer != null)
            {
                _animTimer.Stop();
                _animTimer.Tick -= OnAnimationTick;
                _animTimer = null;
            }

            _spinCancelled   = true;
            _currentVelocity = 0.0;
            _inFreeSpin      = false;
            _spinTcs?.TrySetResult();

            CurrentRotation = 0;
            WinnerIndex     = -1;
            WinnerMessage   = string.Empty;
        }

        [RelayCommand]
        private void AddSlice()
        {
            var color = PaletteColors[Slices.Count % PaletteColors.Length];
            var slice = new WheelSliceViewModel
            {
                Label = $"Slice {Slices.Count + 1}",
                ColorHex = color
            };
            Slices.Add(slice);
            SelectedSlice = slice;
            SpinWheelCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(HasSelection))]
        private void RemoveSlice()
        {
            if (SelectedSlice == null) return;
            var idx = Slices.IndexOf(SelectedSlice);
            Slices.Remove(SelectedSlice);
            SelectedSlice = Slices.Count > 0
                ? Slices[Math.Min(idx, Slices.Count - 1)]
                : null;
            SpinWheelCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(CanMoveUp))]
        private void MoveUp()
        {
            if (SelectedSlice == null) return;
            var idx = Slices.IndexOf(SelectedSlice);
            if (idx <= 0) return;
            Slices.Move(idx, idx - 1);
            NotifyMoveCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(CanMoveDown))]
        private void MoveDown()
        {
            if (SelectedSlice == null) return;
            var idx = Slices.IndexOf(SelectedSlice);
            if (idx < 0 || idx >= Slices.Count - 1) return;
            Slices.Move(idx, idx + 1);
            NotifyMoveCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(HasSelection))]
        private async Task AssignImageAsync()
        {
            if (SelectedSlice == null) return;
            var path = await _picker.OpenImageFileAsync();
            if (path != null)
            {
                SelectedSlice.ImagePath = path;
                RemoveImageCommand.NotifyCanExecuteChanged();
            }
        }

        [RelayCommand(CanExecute = nameof(HasSelectionImagePath))]
        private void RemoveImage()
        {
            if (SelectedSlice == null) return;
            SelectedSlice.ImagePath = null;
            RemoveImageCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(HasSelection))]
        private async Task AssignSoundAsync()
        {
            if (SelectedSlice == null) return;
            var path = await _picker.OpenSoundFileAsync();
            if (path != null)
            {
                SelectedSlice.SoundPath = path;
                RemoveSoundCommand.NotifyCanExecuteChanged();
            }
        }

        [RelayCommand(CanExecute = nameof(HasSelectionSoundPath))]
        private void RemoveSound()
        {
            if (SelectedSlice == null) return;
            SelectedSlice.SoundPath = null;
            RemoveSoundCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        private async Task SaveLayoutAsync()
        {
            var path = await _picker.SaveLayoutFileAsync("my-wheel");
            if (path == null) return;

            var layout = new WheelLayout
            {
                Name = "My Wheel",
                SpinDurationSeconds = (double)SpinDurationSeconds,
                Slices = Slices.Select(s => s.ToModel()).ToList()
            };

            await _layoutService.SaveAsync(layout, path);
        }

        [RelayCommand]
        private async Task LoadLayoutAsync()
        {
            var path = await _picker.OpenLayoutFileAsync();
            if (path == null) return;

            var layout = await _layoutService.LoadAsync(path);
            if (layout == null) return;

            Slices.Clear();
            foreach (var model in layout.Slices)
                Slices.Add(new WheelSliceViewModel(model));

            SpinDurationSeconds = (decimal)Math.Max(1.0, layout.SpinDurationSeconds);
            CurrentRotation = 0;
            WinnerMessage = string.Empty;
            WinnerIndex = -1;
            SelectedSlice = Slices.FirstOrDefault();
            SpinWheelCommand.NotifyCanExecuteChanged();
        }

        // ── Animation tick ────────────────────────────────────────────────────

        private void OnAnimationTick(object? sender, EventArgs e)
        {
            var now     = DateTimeOffset.UtcNow;
            var elapsed = (now - _animStart).TotalSeconds;
            var dt      = Math.Min((now - _lastTickTime).TotalSeconds, 0.05); // cap at 50 ms
            _lastTickTime = now;

            if (!_inFreeSpin)
            {
                if (elapsed < _windUpDuration)
                {
                    // Phase 1: ease-in to backward wind-up speed.
                    var t = elapsed / _windUpDuration;
                    _currentVelocity = -_windUpSpeed * (t * t);
                }
                else if (elapsed < _accelEndTime)
                {
                    // Phase 2: ease-in acceleration from -windUpSpeed to +peakVelocity.
                    var span = _accelEndTime - _windUpDuration;
                    var t    = (elapsed - _windUpDuration) / span;
                    _currentVelocity = Lerp(-_windUpSpeed, _peakVelocity, t * t);
                }
                else if (elapsed < _fullSpeedEndTime)
                {
                    // Phase 3: cruise at peak velocity.
                    _currentVelocity = _peakVelocity;
                }
                else if (elapsed < _halfSpeedEndTime)
                {
                    // Phase 4: linear deceleration from peak to half-peak.
                    var t = (elapsed - _fullSpeedEndTime)
                          / (_halfSpeedEndTime - _fullSpeedEndTime);
                    _currentVelocity = _peakVelocity * (1.0 - 0.5 * t);
                }
                else
                {
                    // Hand off to free spin at half peak velocity.
                    _currentVelocity = _peakVelocity / 2.0;
                    _inFreeSpin      = true;
                }
            }
            else
            {
                // Phase 5: exponential friction decay.
                // Friction 1 = gentle coast (rate ≈ 0.20/s)
                // Friction 10 = quick stop  (rate ≈ 2.72/s)
                var frictionRate = 0.20 + (Friction - 1) * 0.28;
                _currentVelocity *= (1.0 - frictionRate * dt);

                if (_currentVelocity < 0.5)
                {
                    FinishSpin();
                    return;
                }
            }

            CurrentRotation += _currentVelocity * dt;
        }

        private void FinishSpin()
        {
            _animTimer!.Stop();
            _animTimer.Tick -= OnAnimationTick;
            _animTimer       = null;
            _currentVelocity = 0.0;
            _spinTcs?.TrySetResult();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static double Lerp(double a, double b, double t) =>
            a + (b - a) * Math.Clamp(t, 0.0, 1.0);

        private void NotifyMoveCanExecuteChanged()
        {
            MoveUpCommand.NotifyCanExecuteChanged();
            MoveDownCommand.NotifyCanExecuteChanged();
        }

        private void AddDefaultSlices()
        {
            string[] names = ["Prize 1", "Prize 2", "Prize 3", "Prize 4", "Prize 5", "Prize 6"];
            for (int i = 0; i < names.Length; i++)
            {
                Slices.Add(new WheelSliceViewModel
                {
                    Label = names[i],
                    ColorHex = PaletteColors[i % PaletteColors.Length]
                });
            }
            SelectedSlice = Slices[0];
        }
    }
}
