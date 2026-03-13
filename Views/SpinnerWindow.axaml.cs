using Avalonia.Controls;
using GoldenSpinner.ViewModels;

namespace GoldenSpinner.Views
{
    /// <summary>
    /// The wheel-only capture window intended for OBS "Window Capture".
    /// It shares the MainWindowViewModel with the Settings window so that
    /// spinning, slice edits, and chroma-key colour changes are reflected here
    /// instantly without any extra wiring.
    ///
    /// Closing behaviour:
    ///   Clicking the window's X button HIDES it rather than destroying it,
    ///   so the Settings window can show it again without recreating it.
    ///   The Settings window calls CloseForReal() on app exit to bypass this.
    /// </summary>
    public partial class SpinnerWindow : Window
    {
        private bool _forceClose;

        // Parameterless constructor required by the Avalonia XAML loader and
        // IDE design-time tools.  At runtime the app always uses the overload
        // that accepts a ViewModel.
        public SpinnerWindow()
        {
            InitializeComponent();

            // Hide instead of close when the user clicks the title-bar X.
            // This keeps the window alive so Settings can re-show it cheaply.
            Closing += (_, e) =>
            {
                if (!_forceClose)
                {
                    e.Cancel = true;
                    Hide();
                }
            };
        }

        /// <summary>Runtime constructor — shares the ViewModel with the Settings window.</summary>
        public SpinnerWindow(MainWindowViewModel vm) : this()
        {
            DataContext = vm;
        }

        /// <summary>
        /// Called by the Settings window when the app is exiting.
        /// Bypasses the hide-instead-of-close guard so the window actually closes.
        /// </summary>
        public void CloseForReal()
        {
            _forceClose = true;
            Close();
        }
    }
}
