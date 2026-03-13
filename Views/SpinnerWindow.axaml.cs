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
    /// Closing either window closes both — wired from MainWindow.axaml.cs.
    /// </summary>
    public partial class SpinnerWindow : Window
    {
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
            DataContext = vm;
        }
    }
}
