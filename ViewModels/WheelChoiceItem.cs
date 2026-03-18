namespace PerfectSpinner.ViewModels
{
    /// <summary>
    /// Represents one entry in the "If this slice wins, also spin:" ComboBox.
    /// Maintained by <see cref="MainWindowViewModel"/> and stored on each
    /// <see cref="WheelViewModel.OtherWheels"/>.
    /// </summary>
    public class WheelChoiceItem
    {
        /// <summary>Sentinel item that clears any existing chain trigger.</summary>
        public static readonly WheelChoiceItem NoneChoice = new("", "— None (no chain) —");

        /// <summary>The target wheel's <see cref="WheelViewModel.WheelId"/>. Empty = none.</summary>
        public string Id { get; }

        /// <summary>Display name shown in the ComboBox.</summary>
        public string Name { get; }

        public WheelChoiceItem(string id, string name)
        {
            Id   = id;
            Name = name;
        }
    }
}
