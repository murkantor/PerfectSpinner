using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PerfectSpinner.Services;

namespace PerfectSpinner.ViewModels
{
    /// <summary>
    /// Top-level ViewModel — owns an unbounded list of <see cref="WheelViewModel"/> instances
    /// and tracks which one is currently active (drives the OBS capture window).
    /// </summary>
    public partial class MainWindowViewModel : ViewModelBase
    {
        // ── Services (kept so AddWheelCommand can create new WheelViewModels) ──
        private readonly IFilePickerService _picker;
        private readonly LayoutService      _layoutService;
        private readonly AudioService       _audioService;
        private readonly LogService         _logService;

        /// <summary>All wheels; bound to the custom tab bar's ListBox.</summary>
        public ObservableCollection<WheelViewModel> Wheels { get; } = new();

        /// <summary>Master volume (0–100). Drives AudioService.Volume in real time.</summary>
        [ObservableProperty] private int _volume = 80;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ActiveWheel))]
        private int _activeWheelIndex = 0;

        /// <summary>
        /// The currently displayed wheel. Clamped so it never throws even if
        /// ActiveWheelIndex is momentarily -1 (ListBox deselection artefact).
        /// </summary>
        public WheelViewModel ActiveWheel =>
            Wheels[Math.Clamp(ActiveWheelIndex, 0, Wheels.Count - 1)];

        public MainWindowViewModel(
            IFilePickerService picker,
            LayoutService layoutService,
            AudioService audioService,
            LogService logService)
        {
            _picker        = picker;
            _layoutService = layoutService;
            _audioService  = audioService;
            _logService    = logService;

            // Apply initial volume so AudioService matches the default slider position.
            _audioService.Volume = _volume / 100f;

            // Subscribe before adding wheels so the first wheel is wired up automatically.
            Wheels.CollectionChanged += OnWheelsCollectionChanged;
            Wheels.Add(new WheelViewModel(picker, layoutService, audioService, logService, "Wheel 1"));
        }

        partial void OnVolumeChanged(int value) =>
            _audioService.Volume = Math.Clamp(value, 0, 100) / 100f;

        /// <summary>Guard against ListBox momentarily reporting SelectedIndex = -1.</summary>
        partial void OnActiveWheelIndexChanged(int value)
        {
            if (value < 0 && Wheels.Count > 0)
                ActiveWheelIndex = 0;
        }

        [RelayCommand]
        private void SpinAll()
        {
            foreach (var wheel in Wheels)
                if (wheel.SpinWheelCommand.CanExecute(null))
                    wheel.SpinWheelCommand.Execute(null);
        }

        [RelayCommand]
        private void AddWheel()
        {
            Wheels.Add(new WheelViewModel(
                _picker, _layoutService, _audioService, _logService,
                $"Wheel {Wheels.Count + 1}"));
            ActiveWheelIndex = Wheels.Count - 1;
        }

        /// <summary>
        /// Creates a deep copy of <paramref name="source"/> and appends it after the original.
        /// The clone is named by appending " (2)", " (3)" etc. to the base name.
        /// </summary>
        public void CloneWheel(WheelViewModel source)
        {
            var layout = source.ToLayout();
            var cloneName = GenerateCloneName(source.Name);
            var clone = new WheelViewModel(_picker, _layoutService, _audioService, _logService, cloneName);
            layout.Name = cloneName;
            clone.ApplyLayout(layout);

            var insertAt = Wheels.IndexOf(source) + 1;
            Wheels.Insert(insertAt, clone);
            ActiveWheelIndex = insertAt;
        }

        /// <summary>
        /// Removes <paramref name="wheel"/> from <see cref="Wheels"/>.
        /// If it was the only wheel, a blank "Wheel 1" is added so the app is never empty.
        /// </summary>
        public void DeleteWheel(WheelViewModel wheel)
        {
            var idx = Wheels.IndexOf(wheel);
            if (idx < 0) return;

            Wheels.Remove(wheel);

            if (Wheels.Count == 0)
                Wheels.Add(new WheelViewModel(_picker, _layoutService, _audioService, _logService, "Wheel 1"));

            ActiveWheelIndex = Math.Clamp(idx, 0, Wheels.Count - 1);
        }

        // ── private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Returns a unique name for a clone by stripping any existing " (N)" suffix
        /// and appending the next available number.
        /// </summary>
        private string GenerateCloneName(string sourceName)
        {
            var baseName = Regex.Replace(sourceName, @" \(\d+\)$", "");
            int n = 2;
            while (Wheels.Any(w => w.Name == $"{baseName} ({n})"))
                n++;
            return $"{baseName} ({n})";
        }

        // ── Chain trigger plumbing ────────────────────────────────────────────

        private void OnWheelsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (WheelViewModel w in e.OldItems)
                {
                    w.PropertyChanged -= OnWheelPropertyChangedForChain;
                    w.ChainTriggered  -= OnChainTriggered;
                }
            if (e.NewItems != null)
                foreach (WheelViewModel w in e.NewItems)
                {
                    w.PropertyChanged += OnWheelPropertyChangedForChain;
                    w.ChainTriggered  += OnChainTriggered;
                }
            UpdateOtherWheels();
        }

        private void OnWheelPropertyChangedForChain(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(WheelViewModel.Name))
                UpdateOtherWheels();
        }

        /// <summary>
        /// Rebuilds every wheel's <see cref="WheelViewModel.OtherWheels"/> list so the
        /// chain-trigger ComboBox always shows the current set of wheels.
        /// </summary>
        private void UpdateOtherWheels()
        {
            foreach (var wheel in Wheels)
            {
                wheel.OtherWheels.Clear();
                wheel.OtherWheels.Add(WheelChoiceItem.NoneChoice);
                foreach (var other in Wheels)
                {
                    if (other != wheel)
                        wheel.OtherWheels.Add(new WheelChoiceItem(other.WheelId, other.Name));
                }
            }
        }

        /// <summary>
        /// Called when a spin finishes and the winning slice has a chain trigger configured.
        /// Waits briefly so the winner banner is visible, then switches to the target wheel
        /// and spins it.
        /// </summary>
        private async void OnChainTriggered(string targetWheelId)
        {
            // Brief pause so the viewer can see the first wheel's winner before the second spins.
            await Task.Delay(1500);

            var target = Wheels.FirstOrDefault(w => w.WheelId == targetWheelId);
            if (target == null) return;

            ActiveWheelIndex = Wheels.IndexOf(target);

            if (target.SpinWheelCommand.CanExecute(null))
                target.SpinWheelCommand.Execute(null);
        }
    }
}
