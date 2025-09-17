using System;
using System.Diagnostics;

namespace Pkuyo.CanKit.Net.Core.Diagnostics
{
    public enum CanKitLogLevel
    {
        Debug,
        Information,
        Warning,
        Error
    }

    public readonly record struct CanKitLogEntry(
        DateTime Timestamp,
        CanKitLogLevel Level,
        string Message,
        Exception Exception)
    {
        public override string ToString()
        {
            if (Exception == null)
            {
                return $"[{Timestamp:O}] [{Level}] {Message}";
            }

            return $"[{Timestamp:O}] [{Level}] {Message} :: {Exception}";
        }
    }

    public static class CanKitLogger
    {
        private static Action<CanKitLogEntry> _logHandler = DefaultLogHandler;

        public static void Configure(Action<CanKitLogEntry> handler)
        {
            _logHandler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public static void Log(CanKitLogLevel level, string message, Exception exception = null)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            var entry = new CanKitLogEntry(DateTime.UtcNow, level, message, exception);
            var handler = _logHandler;
            handler?.Invoke(entry);
        }

        public static void LogException(Exception exception, string message = null)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));
            Log(CanKitLogLevel.Error, message ?? exception.Message, exception);
        }

        public static void LogWarning(string message, Exception exception = null)
        {
            Log(CanKitLogLevel.Warning, message, exception);
        }

        public static void LogInformation(string message)
        {
            Log(CanKitLogLevel.Information, message);
        }

        public static void LogDebug(string message)
        {
            Log(CanKitLogLevel.Debug, message);
        }

        private static void DefaultLogHandler(CanKitLogEntry entry)
        {
#if DEBUG
            Debug.WriteLine(entry.ToString());
#endif
        }
    }
}
