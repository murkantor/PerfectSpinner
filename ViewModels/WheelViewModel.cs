using System;
using System.Collections.Generic;
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
    /// All state and logic for a single spinner wheel — slices, spin animation,
    /// label styling, weights, image mode, chroma key, save/load.
    ///
    /// Two instances are hosted by <see cref="MainWindowViewModel"/>, one per tab.
    /// </summary>
    public partial class WheelViewModel : ViewModelBase
    {
        // ── Identity ─────────────────────────────────────────────────────────

        /// <summary>Display name shown on the tab header. Editable via double-click.</summary>
        [ObservableProperty] private string _name = string.Empty;
        [ObservableProperty] private bool _isEditingName;
        private string _nameBeforeEdit = string.Empty;

        // ── Services ──────────────────────────────────────────────────────────

        private readonly IFilePickerService _picker;
        private readonly LayoutService _layoutService;
        private readonly AudioService _audioService;
        private readonly LogService _logService;

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

        [ObservableProperty] private decimal _spinDurationSeconds = 4m;
        [ObservableProperty] private int _friction = 5;

        [ObservableProperty] private string _chromaKeyColor = "#00FF00";
        [ObservableProperty] private double _globalWeight = 3.0;
        [ObservableProperty] private bool _useWeightedSlices = false;

        [ObservableProperty] private int _sliceImageMode = 0;

        // ── Label styling ─────────────────────────────────────────────────────

        public static readonly IReadOnlyList<string> AvailableFontNames =
            ["Default", "Arial", "Courier New", "Georgia", "Impact", "Tahoma",
             "Times New Roman", "Trebuchet MS", "Verdana"];

        private static readonly string[] _fontFamilyValues =
            ["", "Arial", "Courier New", "Georgia", "Impact", "Tahoma",
             "Times New Roman", "Trebuchet MS", "Verdana"];

        [ObservableProperty] private bool _showLabels = true;
        [ObservableProperty] private bool _showPointerLabel = false;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LabelFontFamily))]
        private int _labelFontIndex = 0;

        public string LabelFontFamily =>
            _fontFamilyValues[Math.Clamp(LabelFontIndex, 0, _fontFamilyValues.Length - 1)];

        [ObservableProperty] private double _labelFontSize = 0;
        [ObservableProperty] private int _labelColorStyle = 0;
        [ObservableProperty] private bool _labelBold = false;

        // ── Logging ───────────────────────────────────────────────────────────

        [ObservableProperty] private bool _logSpins = true;

        // ── Winner display ────────────────────────────────────────────────────

        [ObservableProperty] private string _winnerMessageTemplate = "🎉  %t%!";
        [ObservableProperty] private string? _defaultSoundPath;
        [ObservableProperty] private bool _brightenWinner = false;
        [ObservableProperty] private bool _darkenLosers = false;
        [ObservableProperty] private bool _invertLoserText = false;

        // ── Border ────────────────────────────────────────────────────────────

        /// <summary>0 = white borders, 1 = black borders (outer ring + slice dividers).</summary>
        [ObservableProperty] private int _borderColorStyle = 0;

        /// <summary>0 = off, 1 = reveal winner only, 2 = reveal all on win.</summary>
        [ObservableProperty] private int _blackoutWheelMode = 0;

        // ── Confetti ──────────────────────────────────────────────────────────

        [ObservableProperty] private bool _showConfetti = false;
        [ObservableProperty] private string? _confettiImagePath;
        [ObservableProperty] private int _confettiCount = 120;
        /// <summary>0 = Mixed, 1 = Strips, 2 = Circles, 3 = Triangles, 4 = Stars.</summary>
        [ObservableProperty] private int _confettiShapeMode = 0;
        /// <summary>0 = Rainbow, 1 = Custom colour.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsCustomConfettiColor))]
        private int _confettiColorMode = 0;
        [ObservableProperty] private string _confettiCustomColor = "#FFD700";

        public bool IsCustomConfettiColor => ConfettiColorMode == 1;

        // ── Save/Load feedback ───────────────────────────────────────────────

        [ObservableProperty] private string? _saveError;

        // ── Sounds ───────────────────────────────────────────────────────────

        [ObservableProperty] private string? _spinStartSoundPath;
        [ObservableProperty] private string? _tickSound1Path;
        [ObservableProperty] private string? _tickSound2Path;

        // ── Derived ───────────────────────────────────────────────────────────

        public bool HasSelectedSlice => SelectedSlice != null;

        // ── Weight snapshot ───────────────────────────────────────────────────

        private double[]? _weightSnapshot;

        // ── Physics animation state ───────────────────────────────────────────

        private DispatcherTimer?  _animTimer;
        private DateTimeOffset    _animStart;
        private DateTimeOffset    _lastTickTime;
        private TaskCompletionSource? _spinTcs;
        private bool              _spinCancelled;

        private double _peakVelocity;
        private double _cruiseDuration;
        private double _windUpDuration;
        private double _accelEndTime;
        private double _fullSpeedEndTime;
        private double _halfSpeedEndTime;
        private double _windUpSpeed;
        private double _currentVelocity;
        private bool   _inFreeSpin;

        // Tick sound crossing detection
        private int  _lastTickSliceIndex = -1;
        private bool _tickSoundToggle    = false;

        // ── Default palette ───────────────────────────────────────────────────

        private static readonly string[] PaletteColors =
        [
            "#E74C3C", "#3498DB", "#2ECC71", "#F39C12",
            "#9B59B6", "#1ABC9C", "#E67E22", "#34495E",
            "#E91E63", "#00BCD4", "#8BC34A", "#FF5722"
        ];

        // ── Constructors ──────────────────────────────────────────────────────

        /// <summary>Runtime constructor.</summary>
        public WheelViewModel(
            IFilePickerService picker,
            LayoutService layoutService,
            AudioService audioService,
            LogService logService,
            string name)
        {
            _picker        = picker;
            _layoutService = layoutService;
            _audioService  = audioService;
            _logService    = logService;
            Name           = name;
            AddDefaultSlices();
        }

        /// <summary>Design-time constructor — services are null and must not be invoked.</summary>
        public WheelViewModel()
        {
            _picker        = null!;
            _layoutService = null!;
            _audioService  = null!;
            _logService    = null!;
            Name           = "Design Wheel";
            AddDefaultSlices();
        }

        // ── CanExecute predicates ─────────────────────────────────────────────

        public bool IsSpinning => _animTimer != null;

        private bool CanSpinWheel()           => Slices.Count >= 1 && _animTimer == null;
        private bool HasSelection()           => SelectedSlice != null;
        private bool CanMoveUp()              => SelectedSlice != null && Slices.IndexOf(SelectedSlice) > 0;
        private bool CanMoveDown()            => SelectedSlice != null && Slices.IndexOf(SelectedSlice) < Slices.Count - 1;
        private bool HasSelectionImagePath()  => SelectedSlice?.ImagePath != null;
        private bool HasSelectionSoundPath()  => SelectedSlice?.SoundPath != null;
        private bool CanUndoWeight()          => _weightSnapshot != null;

        // ── Commands ──────────────────────────────────────────────────────────

        [RelayCommand(CanExecute = nameof(CanSpinWheel))]
        private async Task SpinWheelAsync()
        {
            if (Slices.Count == 0 || _animTimer != null) return;

            if (UseWeightedSlices)
            {
                foreach (var s in Slices)
                    if (s.Weight <= 0) s.IsActive = false;
            }

            WinnerMessage = string.Empty;
            WinnerIndex   = -1;

            if (!string.IsNullOrEmpty(SpinStartSoundPath))
                _audioService.PlaySpinStartSound(SpinStartSoundPath);

            var rng           = new Random();
            var totalDuration = Math.Max(1.0, (double)SpinDurationSeconds);

            // Cruise duration is random 1.0–5.0 s (one decimal place) so the wheel
            // never lands on a predictable quadrant from a standing start.
            _cruiseDuration   = Math.Round(1.0 + rng.NextDouble() * 4.0, 1);

            _peakVelocity     = totalDuration * 180.0;
            _windUpDuration   = (0.02 + rng.NextDouble() * 0.03) * totalDuration;
            _windUpSpeed      = 60.0;
            _accelEndTime     = 0.10 * totalDuration;
            _fullSpeedEndTime = _accelEndTime + _cruiseDuration;
            _halfSpeedEndTime = _fullSpeedEndTime + 0.20 * totalDuration;

            _inFreeSpin          = false;
            _currentVelocity     = 0.0;
            _spinCancelled       = false;
            _lastTickSliceIndex  = -1;
            _tickSoundToggle     = false;

            _animStart    = DateTimeOffset.UtcNow;
            _lastTickTime = _animStart;
            _spinTcs      = new TaskCompletionSource();

            _animTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _animTimer.Tick += OnAnimationTick;
            _animTimer.Start();

            await _spinTcs.Task;
            await FinalizeSpinAsync();
        }

        /// <summary>
        /// Starts the wheel spinning from a drag release. Skips the wind-up /
        /// acceleration / cruise phases and goes straight to free-spin using
        /// <paramref name="velocityDegreesPerSecond"/> as the initial speed.
        /// Friction is applied the same way as a normal spin.
        /// </summary>
        public async Task StartInertialSpinAsync(double velocityDegreesPerSecond)
        {
            if (_animTimer != null || Math.Abs(velocityDegreesPerSecond) < 30) return;

            if (UseWeightedSlices)
            {
                foreach (var s in Slices)
                    if (s.Weight <= 0) s.IsActive = false;
            }

            WinnerMessage       = string.Empty;
            WinnerIndex         = -1;

            if (!string.IsNullOrEmpty(SpinStartSoundPath))
                _audioService.PlaySpinStartSound(SpinStartSoundPath);

            _cruiseDuration     = 0;
            _currentVelocity    = velocityDegreesPerSecond;
            _inFreeSpin         = true;
            _spinCancelled      = false;
            _lastTickSliceIndex = -1;
            _tickSoundToggle    = false;

            _animStart    = DateTimeOffset.UtcNow;
            _lastTickTime = _animStart;
            _spinTcs      = new TaskCompletionSource();

            _animTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _animTimer.Tick += OnAnimationTick;
            _animTimer.Start();
            SpinWheelCommand.NotifyCanExecuteChanged();

            await _spinTcs.Task;
            await FinalizeSpinAsync();
        }

        private async Task FinalizeSpinAsync()
        {
            if (_spinCancelled) return;

            var activeSlices = Slices
                .Where(s => s.IsActive && (!UseWeightedSlices || s.Weight > 0))
                .ToList();
            if (activeSlices.Count == 0) return;

            var pointerAngle = ((360.0 - CurrentRotation % 360.0) % 360.0 + 360.0) % 360.0;
            double totalWeight = UseWeightedSlices
                ? activeSlices.Sum(s => s.Weight)
                : activeSlices.Count;

            var winnerSlice = activeSlices[activeSlices.Count - 1];
            double cumDeg   = 0.0;
            for (int i = 0; i < activeSlices.Count; i++)
            {
                double w = UseWeightedSlices ? activeSlices[i].Weight : 1.0;
                cumDeg += (w / totalWeight) * 360.0;
                if (pointerAngle < cumDeg) { winnerSlice = activeSlices[i]; break; }
            }

            WinnerIndex = Slices.IndexOf(winnerSlice);

            // Per-slice winner label overrides the template; blank = use template.
            WinnerMessage = !string.IsNullOrWhiteSpace(winnerSlice.WinnerLabel)
                ? winnerSlice.WinnerLabel
                : WinnerMessageTemplate.Replace("%t%", winnerSlice.Label);

            if (LogSpins && _logService != null)
                await _logService.AppendSpinResultAsync(SpinDurationSeconds, Friction, _cruiseDuration, winnerSlice.Label);

            if (UseWeightedSlices)
            {
                winnerSlice.Weight = Math.Max(0.0, winnerSlice.Weight - 1.0);
                _weightSnapshot = null;
                UndoWeightCommand.NotifyCanExecuteChanged();
            }

            // Per-slice sound takes priority; fall back to the wheel default sound.
            var soundToPlay = !string.IsNullOrEmpty(winnerSlice.SoundPath)
                ? winnerSlice.SoundPath
                : DefaultSoundPath;
            if (!string.IsNullOrEmpty(soundToPlay))
                _audioService.PlaySound(soundToPlay);
        }

        [RelayCommand]
        private void ResetWheel()
        {
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
            SpinWheelCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        private void RandomiseStartAngle() =>
            CurrentRotation = new Random().NextDouble() * 360.0;

        [RelayCommand]
        private void RandomiseSliceOrder() =>
            ShuffleSlices(new Random());

        [RelayCommand]
        private void AddSlice()
        {
            var color = PaletteColors[Slices.Count % PaletteColors.Length];
            var slice = new WheelSliceViewModel
            {
                Label    = $"Slice {Slices.Count + 1}",
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
            var idx   = Slices.IndexOf(SelectedSlice);
            if (idx <= 0) return;
            var slice = SelectedSlice;
            Slices.Move(idx, idx - 1);
            SelectedSlice = slice;
            NotifyMoveCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(CanMoveDown))]
        private void MoveDown()
        {
            if (SelectedSlice == null) return;
            var idx   = Slices.IndexOf(SelectedSlice);
            if (idx < 0 || idx >= Slices.Count - 1) return;
            var slice = SelectedSlice;
            Slices.Move(idx, idx + 1);
            SelectedSlice = slice;
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
        private async Task BrowseDefaultSoundAsync()
        {
            var path = await _picker.OpenSoundFileAsync();
            if (path != null) DefaultSoundPath = path;
        }

        [RelayCommand]
        private void RemoveDefaultSound() => DefaultSoundPath = null;

        [RelayCommand]
        private async Task BrowseSpinStartSoundAsync()
        {
            var path = await _picker.OpenSoundFileAsync();
            if (path != null) SpinStartSoundPath = path;
        }

        [RelayCommand]
        private void RemoveSpinStartSound() => SpinStartSoundPath = null;

        [RelayCommand]
        private async Task BrowseTickSound1Async()
        {
            var path = await _picker.OpenSoundFileAsync();
            if (path != null) TickSound1Path = path;
        }

        [RelayCommand]
        private void RemoveTickSound1() => TickSound1Path = null;

        [RelayCommand]
        private async Task BrowseTickSound2Async()
        {
            var path = await _picker.OpenSoundFileAsync();
            if (path != null) TickSound2Path = path;
        }

        [RelayCommand]
        private void RemoveTickSound2() => TickSound2Path = null;

        [RelayCommand]
        private async Task BrowseConfettiImageAsync()
        {
            var path = await _picker.OpenConfettiFileAsync();
            if (path != null) ConfettiImagePath = path;
        }

        [RelayCommand]
        private void RemoveConfettiImage() => ConfettiImagePath = null;

        [RelayCommand]
        private void BeginRename()
        {
            _nameBeforeEdit = Name;
            IsEditingName = true;
        }

        [RelayCommand]
        private void CommitRename()
        {
            if (string.IsNullOrWhiteSpace(Name))
                Name = _nameBeforeEdit;
            IsEditingName = false;
        }

        [RelayCommand]
        private void CancelRename()
        {
            Name = _nameBeforeEdit;
            IsEditingName = false;
        }

        [RelayCommand]
        private async Task SaveLayoutAsync()
        {
            SaveError = null;

            var path = await _picker.SaveLayoutFileAsync(Name.ToLower().Replace(' ', '-'));
            if (path == null) return;

            var layout = ToLayout();

            try
            {
                if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    await _layoutService.SaveZipAsync(layout, path);
                else
                    await _layoutService.SaveAsync(layout, path);
            }
            catch (Exception ex)
            {
                SaveError = $"Save failed: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task LoadLayoutAsync()
        {
            var path = await _picker.OpenLayoutFileAsync();
            if (path == null) return;

            var layout = path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                ? await _layoutService.LoadZipAsync(path)
                : await _layoutService.LoadAsync(path);
            if (layout == null) return;

            ApplyLayout(layout);
        }

        /// <summary>Snapshots all current state into a serialisable <see cref="WheelLayout"/>.</summary>
        public WheelLayout ToLayout() => new WheelLayout
        {
            Name                  = Name,
            Slices                = Slices.Select(s => s.ToModel()).ToList(),
            SpinDurationSeconds   = (double)SpinDurationSeconds,
            Friction              = Friction,
            SliceImageMode        = SliceImageMode,
            ShowLabels            = ShowLabels,
            ShowPointerLabel      = ShowPointerLabel,
            LabelFontIndex        = LabelFontIndex,
            LabelFontSize         = LabelFontSize,
            LabelColorStyle       = LabelColorStyle,
            LabelBold             = LabelBold,
            ChromaKeyColor        = ChromaKeyColor,
            UseWeightedSlices     = UseWeightedSlices,
            GlobalWeight          = GlobalWeight,
            LogSpins              = LogSpins,
            WinnerMessageTemplate = WinnerMessageTemplate,
            DefaultSoundPath      = string.IsNullOrEmpty(DefaultSoundPath) ? null : DefaultSoundPath,
            BrightenWinner        = BrightenWinner,
            DarkenLosers          = DarkenLosers,
            InvertLoserText       = InvertLoserText,
            BorderColorStyle      = BorderColorStyle,
            BlackoutWheelMode     = BlackoutWheelMode,
            ShowConfetti          = ShowConfetti,
            ConfettiImagePath     = string.IsNullOrEmpty(ConfettiImagePath) ? null : ConfettiImagePath,
            ConfettiCount         = ConfettiCount,
            ConfettiShapeMode     = ConfettiShapeMode,
            ConfettiColorMode     = ConfettiColorMode,
            ConfettiCustomColor   = ConfettiCustomColor,
            SpinStartSoundPath    = string.IsNullOrEmpty(SpinStartSoundPath) ? null : SpinStartSoundPath,
            TickSound1Path        = string.IsNullOrEmpty(TickSound1Path) ? null : TickSound1Path,
            TickSound2Path        = string.IsNullOrEmpty(TickSound2Path) ? null : TickSound2Path,
        };

        /// <summary>
        /// Applies a <see cref="WheelLayout"/> to this wheel, replacing all current state.
        /// Used by load and clone operations.
        /// </summary>
        public void ApplyLayout(WheelLayout layout)
        {
            Slices.Clear();
            foreach (var model in layout.Slices)
                Slices.Add(new WheelSliceViewModel(model));

            SpinDurationSeconds   = (decimal)Math.Max(1.0, layout.SpinDurationSeconds);
            Friction              = Math.Clamp(layout.Friction, 1, 10);
            SliceImageMode        = Math.Clamp(layout.SliceImageMode, 0, 2);
            ShowLabels            = layout.ShowLabels;
            ShowPointerLabel      = layout.ShowPointerLabel;
            LabelFontIndex        = Math.Clamp(layout.LabelFontIndex, 0, _fontFamilyValues.Length - 1);
            LabelFontSize         = Math.Clamp(layout.LabelFontSize, 0, 72);
            LabelColorStyle       = Math.Clamp(layout.LabelColorStyle, 0, 1);
            LabelBold             = layout.LabelBold;
            ChromaKeyColor        = string.IsNullOrWhiteSpace(layout.ChromaKeyColor) ? "#00FF00" : layout.ChromaKeyColor;
            UseWeightedSlices     = layout.UseWeightedSlices;
            GlobalWeight          = Math.Clamp(layout.GlobalWeight, 1, 100);
            LogSpins              = layout.LogSpins;
            WinnerMessageTemplate = string.IsNullOrEmpty(layout.WinnerMessageTemplate)
                                    ? "🎉  %t%!" : layout.WinnerMessageTemplate;
            DefaultSoundPath      = layout.DefaultSoundPath;
            BrightenWinner        = layout.BrightenWinner;
            DarkenLosers          = layout.DarkenLosers;
            InvertLoserText       = layout.InvertLoserText;
            BorderColorStyle      = Math.Clamp(layout.BorderColorStyle, 0, 1);
            BlackoutWheelMode     = Math.Clamp(layout.BlackoutWheelMode, 0, 2);
            ShowConfetti          = layout.ShowConfetti;
            ConfettiImagePath     = layout.ConfettiImagePath;
            ConfettiCount         = Math.Clamp(layout.ConfettiCount == 0 ? 120 : layout.ConfettiCount, 1, 2000);
            ConfettiShapeMode     = Math.Clamp(layout.ConfettiShapeMode, 0, 4);
            ConfettiColorMode     = Math.Clamp(layout.ConfettiColorMode, 0, 1);
            ConfettiCustomColor   = string.IsNullOrWhiteSpace(layout.ConfettiCustomColor) ? "#FFD700" : layout.ConfettiCustomColor;
            SpinStartSoundPath    = layout.SpinStartSoundPath;
            TickSound1Path        = layout.TickSound1Path;
            TickSound2Path        = layout.TickSound2Path;
            if (!string.IsNullOrWhiteSpace(layout.Name))
                Name = layout.Name;
            CurrentRotation = 0;
            WinnerMessage   = string.Empty;
            WinnerIndex     = -1;
            SelectedSlice   = Slices.FirstOrDefault();
            SpinWheelCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        private void ApplyWeightToAll()
        {
            _weightSnapshot = Slices.Select(s => s.Weight).ToArray();
            UndoWeightCommand.NotifyCanExecuteChanged();
            foreach (var slice in Slices)
                slice.Weight = Math.Max(1.0, GlobalWeight);
        }

        [RelayCommand(CanExecute = nameof(CanUndoWeight))]
        private void UndoWeight()
        {
            if (_weightSnapshot == null) return;
            for (int i = 0; i < Math.Min(Slices.Count, _weightSnapshot.Length); i++)
                Slices[i].Weight = _weightSnapshot[i];
            _weightSnapshot = null;
            UndoWeightCommand.NotifyCanExecuteChanged();
        }

        // ── Animation tick ────────────────────────────────────────────────────

        private void OnAnimationTick(object? sender, EventArgs e)
        {
            var now     = DateTimeOffset.UtcNow;
            var elapsed = (now - _animStart).TotalSeconds;
            var dt      = Math.Min((now - _lastTickTime).TotalSeconds, 0.05);
            _lastTickTime = now;

            if (!_inFreeSpin)
            {
                if (elapsed < _windUpDuration)
                {
                    var t = elapsed / _windUpDuration;
                    _currentVelocity = -_windUpSpeed * (t * t);
                }
                else if (elapsed < _accelEndTime)
                {
                    var span = _accelEndTime - _windUpDuration;
                    var t    = (elapsed - _windUpDuration) / span;
                    _currentVelocity = Lerp(-_windUpSpeed, _peakVelocity, t * t);
                }
                else if (elapsed < _fullSpeedEndTime)
                {
                    _currentVelocity = _peakVelocity;
                }
                else if (elapsed < _halfSpeedEndTime)
                {
                    var t = (elapsed - _fullSpeedEndTime)
                          / (_halfSpeedEndTime - _fullSpeedEndTime);
                    _currentVelocity = _peakVelocity * (1.0 - 0.5 * t);
                }
                else
                {
                    _currentVelocity = _peakVelocity / 2.0;
                    _inFreeSpin      = true;
                }
            }
            else
            {
                var frictionRate = 0.20 + (Friction - 1) * 0.28;
                _currentVelocity *= (1.0 - frictionRate * dt);

                if (Math.Abs(_currentVelocity) < 0.5)
                {
                    FinishSpin();
                    return;
                }
            }

            CurrentRotation += _currentVelocity * dt;

            // ── Tick sound on each slice border crossing ───────────────────────
            var tickActive = Slices.Where(s => s.IsActive).ToList();
            if (tickActive.Count > 1 &&
                (!string.IsNullOrEmpty(TickSound1Path) || !string.IsNullOrEmpty(TickSound2Path)))
            {
                double tickTotalW = UseWeightedSlices
                    ? tickActive.Sum(s => Math.Max(1.0, s.Weight))
                    : tickActive.Count;

                double ptr = ((360.0 - CurrentRotation % 360.0) % 360.0 + 360.0) % 360.0;
                int currentSlice = tickActive.Count - 1;
                double cumDeg2 = 0;
                for (int i = 0; i < tickActive.Count; i++)
                {
                    double w = UseWeightedSlices ? Math.Max(1.0, tickActive[i].Weight) : 1.0;
                    cumDeg2 += (w / tickTotalW) * 360.0;
                    if (ptr < cumDeg2) { currentSlice = i; break; }
                }

                if (_lastTickSliceIndex >= 0 && currentSlice != _lastTickSliceIndex)
                {
                    // Channel A = sound 1 (toggle=false), Channel B = sound 2 (toggle=true).
                    // Fall back to the other sound if only one is configured.
                    var soundPath = _tickSoundToggle
                        ? (string.IsNullOrEmpty(TickSound2Path) ? TickSound1Path : TickSound2Path)
                        : (string.IsNullOrEmpty(TickSound1Path) ? TickSound2Path : TickSound1Path);
                    if (!string.IsNullOrEmpty(soundPath))
                        _audioService.PlayTickSound(soundPath, channelB: _tickSoundToggle);
                    _tickSoundToggle = !_tickSoundToggle;
                }
                _lastTickSliceIndex = currentSlice;
            }
        }

        private void FinishSpin()
        {
            _animTimer!.Stop();
            _animTimer.Tick -= OnAnimationTick;
            _animTimer       = null;
            _currentVelocity = 0.0;
            _spinTcs?.TrySetResult();
            SpinWheelCommand.NotifyCanExecuteChanged();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static double Lerp(double a, double b, double t) =>
            a + (b - a) * Math.Clamp(t, 0.0, 1.0);

        private void ShuffleSlices(Random rng)
        {
            var selected = SelectedSlice;
            var list = Slices.ToList();
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            Slices.Clear();
            foreach (var s in list) Slices.Add(s);
            SelectedSlice = selected;
        }

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
                    Label    = names[i],
                    ColorHex = PaletteColors[i % PaletteColors.Length]
                });
            }
            SelectedSlice = Slices[0];
        }
    }
}
