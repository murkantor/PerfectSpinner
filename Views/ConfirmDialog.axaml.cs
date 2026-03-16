using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PerfectSpinner.Views
{
    /// <summary>
    /// Minimal yes/no confirmation dialog.
    /// Call <see cref="ShowAsync"/> and await the result.
    /// </summary>
    public partial class ConfirmDialog : Window
    {
        private bool _result;

        public ConfirmDialog() { InitializeComponent(); }

        public ConfirmDialog(string message)
        {
            InitializeComponent();
            MessageText.Text = message;
        }

        public async System.Threading.Tasks.Task<bool> ShowAsync(Window owner)
        {
            await ShowDialog(owner);
            return _result;
        }

        private void OnYesClick(object? sender, RoutedEventArgs e)
        {
            _result = true;
            Close();
        }

        private void OnNoClick(object? sender, RoutedEventArgs e)
        {
            _result = false;
            Close();
        }
    }
}
