using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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
            var logService    = new LogService();
            var vm = new MainWindowViewModel(pickerService, layoutService, audioService, logService);

            DataContext = vm;

            // Create the capture window sharing the same ViewModel and show it.
            _spinnerWindow = new SpinnerWindow(vm);
            _spinnerWindow.Show();

            // ── Bidirectional close ───────────────────────────────────────────
            Closing               += (_, _) => _spinnerWindow.Close();
            _spinnerWindow.Closed += (_, _) => Close();

            // ── Mutual z-raise — clicking either window brings both to front ──
            Activated                += (_, _) => BringToFrontNoActivate(_spinnerWindow);
            _spinnerWindow.Activated += (_, _) => BringToFrontNoActivate(this);

            // ── Side-by-side centred layout on startup ────────────────────────
            Opened += (_, _) => PositionWindowsSideBySide();

            // ── Tab rename: wire up existing wheels, then new ones ────────────
            foreach (var wheel in vm.Wheels)
                wheel.PropertyChanged += OnWheelPropertyChanged;

            vm.Wheels.CollectionChanged += (_, e) =>
            {
                if (e.NewItems == null) return;
                foreach (WheelViewModel wheel in e.NewItems)
                    wheel.PropertyChanged += OnWheelPropertyChanged;
            };

            // ── Auto-scroll tab bar when selection changes ────────────────────
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainWindowViewModel.ActiveWheelIndex))
                    ScrollSelectedTabIntoView();
            };

            // Detect double-click on tab header TextBlocks.
            this.AddHandler(PointerPressedEvent, OnTabHeaderPointerPressed, RoutingStrategies.Bubble);

            // Commit on LostFocus (user clicks away) or Enter; cancel on Escape.
            this.AddHandler(LostFocusEvent, OnRenameLostFocus, RoutingStrategies.Bubble);
            this.AddHandler(KeyDownEvent,   OnRenameKeyDown,   RoutingStrategies.Tunnel);
        }

        // ── Tab context-menu handlers ─────────────────────────────────────────

        private void OnTabRenameClick(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { DataContext: WheelViewModel wheel })
                wheel.BeginRenameCommand.Execute(null);
        }

        private void OnTabCloneClick(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { DataContext: WheelViewModel wheel } &&
                DataContext is MainWindowViewModel vm)
                vm.CloneWheel(wheel);
        }

        private async void OnTabDeleteClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem { DataContext: WheelViewModel wheel }) return;
            if (DataContext is not MainWindowViewModel vm) return;

            var confirmed = await new ConfirmDialog(
                $"Delete \"{wheel.Name}\"? This cannot be undone.")
                .ShowAsync(this);

            if (confirmed)
                vm.DeleteWheel(wheel);
        }

        // ── Scroll button handlers (navigate tabs) ────────────────────────────

        private void OnScrollLeft(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm && vm.ActiveWheelIndex > 0)
                vm.ActiveWheelIndex--;
        }

        private void OnScrollRight(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm && vm.ActiveWheelIndex < vm.Wheels.Count - 1)
                vm.ActiveWheelIndex++;
        }

        private void ScrollSelectedTabIntoView()
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (DataContext is not MainWindowViewModel vm) return;
                var tabList = this.FindControl<ListBox>("TabList");
                tabList?.ScrollIntoView(vm.ActiveWheel);
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }

        // ── Tab rename handlers ───────────────────────────────────────────────

        private void OnTabHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.ClickCount < 2) return;
            if (e.Source is TextBlock { DataContext: WheelViewModel wheel } && !wheel.IsEditingName)
                wheel.BeginRenameCommand.Execute(null);
        }

        private void OnWheelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(WheelViewModel.IsEditingName)) return;
            if (sender is not WheelViewModel { IsEditingName: true }) return;

            // After the visual tree updates (TextBox becomes visible), focus it.
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var tabList = this.FindControl<ListBox>("TabList");
                var textBox = tabList?
                    .GetVisualDescendants()
                    .OfType<TextBox>()
                    .FirstOrDefault(t => t.IsVisible);
                textBox?.Focus();
                textBox?.SelectAll();
            });
        }

        private void OnRenameLostFocus(object? sender, RoutedEventArgs e)
        {
            if (e.Source is not TextBox) return;
            if (DataContext is not MainWindowViewModel vm) return;
            foreach (var wheel in vm.Wheels)
                if (wheel.IsEditingName)
                    wheel.CommitRenameCommand.Execute(null);
        }

        private void OnRenameKeyDown(object? sender, KeyEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm) return;
            var editing = vm.Wheels.FirstOrDefault(w => w.IsEditingName);
            if (editing == null) return;

            switch (e.Key)
            {
                case Key.Enter:
                    editing.CommitRenameCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    editing.CancelRenameCommand.Execute(null);
                    e.Handled = true;
                    break;
            }
        }

        // ── Win32 z-order helper ──────────────────────────────────────────────

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(
            nint hWnd, nint hWndInsertAfter,
            int x, int y, int cx, int cy, uint flags);

        private const uint SWP_NOMOVE     = 0x0002;
        private const uint SWP_NOSIZE     = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        private static void BringToFrontNoActivate(Window window)
        {
            var handle = window.TryGetPlatformHandle()?.Handle ?? nint.Zero;
            if (handle != nint.Zero)
                SetWindowPos(handle, nint.Zero, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        // ── Initial layout ────────────────────────────────────────────────────

        private void PositionWindowsSideBySide()
        {
            var screen = Screens.Primary;
            if (screen == null) return;

            var scale    = screen.Scaling;
            var workArea = screen.WorkingArea;

            var settingsW = (int)(Width  * scale);
            var settingsH = (int)(Height * scale);
            var spinnerW  = (int)(_spinnerWindow.Width  * scale);
            var spinnerH  = (int)(_spinnerWindow.Height * scale);

            const int gap = 16;

            var startX  = workArea.X + (workArea.Width - settingsW - spinnerW - gap) / 2;
            var centerY = workArea.Y +  workArea.Height / 2;

            Position                = new PixelPoint(startX,                   centerY - settingsH / 2);
            _spinnerWindow.Position = new PixelPoint(startX + settingsW + gap, centerY - spinnerH  / 2);
        }
    }
}
