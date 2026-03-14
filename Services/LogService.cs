using System;
using System.IO;
using System.Threading.Tasks;

namespace GoldenSpinner.Services
{
    /// <summary>
    /// Appends spin results to ./logs/spins.log (relative to the executable).
    /// Format: YYYY-MM-DD | HH:MM:SS | Speed | Friction | Result
    /// Each app session is preceded by a separator line the first time a result is logged.
    /// </summary>
    public class LogService
    {
        private readonly string _logFile;
        private bool _sessionStarted;

        public LogService()
        {
            var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
            _logDir = logDir;
            _logFile = Path.Combine(logDir, "spins.log");
        }

        private readonly string _logDir;

        public async Task AppendSpinResultAsync(decimal speed, int friction, double cruiseDuration, string result)
        {
            Directory.CreateDirectory(_logDir);

            if (!_sessionStarted)
            {
                _sessionStarted = true;
                var separator = Environment.NewLine +
                                "============================================================" +
                                Environment.NewLine;
                await File.AppendAllTextAsync(_logFile, separator);
            }

            var now = DateTime.Now;
            var line = $"{now:yyyy-MM-dd} | {now:HH:mm:ss} | {speed,4:F1} | {friction,2} | {cruiseDuration:F1} | {result}";
            await File.AppendAllTextAsync(_logFile, line + Environment.NewLine);
        }
    }
}
