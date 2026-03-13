using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using GoldenSpinner.Services;


namespace GoldenSpinner.ViewModels
{
    /// <summary>
    /// Top-level ViewModel — owns two <see cref="WheelViewModel"/> instances and
    /// tracks which one is currently active (drives the OBS capture window).
    ///
    /// Switching tabs in MainWindow changes <see cref="ActiveWheelIndex"/>, which
    /// updates <see cref="ActiveWheel"/> and therefore the SpinnerWindow instantly.
    /// </summary>
    public partial class MainWindowViewModel : ViewModelBase
    {
        public WheelViewModel Wheel1 { get; }
        public WheelViewModel Wheel2 { get; }

        /// <summary>Ordered list used as the TabControl's ItemsSource.</summary>
        public IReadOnlyList<WheelViewModel> Wheels { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ActiveWheel))]
        private int _activeWheelIndex = 0;

        public WheelViewModel ActiveWheel => ActiveWheelIndex == 0 ? Wheel1 : Wheel2;

        public MainWindowViewModel(
            IFilePickerService picker,
            LayoutService layoutService,
            AudioService audioService,
            LogService logService)
        {
            Wheel1 = new WheelViewModel(picker, layoutService, audioService, logService, "Wheel 1");
            Wheel2 = new WheelViewModel(picker, layoutService, audioService, logService, "Wheel 2");
            Wheels = [Wheel1, Wheel2];
        }
    }
}
