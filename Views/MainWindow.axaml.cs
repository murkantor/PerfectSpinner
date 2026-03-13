using Avalonia;
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
    /// On startup it creates the SpinnerWindow, shows it, then positions
    /// both windows side by side in the centre of the primary display.
    ///
    /// Closing either window closes both.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly SpinnerWindow _spinnerWindow;

        public MainWindow()
        {
            InitializeComponent();

            // Build the shared ViewModel with all required services.
            var pickerService = new WindowFilePickerService(this);
            var layoutService = new LayoutService();
            var audioService  = new AudioService();
            var vm = new MainWindowViewModel(pickerService, layoutService, audioService);

            DataContext = vm;

            // Create the capture window sharing the same ViewModel and show it.
            _spinnerWindow = new SpinnerWindow(vm);
            _spinnerWindow.Show();

            // ── Bidirectional close ───────────────────────────────────────────
            // Closing either window closes the other.  Using Closed (past-tense)
            // on the spinner avoids re-entrancy: by the time it fires the window
            // is already gone, so the resulting Close() here is the only action.
            Closing              += (_, _) => _spinnerWindow.Close();
            _spinnerWindow.Closed += (_, _) => Close();

            // ── Side-by-side centred layout on startup ────────────────────────
            // Screens are only queryable after the window is shown, so we defer
            // to the Opened event.  SpinnerWindow is already visible by then.
            Opened += (_, _) => PositionWindowsSideBySide();
        }

        /// <summary>
        /// Places the Settings window on the left and the SpinnerWindow on the
        /// right, with the pair centred on the primary display's work area.
        /// </summary>
        private void PositionWindowsSideBySide()
        {
            var screen = Screens.Primary;
            if (screen == null) return;

            var scale    = screen.Scaling;
            var workArea = screen.WorkingArea;   // physical pixels

            // Convert logical window sizes to physical pixels.
            var settingsW = (int)(Width  * scale);
            var settingsH = (int)(Height * scale);
            var spinnerW  = (int)(_spinnerWindow.Width  * scale);
            var spinnerH  = (int)(_spinnerWindow.Height * scale);

            const int gap = 16; // physical pixels between the two windows

            var startX  = workArea.X + (workArea.Width - settingsW - spinnerW - gap) / 2;
            var centerY = workArea.Y +  workArea.Height / 2;

            // Settings on the left, Spinner on the right, both vertically centred.
            Position               = new PixelPoint(startX,                  centerY - settingsH / 2);
            _spinnerWindow.Position = new PixelPoint(startX + settingsW + gap, centerY - spinnerH  / 2);
        }

        // ── Event handlers (view-specific, not suitable for ViewModel) ────────

        /// <summary>
        /// Attached to the "Show Spinner Window" button's Click event.
        /// Brings the SpinnerWindow to the front, or activates it if already visible.
        /// </summary>
        private void OnShowSpinnerWindowClicked(object? sender, RoutedEventArgs e)
        {
            if (_spinnerWindow.IsVisible)
                _spinnerWindow.Activate();
            else
                _spinnerWindow.Show();
        }
    }
}
