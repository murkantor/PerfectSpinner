using Avalonia.Controls;
using Avalonia.Interactivity;
using GoldenSpinner.Services;
using GoldenSpinner.ViewModels;

namespace GoldenSpinner.Views
{
    /// <summary>
    /// The Settings window — controls, slice editor, and layout I/O.
    /// This is the app's MainWindow (closing it exits the process).
    ///
    /// On startup it creates the <see cref="SpinnerWindow"/> and shows it
    /// alongside itself.  Both windows share the same ViewModel instance
    /// so every change is reflected in real time in the capture window.
    /// </summary>
    public partial class MainWindow : Window
    {
        private SpinnerWindow _spinnerWindow;

        public MainWindow()
        {
            InitializeComponent();

            // Build the shared ViewModel with all required services.
            // WindowFilePickerService needs 'this' to resolve the OS file picker.
            var pickerService = new WindowFilePickerService(this);
            var layoutService = new LayoutService();
            var audioService  = new AudioService();
            var vm = new MainWindowViewModel(pickerService, layoutService, audioService);

            DataContext = vm;

            // Create the capture window, sharing the same ViewModel,
            // then show it so it's visible when the app starts.
            _spinnerWindow = new SpinnerWindow(vm);
            _spinnerWindow.Show();

            // When this (Settings) window closes, force-close the Spinner window.
            // SpinnerWindow's own close button only hides it, so we need CloseForReal()
            // to bypass that guard on app exit.
            Closing += (_, _) => _spinnerWindow.CloseForReal();
        }

        // ── Event handlers (view-specific, not suitable for ViewModel) ────────

        /// <summary>
        /// Attached to the "Show Spinner Window" button's Click event.
        /// Re-shows a hidden SpinnerWindow or brings it to the front if already visible.
        /// </summary>
        private void OnShowSpinnerWindowClicked(object? sender, RoutedEventArgs e)
        {
            if (_spinnerWindow.IsVisible)
            {
                _spinnerWindow.Activate();
            }
            else
            {
                _spinnerWindow.Show();
            }
        }
    }
}
