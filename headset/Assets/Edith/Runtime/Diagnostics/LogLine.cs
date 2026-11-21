using System;
using System.Text;
using UnityEngine;

namespace Edith.Diagnostics
{
    public static class LogLine
    {
        /// One line "HH:mm:ss.fff LEVEL message". Errors, asserts and exceptions carry their stack trace on the lines
        /// below, indented, because a failure on the headset is read long after it happened.
        public static string Format(DateTime time, LogType type, string message, string stackTrace)
        {
            var line = new StringBuilder(time.ToString("HH:mm:ss.fff")).Append(' ').Append(Level(type)).Append(' ')
                .Append(message);
            if (IsFailure(type) && !string.IsNullOrWhiteSpace(stackTrace))
                foreach (var frame in stackTrace.TrimEnd().Split('\n'))
                    line.Append("\n    ").Append(frame.TrimEnd('\r'));
            return line.ToString();
        }

        public static bool IsFailure(LogType type) => type != LogType.Log && type != LogType.Warning;

        static string Level(LogType type) => type switch
        {
            LogType.Log => "INFO",
            LogType.Warning => "WARN",
            LogType.Error => "ERROR",
            LogType.Assert => "ASSERT",
            _ => "EXCEPTION",
        };
    }
}
