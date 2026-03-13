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

        /// <summary>Spin duration in seconds (shown in the NumericUpDown).</summary>
        [ObservableProperty] private decimal _spinDurationSeconds = 4m;

        /// <summary>
        /// Inertia level 1–10.  Controls how many extra full rotations the wheel
        /// makes before landing.  Each level maps to a random range so the wheel
        /// never lands in exactly the same spot twice.
        ///   Level 1  →  2–3 extra spins
        ///   Level 5  → 10–15 extra spins
        ///   Level 10 → 20–30 extra spins
        /// </summary>
        [ObservableProperty] private int _inertia = 5;

        /// <summary>
        /// Background colour of the Spinner window used for OBS chromakey capture.
        /// Any valid CSS hex string. Defaults to broadcast-safe green.
        /// </summary>
        [ObservableProperty] private string _chromaKeyColor = "#00B140";

        // ── Derived ───────────────────────────────────────────────────────────

        public bool HasSelectedSlice => SelectedSlice != null;

        // ── Spin animation state ──────────────────────────────────────────────

        private DispatcherTimer? _animTimer;
        private double _animStartAngle;
        private double _animTargetAngle;
        private DateTimeOffset _animStart;
        private TimeSpan _animDuration;
        private int _pendingWinnerIndex;
        private TaskCompletionSource? _spinTcs;

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
        /// Starts the spin animation.  Returns only after the animation finishes
        /// so the RelayCommand keeps the button disabled throughout.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanSpinWheel))]
        private async Task SpinWheelAsync()
        {
            var n = Slices.Count;
            if (n == 0) return;

            WinnerMessage = string.Empty;
            WinnerIndex = -1;

            var rng = new Random();
            _pendingWinnerIndex = rng.Next(n);

            // ── Calculate target rotation ──────────────────────────────────
            // Wheel is drawn with slice 0 starting at -90° (top), going clockwise.
            // After rotating the canvas by R degrees, the pointer (fixed at top)
            // sees canvas angle:  pointerAngle = (360 - R%360) % 360
            // Slice i occupies:   [i*sliceDeg, (i+1)*sliceDeg)
            // To land on slice w's centre:  targetMod = 360 - (w + 0.5)*sliceDeg
            var sliceDeg = 360.0 / n;
            var targetMod = (360.0 - (_pendingWinnerIndex + 0.5) * sliceDeg % 360.0 + 360.0) % 360.0;

            // Add a small random wobble (±30 % of one slice) for variety
            var wobble = (rng.NextDouble() - 0.5) * sliceDeg * 0.6;
            targetMod = ((targetMod + wobble) % 360.0 + 360.0) % 360.0;

            var currentMod = ((CurrentRotation % 360.0) + 360.0) % 360.0;
            var delta = (targetMod - currentMod + 360.0) % 360.0;

            // Ensure at least one full rotation beyond the target offset
            if (delta < sliceDeg) delta += 360.0;
            // Inertia 1–10: minSpins = inertia×2, plus 0–inertia random bonus spins.
            // Level 1 → 2–3 spins; Level 5 → 10–15; Level 10 → 20–30.
            var clampedInertia = Math.Clamp(Inertia, 1, 10);
            delta += (clampedInertia * 2 + rng.Next(clampedInertia + 1)) * 360.0;

            _animStartAngle = CurrentRotation;
            _animTargetAngle = CurrentRotation + delta;
            _animStart = DateTimeOffset.UtcNow;
            _animDuration = TimeSpan.FromSeconds(Math.Max(1.0, (double)SpinDurationSeconds));

            // Use a TaskCompletionSource so we can await the timer-based animation.
            _spinTcs = new TaskCompletionSource();

            _animTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)   // ~60 fps
            };
            _animTimer.Tick += OnAnimationTick;
            _animTimer.Start();

            await _spinTcs.Task;     // suspend until animation completes

            // ── Post-spin ──────────────────────────────────────────────────
            var winner = Slices[_pendingWinnerIndex];
            WinnerIndex = _pendingWinnerIndex;
            WinnerMessage = $"🎉  {winner.Label}!";

            if (!string.IsNullOrEmpty(winner.SoundPath))
                _audioService.PlaySound(winner.SoundPath);
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
            var elapsed = DateTimeOffset.UtcNow - _animStart;
            var t = Math.Min(elapsed.TotalSeconds / _animDuration.TotalSeconds, 1.0);

            // Cubic ease-out: decelerate to a smooth stop
            var eased = 1.0 - Math.Pow(1.0 - t, 3.0);
            CurrentRotation = _animStartAngle + (_animTargetAngle - _animStartAngle) * eased;

            if (t >= 1.0)
            {
                _animTimer!.Stop();
                _animTimer.Tick -= OnAnimationTick;
                _animTimer = null;
                CurrentRotation = _animTargetAngle;
                _spinTcs?.TrySetResult();
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

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
