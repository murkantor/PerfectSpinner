using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GoldenSpinner.Services;

namespace GoldenSpinner.ViewModels
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
        private void AddWheel()
        {
            Wheels.Add(new WheelViewModel(
                _picker, _layoutService, _audioService, _logService,
                $"Wheel {Wheels.Count + 1}"));
            ActiveWheelIndex = Wheels.Count - 1;
        }
    }
}
