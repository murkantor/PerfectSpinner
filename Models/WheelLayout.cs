using System.Collections.Generic;

namespace GoldenSpinner.Models
{
    /// <summary>
    /// Container for a complete wheel configuration.
    /// This is the root object serialized to JSON when the user saves a layout.
    /// Asset paths (images, sounds) are stored as absolute paths on disk and
    /// must remain accessible when the layout is reloaded.
    /// </summary>
    public class WheelLayout
    {
        public string Name { get; set; } = "My Wheel";
        public List<WheelSlice> Slices { get; set; } = new();
        public double SpinDurationSeconds { get; set; } = 4.0;
    }
}
