using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PerfectSpinner.Models;
using PerfectSpinner.Services;

namespace PerfectSpinner.ViewModels
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

        /// <summary>
        /// Stable per-session identity used by cross-wheel chain triggers.
        /// Generated fresh each run; not restored from layout (so clones never share an ID).
        /// </summary>
        public string WheelId { get; } = Guid.NewGuid().ToString();

        // ── Services ──────────────────────────────────────────────────────────

        private readonly IFilePickerService _picker;
        private readonly LayoutService _layoutService;
        private readonly AudioService _audioService;
        private readonly LogService _logService;

        // ── Observable state ─────────────────────────────────────────────────

        [ObservableProperty] private ObservableCollection<WheelSliceViewModel> _slices = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FilteredSlices))]
        private string _sliceSearchText = string.Empty;

        public IEnumerable<WheelSliceViewModel> FilteredSlices =>
            string.IsNullOrWhiteSpace(SliceSearchText)
                ? Slices
                : Slices.Where(s => s.Label.Contains(SliceSearchText, StringComparison.OrdinalIgnoreCase));

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSelectedSlice))]
        [NotifyPropertyChangedFor(nameof(SelectedSliceTriggerChoice))]
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

        // ── Performance ───────────────────────────────────────────────────────

        /// <summary>When true, animation and confetti timers target ~30 fps instead of ~60 fps.</summary>
        [ObservableProperty] private bool _capTo30Fps = false;

        /// <summary>When true, the pointer sits at 3 o'clock and labels rotate with the wheel.</summary>
        [ObservableProperty] private bool _pointerOnRight = false;

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

        // ── Troll Mode ────────────────────────────────────────────────────────

        [ObservableProperty] private bool _trollMode = false;
        [ObservableProperty] private int  _trollChance = 30;

        /// <summary>
        /// Revealed by clicking "By Murk17" in the window header.
        /// Toggled on all wheels simultaneously by MainWindow code-behind.
        /// Not persisted — session-only UI state.
        /// </summary>
        [ObservableProperty] private bool _isTrollSettingsVisible = false;

        /// <summary>When true, the troll fires on every spin (overrides TrollChance).</summary>
        [ObservableProperty] private bool _trollGuaranteeEnabled = false;

        /// <summary>0 = random, 1–8 = force a specific effect. Bound to ComboBox SelectedIndex.</summary>
        [ObservableProperty] private int _trollForcedEffect = 0;

        // ── Save/Load feedback ───────────────────────────────────────────────

        [ObservableProperty] private string? _saveError;

        // ── Sounds ───────────────────────────────────────────────────────────

        [ObservableProperty] private string? _spinStartSoundPath;
        [ObservableProperty] private string? _tickSound1Path;
        [ObservableProperty] private string? _tickSound2Path;

        // ── Derived ───────────────────────────────────────────────────────────

        public bool HasSelectedSlice => SelectedSlice != null;

        // ── Chain trigger ─────────────────────────────────────────────────────

        /// <summary>
        /// Fired at the end of a spin when the winning slice has a TriggerWheelId set.
        /// Handled by <see cref="MainWindowViewModel"/> which switches tabs and spins
        /// the target wheel after a short delay.
        /// </summary>
        public event Action<string>? ChainTriggered;

        /// <summary>
        /// Target wheels available for the "If this slice wins, spin:" ComboBox.
        /// Populated and kept up-to-date by <see cref="MainWindowViewModel"/>.
        /// Always starts with <see cref="WheelChoiceItem.NoneChoice"/>.
        /// </summary>
        public ObservableCollection<WheelChoiceItem> OtherWheels { get; } = new();

        /// <summary>
        /// Gets or sets the chain-trigger choice for the currently selected slice.
        /// Bound to the "If this slice wins, also spin:" ComboBox.
        /// </summary>
        public WheelChoiceItem? SelectedSliceTriggerChoice
        {
            get
            {
                if (SelectedSlice == null) return null;
                var id = SelectedSlice.TriggerWheelId;
                if (string.IsNullOrEmpty(id)) return WheelChoiceItem.NoneChoice;
                return OtherWheels.FirstOrDefault(w => w.Id == id) ?? WheelChoiceItem.NoneChoice;
            }
            set
            {
                if (SelectedSlice == null) return;
                SelectedSlice.TriggerWheelId = (value == null || value.Id == "") ? null : value.Id;
                OnPropertyChanged(nameof(SelectedSliceTriggerChoice));
            }
        }

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

        // ── Active-slice cache ────────────────────────────────────────────────
        // Rebuilt lazily when IsActive or Weight changes; avoids .Where().ToList()
        // + .Sum() lambda allocations on every animation tick (60×/s).

        private List<WheelSliceViewModel>? _activeSlicesCache;
        private double                     _activeTotalWeightCache;

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
            Slices.CollectionChanged += OnSlicesCollectionChangedForCache;
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
            Slices.CollectionChanged += OnSlicesCollectionChangedForCache;
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
                Interval = TimeSpan.FromMilliseconds(CapTo30Fps ? 33 : 16)
            };
            _animTimer.Tick += OnAnimationTick;
            _animTimer.Start();

            await _spinTcs.Task;
            if (TrollMode) await TrollAsync();
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
                Interval = TimeSpan.FromMilliseconds(CapTo30Fps ? 33 : 16)
            };
            _animTimer.Tick += OnAnimationTick;
            _animTimer.Start();
            SpinWheelCommand.NotifyCanExecuteChanged();

            await _spinTcs.Task;
            if (TrollMode) await TrollAsync();
            await FinalizeSpinAsync();
        }

        private async Task FinalizeSpinAsync()
        {
            if (_spinCancelled) return;

            var activeSlices = Slices
                .Where(s => s.IsActive && (!UseWeightedSlices || s.Weight > 0))
                .ToList();
            if (activeSlices.Count == 0) return;

            double pointerOffset = PointerOnRight ? 90.0 : 0.0;
            var pointerAngle = ((360.0 - CurrentRotation % 360.0 + pointerOffset) % 360.0 + 360.0) % 360.0;
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

            // Chain trigger: automatically spin another wheel if this slice has one configured.
            if (!string.IsNullOrEmpty(winnerSlice.TriggerWheelId))
                ChainTriggered?.Invoke(winnerSlice.TriggerWheelId);
        }

        // ── Troll engine ──────────────────────────────────────────────────────

        /// <summary>
        /// Optionally animates the wheel to a "trolled" position after stopping and
        /// before the winner is read. FinalizeSpinAsync then picks whoever is under
        /// the pointer at the new position.
        /// </summary>
        private async Task TrollAsync(int depth = 0)
        {
            if (_spinCancelled) return;

            var rng = new Random();
            bool shouldTroll = TrollGuaranteeEnabled || rng.Next(100) < TrollChance;
            if (!shouldTroll) return;

            var active = GetActiveSlicesCache();
            if (active.Count < 2) return;

            // Pick effect (0-based internally; TrollForcedEffect 0=random, 1-9=specific)
            int picked = (TrollForcedEffect > 0) ? (TrollForcedEffect - 1) : rng.Next(9);

            // Find current winner index in active slices
            double pointerOffset = PointerOnRight ? 90.0 : 0.0;
            double ptr = ((360.0 - CurrentRotation % 360.0 + pointerOffset) % 360.0 + 360.0) % 360.0;
            double totalW = _activeTotalWeightCache;

            int winnerIdx = active.Count - 1;
            double cum = 0.0;
            for (int i = 0; i < active.Count; i++)
            {
                double w = UseWeightedSlices ? Math.Max(1.0, active[i].Weight) : 1.0;
                cum += (w / totalW) * 360.0;
                if (ptr < cum) { winnerIdx = i; break; }
            }

            int prevIdx   = (winnerIdx - 1 + active.Count) % active.Count;
            int nextIdx   = (winnerIdx + 1) % active.Count;
            int skipIdx   = (winnerIdx + 2) % active.Count;
            int randomIdx;
            do { randomIdx = rng.Next(active.Count); } while (randomIdx == winnerIdx);

            // Precompute absolute target rotations
            double toPrev   = TrollTargetRotation(prevIdx,   true);
            double toNext   = TrollTargetRotation(nextIdx,   false);
            double toSkip   = TrollTargetRotation(skipIdx,   false);
            double toRandom = TrollTargetRotation(randomIdx, true);

            switch (picked)
            {
                case 0:
                    // Little Tick — one last CW hop, pointer moves back one slice
                    await TrollAnimateToAsync(toPrev, 0.9, TrollEaseOut);
                    break;

                case 1:
                    // Second Thoughts — wheel reverses slightly then snaps, pointer creeps forward
                    await TrollAnimateToAsync(toNext, 1.1, TrollEaseBackOut);
                    break;

                case 2:
                    // Second Wind — suddenly accelerates forward 2+ full rotations, random result
                {
                    double target = toRandom + 2.0 * 360.0;
                    while (target < CurrentRotation + 360.0) target += 360.0;
                    await TrollAnimateToAsync(target, 2.2, TrollEaseOut);
                    if (depth < 2) await TrollAsync(depth + 1);
                    break;
                }

                case 3:
                    // Victory Lap — true 360° that lands on exactly the same result
                    await TrollAnimateToAsync(CurrentRotation + 360.0, 2.8, TrollEaseOut);
                    break;

                case 4:
                    // The Shakes — earthquake oscillation then random snap
                {
                    double baseR = CurrentRotation;
                    for (int i = 0; i < 9; i++)
                    {
                        double shake = 14.0 * (1.0 - i * 0.09);
                        double shakeTarget = baseR + (i % 2 == 0 ? shake : -shake);
                        await TrollAnimateToAsync(shakeTarget, 0.11, t => Math.Sin(t * Math.PI));
                    }
                    await TrollAnimateToAsync(toRandom, 0.45, TrollEaseOut);
                    break;
                }

                case 5:
                    // Skip Ahead — smooth CW skip two slices forward
                    await TrollAnimateToAsync(toSkip, 1.4, TrollEaseInOut);
                    break;

                case 6:
                    // Boomerang — reverses part-way, then springs forward past winner to prev slice
                {
                    double arc = (UseWeightedSlices
                        ? Math.Max(1.0, active[winnerIdx].Weight) / totalW
                        : 1.0 / active.Count) * 360.0;
                    double midPoint = CurrentRotation - Math.Max(arc, 15.0) * 0.65;
                    await TrollAnimateToAsync(midPoint, 0.42, TrollEaseOut);
                    await Task.Delay(160);
                    await TrollAnimateToAsync(toPrev, 1.05, TrollEaseIn);
                    break;
                }

                case 7:
                    // Spin Doctors — three extra fast full rotations, dramatic slow finish
                {
                    double target = toRandom + 3.0 * 360.0;
                    while (target < CurrentRotation + 2.5 * 360.0) target += 360.0;
                    await TrollAnimateToAsync(target, 3.8, t => 1.0 - Math.Pow(1.0 - t, 3));
                    break;
                }

                case 8:
                    // Big Slice — one slice inflates to half the wheel, holds, then reverts
                    await BigSliceTrollAsync(active, rng);
                    break;
            }
        }

        /// <summary>
        /// Inflates a random slice so it takes 50% of the visual arc, holds ~1.5 s,
        /// then does a Second Wind spin. Recurses up to depth 2, keeping each prior
        /// inflated slice alive — all big slices stay equal width (non-big total weight
        /// each) and all revert together when the root call finishes.
        /// </summary>
        private async Task BigSliceTrollAsync(
            List<WheelSliceViewModel> active,
            Random rng,
            int depth = 0,
            List<(WheelSliceViewModel Slice, double OrigWeight)>? bigSlices = null)
        {
            bool isRoot      = bigSlices == null;
            bool origWeighted = isRoot ? UseWeightedSlices : false;
            bigSlices ??= new List<(WheelSliceViewModel, double)>();

            try
            {
                if (active.Count == 0 || _spinCancelled) return;

                // Pick a slice not already in the big pool.
                var usedSet = new HashSet<WheelSliceViewModel>(bigSlices.Select(t => t.Slice));
                var pool    = active.Where(s => !usedSet.Contains(s)).ToList();
                if (pool.Count == 0) pool = active.ToList();

                var bigSlice = pool[rng.Next(pool.Count)];
                bigSlices.Add((bigSlice, bigSlice.Weight));

                // All big slices get equal weight = sum of the remaining non-big weights.
                // This keeps every big slice the same visual size as each other.
                UseWeightedSlices = true;
                double nonBigSum = active
                    .Where(s => bigSlices.All(b => b.Slice != s))
                    .Sum(s => Math.Max(1.0, s.Weight));
                if (nonBigSum <= 0) nonBigSum = 1;
                foreach (var (s, _) in bigSlices)
                    s.Weight = nonBigSum;

                await Task.Delay(1500);
                if (_spinCancelled) return;

                // Second Wind animation from current position.
                var fresh = GetActiveSlicesCache();
                if (fresh.Count >= 2)
                {
                    double pointerOffset = PointerOnRight ? 90.0 : 0.0;
                    double ptr = ((360.0 - CurrentRotation % 360.0 + pointerOffset) % 360.0 + 360.0) % 360.0;
                    double totalW = _activeTotalWeightCache;
                    int wIdx = fresh.Count - 1;
                    double cum = 0.0;
                    for (int i = 0; i < fresh.Count; i++)
                    {
                        double w = UseWeightedSlices ? Math.Max(1.0, fresh[i].Weight) : 1.0;
                        cum += (w / totalW) * 360.0;
                        if (ptr < cum) { wIdx = i; break; }
                    }
                    int ri;
                    do { ri = rng.Next(fresh.Count); } while (ri == wIdx);
                    double swTarget = TrollTargetRotation(ri, true) + 2.0 * 360.0;
                    while (swTarget < CurrentRotation + 360.0) swTarget += 360.0;
                    await TrollAnimateToAsync(swTarget, 2.2, TrollEaseOut);
                }

                // Stack: add another inflated slice (keeps all previous ones inflated).
                if (depth < 2 && !_spinCancelled)
                    await BigSliceTrollAsync(GetActiveSlicesCache(), rng, depth + 1, bigSlices);
            }
            finally
            {
                // Root call reverts all inflated slices after all recursion completes.
                if (isRoot)
                {
                    foreach (var (s, orig) in bigSlices)
                        s.Weight = orig;
                    UseWeightedSlices = origWeighted;
                    InvalidateActiveCache();
                }
            }
        }

        /// <summary>
        /// Returns the wheel rotation value that places the pointer at the centre of
        /// <paramref name="activeSliceIndex"/>. <paramref name="preferCW"/> controls which
        /// direction is taken when the shortest path is ambiguous.
        /// </summary>
        private double TrollTargetRotation(int activeSliceIndex, bool preferCW)
        {
            var active = GetActiveSlicesCache();
            if (active.Count == 0 || (uint)activeSliceIndex >= (uint)active.Count)
                return CurrentRotation;

            double totalW       = _activeTotalWeightCache;
            double pointerOffset = PointerOnRight ? 90.0 : 0.0;

            double cumDeg = 0.0;
            for (int i = 0; i < activeSliceIndex; i++)
            {
                double w = UseWeightedSlices ? Math.Max(1.0, active[i].Weight) : 1.0;
                cumDeg += (w / totalW) * 360.0;
            }
            double arc  = (UseWeightedSlices ? Math.Max(1.0, active[activeSliceIndex].Weight) : 1.0)
                          / totalW * 360.0;
            double targetPtr = cumDeg + arc / 2.0;

            // R%360 that places the pointer at targetPtr
            // pointerAngle = ((360 - R%360 + offset) % 360 + 360) % 360
            // => R%360 = ((360 + offset - targetPtr) % 360 + 360) % 360
            double targetMod  = ((360.0 + pointerOffset - targetPtr) % 360.0 + 360.0) % 360.0;
            double currentMod = ((CurrentRotation % 360.0) + 360.0) % 360.0;

            double delta = targetMod - currentMod;
            if (delta >  180.0) delta -= 360.0;
            if (delta < -180.0) delta += 360.0;

            if (preferCW  && delta < 0) delta += 360.0;
            if (!preferCW && delta > 0) delta -= 360.0;

            return CurrentRotation + delta;
        }

        /// <summary>
        /// Smoothly animates <see cref="CurrentRotation"/> to <paramref name="targetRotation"/>
        /// over <paramref name="durationSeconds"/> using the provided <paramref name="easing"/>.
        /// </summary>
        private async Task TrollAnimateToAsync(
            double targetRotation, double durationSeconds, Func<double, double> easing)
        {
            double startR = CurrentRotation;
            double delta  = targetRotation - startR;
            int    ms     = CapTo30Fps ? 33 : 16;
            int    steps  = Math.Max(1, (int)(durationSeconds * 1000 / ms));

            for (int i = 1; i <= steps; i++)
            {
                if (_spinCancelled) return;
                double t = (double)i / steps;
                CurrentRotation = startR + delta * easing(t);
                await Task.Delay(TimeSpan.FromMilliseconds(ms));
            }
            CurrentRotation = targetRotation;
        }

        // ── Easing functions used by troll effects ────────────────────────────

        private static double TrollEaseInOut(double t)  => t < 0.5 ? 2*t*t : 1 - 2*(1-t)*(1-t);
        private static double TrollEaseOut(double t)    => 1 - (1-t)*(1-t)*(1-t);
        private static double TrollEaseIn(double t)     => t * t * t;

        /// <summary>Slightly overshoots then settles — "second thoughts" wobble.</summary>
        private static double TrollEaseBackOut(double t)
        {
            const double c1 = 1.70158, c3 = c1 + 1;
            return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
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
            WheelId               = WheelId,
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
            CapTo30Fps            = CapTo30Fps,
            PointerOnRight        = PointerOnRight,
            WinnerMessageTemplate = WinnerMessageTemplate,
            DefaultSoundPath      = string.IsNullOrEmpty(DefaultSoundPath) ? null : DefaultSoundPath,
            BrightenWinner        = BrightenWinner,
            DarkenLosers          = DarkenLosers,
            InvertLoserText       = InvertLoserText,
            BorderColorStyle      = BorderColorStyle,
            BlackoutWheelMode     = BlackoutWheelMode,
            TrollMode             = TrollMode,
            TrollChance           = TrollChance,
            TrollGuaranteeEnabled = TrollGuaranteeEnabled,
            TrollForcedEffect     = TrollForcedEffect,
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
            CapTo30Fps            = layout.CapTo30Fps;
            PointerOnRight        = layout.PointerOnRight;
            WinnerMessageTemplate = string.IsNullOrEmpty(layout.WinnerMessageTemplate)
                                    ? "🎉  %t%!" : layout.WinnerMessageTemplate;
            DefaultSoundPath      = layout.DefaultSoundPath;
            BrightenWinner        = layout.BrightenWinner;
            DarkenLosers          = layout.DarkenLosers;
            InvertLoserText       = layout.InvertLoserText;
            BorderColorStyle      = Math.Clamp(layout.BorderColorStyle, 0, 1);
            BlackoutWheelMode     = Math.Clamp(layout.BlackoutWheelMode, 0, 2);
            TrollMode             = layout.TrollMode;
            TrollChance           = Math.Clamp(layout.TrollChance, 0, 100);
            TrollGuaranteeEnabled = layout.TrollGuaranteeEnabled;
            TrollForcedEffect     = Math.Clamp(layout.TrollForcedEffect, 0, 9);
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
            var tickActive = GetActiveSlicesCache();
            if (tickActive.Count > 1 &&
                (!string.IsNullOrEmpty(TickSound1Path) || !string.IsNullOrEmpty(TickSound2Path)))
            {
                double tickTotalW = _activeTotalWeightCache;

                double tickPointerOffset = PointerOnRight ? 90.0 : 0.0;
                double ptr = ((360.0 - CurrentRotation % 360.0 + tickPointerOffset) % 360.0 + 360.0) % 360.0;
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

        // ── Active-slice cache helpers ────────────────────────────────────────

        private List<WheelSliceViewModel> GetActiveSlicesCache()
        {
            if (_activeSlicesCache != null) return _activeSlicesCache;

            _activeSlicesCache = new List<WheelSliceViewModel>(Slices.Count);
            foreach (var s in Slices)
                if (s.IsActive) _activeSlicesCache.Add(s);

            _activeTotalWeightCache = UseWeightedSlices
                ? SumActiveWeights(_activeSlicesCache)
                : _activeSlicesCache.Count;

            return _activeSlicesCache;
        }

        private static double SumActiveWeights(List<WheelSliceViewModel> list)
        {
            double total = 0;
            foreach (var s in list) total += Math.Max(1.0, s.Weight);
            return total;
        }

        private void InvalidateActiveCache() => _activeSlicesCache = null;

        private void OnSlicesCollectionChangedForCache(
            object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (WheelSliceViewModel s in e.OldItems)
                    s.PropertyChanged -= OnSlicePropertyChangedForCache;
            if (e.NewItems != null)
                foreach (WheelSliceViewModel s in e.NewItems)
                    s.PropertyChanged += OnSlicePropertyChangedForCache;
            InvalidateActiveCache();
            OnPropertyChanged(nameof(FilteredSlices));
        }

        private void OnSlicePropertyChangedForCache(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(WheelSliceViewModel.IsActive) or nameof(WheelSliceViewModel.Weight))
                InvalidateActiveCache();
            if (e.PropertyName is nameof(WheelSliceViewModel.Label) && !string.IsNullOrWhiteSpace(SliceSearchText))
                OnPropertyChanged(nameof(FilteredSlices));
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
