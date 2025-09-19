using System;
using System.Diagnostics;

namespace Pkuyo.CanKit.Net.Core.Diagnostics
{
    /// <summary>
    /// Internal logging level (内部日志级别)。
    /// </summary>
    public enum CanKitLogLevel
    {
        Debug,
        Information,
        Warning,
        Error
    }

    /// <summary>
    /// A single log entry with timestamp/level/message/exception (单条日志：时间/级别/消息/异常)。
    /// </summary>
    /// <param name="Timestamp">UTC timestamp (UTC 时间戳)。</param>
    /// <param name="Level">Log level (日志级别)。</param>
    /// <param name="Message">Log message (日志消息)。</param>
    /// <param name="Exception">Optional exception (可选异常)。</param>
    public readonly record struct CanKitLogEntry(
        DateTime Timestamp,
        CanKitLogLevel Level,
        string Message,
        Exception Exception)
    {
        /// <summary>
        /// Format entry for console/debug output (格式化输出)。
        /// </summary>
        public override string ToString()
        {
            if (Exception == null)
            {
                return $"[{Timestamp:O}] [{Level}] {Message}";
            }

            return $"[{Timestamp:O}] [{Level}] {Message} :: {Exception}";
        }
    }

    /// <summary>
    /// Unified logging façade with pluggable handler (统一日志门面，支持可插拔处理器)。
    /// </summary>
    public static class CanKitLogger
    {
        private static Action<CanKitLogEntry> _logHandler = DefaultLogHandler;

        /// <summary>
        /// Configure log handler to redirect entries (配置日志处理器以重定向日志)。
        /// </summary>
        /// <param name="handler">Target handler (目标处理器)。</param>
        public static void Configure(Action<CanKitLogEntry> handler)
        {
            _logHandler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        /// <summary>
        /// Write a log entry (写入一条日志)。
        /// </summary>
        /// <param name="level">Log level (日志级别)。</param>
        /// <param name="message">Message (日志内容)。</param>
        /// <param name="exception">Optional exception (可选异常)。</param>
        public static void Log(CanKitLogLevel level, string message, Exception exception = null)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            var entry = new CanKitLogEntry(DateTime.UtcNow, level, message, exception);
            var handler = _logHandler;
            handler?.Invoke(entry);
        }

        /// <summary>
        /// Log an exception, with optional custom message (记录异常，可带自定义消息)。
        /// </summary>
        /// <param name="exception">Exception (异常)。</param>
        /// <param name="message">Optional message, defaults to exception.Message (可选消息)。</param>
        public static void LogException(Exception exception, string message = null)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));
            Log(CanKitLogLevel.Error, message ?? exception.Message, exception);
        }
        
        /// <summary>
        /// Log an error, with optional custom exception (记录错误)。
        /// </summary>
        /// <param name="message">Optional message, defaults to exception.Message (消息)。</param>
        /// <param name="exception">Optional Exception (可选异常)。</param>
        public static void LogError(string message, Exception exception = null)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            Log(CanKitLogLevel.Error, message , exception);
        }
        
        /// <summary>
        /// Log a warning (记录警告)。
        /// </summary>
        public static void LogWarning(string message, Exception exception = null)
        {
            Log(CanKitLogLevel.Warning, message, exception);
        }

        /// <summary>
        /// Log an informational message (记录信息)。
        /// </summary>
        public static void LogInformation(string message)
        {
            Log(CanKitLogLevel.Information, message);
        }

        /// <summary>
        /// Log debug message (记录调试信息)。
        /// </summary>
        public static void LogDebug(string message)
        {
            Log(CanKitLogLevel.Debug, message);
        }

        /// <summary>
        /// Default handler; writes to Debug in DEBUG builds (默认处理器，仅 DEBUG 输出到 Debug)。
        /// </summary>
        private static void DefaultLogHandler(CanKitLogEntry entry)
        {
#if DEBUG
            Debug.WriteLine(entry.ToString());
#endif
        }
    }
}

