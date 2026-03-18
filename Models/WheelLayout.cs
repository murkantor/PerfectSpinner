using System.Collections.Generic;

namespace PerfectSpinner.Models
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

        /// <summary>Stable identity for this wheel. Used by cross-wheel chain triggers.</summary>
        public string WheelId { get; set; } = string.Empty;

        // Spin settings
        public double SpinDurationSeconds { get; set; } = 4.0;
        public int Friction { get; set; } = 5;

        // Appearance
        public int SliceImageMode { get; set; } = 0;
        public bool ShowLabels { get; set; } = true;
        public bool ShowPointerLabel { get; set; } = false;
        public int LabelFontIndex { get; set; } = 0;
        public double LabelFontSize { get; set; } = 0;
        public int LabelColorStyle { get; set; } = 0;
        public bool LabelBold { get; set; } = false;

        // Chroma key
        public string ChromaKeyColor { get; set; } = "#00FF00";

        // Weights
        public bool UseWeightedSlices { get; set; } = false;
        public double GlobalWeight { get; set; } = 3.0;

        // Logging
        public bool LogSpins { get; set; } = true;

        // Performance
        /// <summary>When true, animation and confetti timers run at ~30 fps instead of ~60 fps.</summary>
        public bool CapTo30Fps { get; set; } = false;

        /// <summary>When true, the pointer sits at 3 o'clock (right) and labels rotate with the wheel.</summary>
        public bool PointerOnRight { get; set; } = false;

        // Border
        public int BorderColorStyle { get; set; } = 0; // 0 = white, 1 = black

        // Blackout: 0 = off, 1 = reveal winner only, 2 = reveal all on win
        public int BlackoutWheelMode { get; set; } = 0;

        // Sounds
        public string? SpinStartSoundPath { get; set; }
        public string? TickSound1Path { get; set; }
        public string? TickSound2Path { get; set; }

        // Winner display
        public string WinnerMessageTemplate { get; set; } = "🎉  %t%!";
        public string? DefaultSoundPath { get; set; }
        public bool BrightenWinner { get; set; } = false;
        public bool DarkenLosers { get; set; } = false;
        public bool InvertLoserText { get; set; } = false;

        // Troll Mode
        public bool TrollMode             { get; set; } = false;
        public int  TrollChance           { get; set; } = 30;  // 0–100 %
        public bool TrollGuaranteeEnabled { get; set; } = false;
        /// <summary>0 = random, 1–8 = specific effect index.</summary>
        public int  TrollForcedEffect     { get; set; } = 0;

        // Confetti
        public bool ShowConfetti { get; set; } = false;
        public string? ConfettiImagePath { get; set; }
        public int ConfettiCount { get; set; } = 120;
        /// <summary>0 = Mixed, 1 = Strips, 2 = Circles, 3 = Triangles, 4 = Stars.</summary>
        public int ConfettiShapeMode { get; set; } = 0;
        /// <summary>0 = Rainbow, 1 = Custom colour.</summary>
        public int ConfettiColorMode { get; set; } = 0;
        public string ConfettiCustomColor { get; set; } = "#FFD700";
    }
}
