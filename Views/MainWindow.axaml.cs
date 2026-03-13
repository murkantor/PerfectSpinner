using System;
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

        // Timestamp guard for mutual activation.
        // A bool flag resets too early (Activate() is async; the other window's
        // Activated event fires after the flag is already cleared, so the ping-pong
        // continues indefinitely and starves all input).  A 200 ms cooldown breaks
        // the cycle: both Activated events in the same user click happen within a
        // few milliseconds of each other, well inside the window.
        private long _lastActivationMs;

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
            Closing               += (_, _) => _spinnerWindow.Close();
            _spinnerWindow.Closed += (_, _) => Close();

            // ── Mutual activation — clicking either window raises both ─────────
            Activated += (_, _) =>
            {
                var now = Environment.TickCount64;
                if (now - _lastActivationMs < 200) return;
                _lastActivationMs = now;
                _spinnerWindow.Activate();
            };

            _spinnerWindow.Activated += (_, _) =>
            {
                var now = Environment.TickCount64;
                if (now - _lastActivationMs < 200) return;
                _lastActivationMs = now;
                Activate();
            };

            // ── Side-by-side centred layout on startup ────────────────────────
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
