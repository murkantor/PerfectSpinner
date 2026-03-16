using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
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

            Wheels.Add(new WheelViewModel(picker, layoutService, audioService, logService, "Wheel 1"));
        }

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
    }
}
