namespace PerfectSpinner.Models
{
    /// <summary>
    /// Application-level settings persisted to %AppData%\PerfectSpinner\appsettings.json.
    /// These are global (not per-wheel) and survive across sessions.
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// When true, all wheels are auto-saved on exit and auto-restored on launch.
        /// </summary>
        public bool SaveOnExit { get; set; } = false;

        /// <summary>
        /// Multiplier applied to the base UI font size (14 px).
        /// Range 0.7 – 1.5; default 1.0 (= 14 px).
        /// </summary>
        public double UiTextScale { get; set; } = 1.0;
    }
}
