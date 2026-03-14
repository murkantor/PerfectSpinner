using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using GoldenSpinner.ViewModels;

namespace GoldenSpinner.Views
{
    /// <summary>
    /// The wheel-only capture window intended for OBS "Window Capture".
    /// It shares the MainWindowViewModel with the Settings window so that
    /// spinning, slice edits, and chroma-key colour changes are reflected here
    /// instantly without any extra wiring.
    ///
    /// Closing either window closes both — wired from MainWindow.axaml.cs.
    ///
    /// Easter egg: click and drag on the wheel to spin it manually. Release
    /// velocity drives a free-spin that uses the current Friction setting.
    /// </summary>
    public partial class SpinnerWindow : Window
    {
        private MainWindowViewModel? _vm;

        // ── Drag state ────────────────────────────────────────────────────────
        private bool           _isDragging;
        private double         _lastAngleDeg;   // angle of last pointer position (degrees)
        private double         _dragVelocity;   // EMA of angular velocity (degrees/second)
        private DateTimeOffset _lastDragTime;

        // Parameterless constructor required by the Avalonia XAML loader and
        // IDE design-time tools.  At runtime the app always uses the overload
        // that accepts a ViewModel.
        public SpinnerWindow()
        {
            InitializeComponent();
        }

        /// <summary>Runtime constructor — shares the ViewModel with the Settings window.</summary>
        public SpinnerWindow(MainWindowViewModel vm) : this()
        {
            _vm = vm;
            DataContext = vm;
        }

        // ── Pointer events ────────────────────────────────────────────────────

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            // Reset any stale drag state (e.g. release happened outside the window).
            _isDragging = false;

            var wheel = _vm?.ActiveWheel;
            if (wheel == null || wheel.IsSpinning) return;

            // Clear any previous winner display when the user grabs the wheel.
            wheel.WinnerIndex   = -1;
            wheel.WinnerMessage = string.Empty;

            _lastAngleDeg = AngleDeg(e.GetPosition(this));
            _dragVelocity = 0;
            _lastDragTime = DateTimeOffset.UtcNow;
            _isDragging   = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (!_isDragging) return;

            var wheel = _vm?.ActiveWheel;
            if (wheel == null) return;

            var now = DateTimeOffset.UtcNow;
            var dt  = (now - _lastDragTime).TotalSeconds;
            if (dt < 0.001) return; // avoid division by near-zero

            var newAngle = AngleDeg(e.GetPosition(this));
            var delta    = NormalizeDelta(newAngle - _lastAngleDeg);

            // Smooth instantaneous velocity with an EMA (α = 0.65).
            var instantVel = delta / dt;
            _dragVelocity  = 0.65 * instantVel + 0.35 * _dragVelocity;

            wheel.CurrentRotation += delta;
            _lastAngleDeg = newAngle;
            _lastDragTime = now;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (!_isDragging) return;

            _isDragging = false;

            var wheel = _vm?.ActiveWheel;
            if (wheel == null) return;

            // Fire-and-forget; StartInertialSpinAsync guards against low velocity internally.
            _ = wheel.StartInertialSpinAsync(_dragVelocity);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Angle in degrees of <paramref name="pos"/> relative to the window centre.</summary>
        private double AngleDeg(Point pos) =>
            Math.Atan2(pos.Y - Bounds.Height / 2, pos.X - Bounds.Width / 2) * (180.0 / Math.PI);

        /// <summary>Normalises an angular delta to the range (−180, 180].</summary>
        private static double NormalizeDelta(double delta)
        {
            while (delta >  180) delta -= 360;
            while (delta < -180) delta += 360;
            return delta;
        }
    }
}
