using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
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
            Closing               += (_, _) => _spinnerWindow.Close();
            _spinnerWindow.Closed += (_, _) => Close();

            // ── Mutual z-raise — clicking either window brings both to front ──
            // We use SetWindowPos(SWP_NOACTIVATE) instead of Activate() so that
            // focus never moves away from the clicked window mid-click.  This
            // was the root cause of controls requiring multiple clicks: the old
            // Activate() call stole focus before PointerReleased completed.
            Activated             += (_, _) => BringToFrontNoActivate(_spinnerWindow);
            _spinnerWindow.Activated += (_, _) => BringToFrontNoActivate(this);

            // ── Side-by-side centred layout on startup ────────────────────────
            Opened += (_, _) => PositionWindowsSideBySide();
        }

        // ── Win32 z-order helper ──────────────────────────────────────────────

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(
            nint hWnd, nint hWndInsertAfter,
            int x, int y, int cx, int cy, uint flags);

        private const uint SWP_NOMOVE     = 0x0002;
        private const uint SWP_NOSIZE     = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;  // bring to top without stealing focus

        private static void BringToFrontNoActivate(Window window)
        {
            var handle = window.TryGetPlatformHandle()?.Handle ?? nint.Zero;
            if (handle != nint.Zero)
                SetWindowPos(handle, nint.Zero, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        // ── Initial layout ────────────────────────────────────────────────────

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
            Position                = new PixelPoint(startX,                  centerY - settingsH / 2);
            _spinnerWindow.Position = new PixelPoint(startX + settingsW + gap, centerY - spinnerH  / 2);
        }
    }
}
