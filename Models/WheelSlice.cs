using System;

namespace PerfectSpinner.Models
{
    /// <summary>
    /// Data model for a single slice on the spinner wheel.
    /// Serialized to/from JSON for layout save/load.
    /// </summary>
    public class WheelSlice
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>Display label shown inside the slice.</summary>
        public string Label { get; set; } = "Slice";

        /// <summary>Absolute path to the slice image (PNG or JPG). Null if none.</summary>
        public string? ImagePath { get; set; }

        /// <summary>Absolute path to the sound file (WAV or MP3). Null if none.</summary>
        public string? SoundPath { get; set; }

        /// <summary>
        /// Overrides the winner message template when this slice wins.
        /// Null/empty = use the wheel's WinnerMessageTemplate.
        /// </summary>
        public string? WinnerLabel { get; set; }

        /// <summary>Background color as a CSS hex string, e.g. "#E74C3C".</summary>
        public string ColorHex { get; set; } = "#E74C3C";

        /// <summary>Relative weight for weighted spinning. 0 = auto-hidden from wheel.</summary>
        public double Weight { get; set; } = 1.0;

        /// <summary>User-toggled. True = participates in the wheel. False = excluded.</summary>
        public bool IsActive { get; set; } = true;
    }
}
