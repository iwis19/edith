using System;
using System.IO;
using UnityEngine;

namespace Edith.Diagnostics
{
    /// Copies every Unity log message to edith.log, because the headset's system log rolls over within minutes and a
    /// failed Look or Ask was gone before anyone could read it. Pull edith.log, and edith.log.old for the run before,
    /// from the app's files folder under /sdcard/Android/data/.
    public sealed class LogRecorder : MonoBehaviour
    {
        public const string FileName = "edith.log";
        const long MaxBytes = 2 * 1024 * 1024;

        static LogFile file;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Start()
        {
            // With domain reload off in the editor, a second play would otherwise hook the handler twice.
            if (file != null)
            {
                Application.logMessageReceivedThreaded -= Record;
                Application.quitting -= Flush;
                file.Dispose();
            }
            file = new LogFile(Path.Combine(Application.persistentDataPath, FileName), MaxBytes);
            file.Write($"--- session start {DateTime.Now:yyyy-MM-dd HH:mm:ss} v{Application.version} {Application.platform}");
            Application.logMessageReceivedThreaded += Record;
            Application.quitting += Flush;

            var holder = new GameObject("EdithLogRecorder") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(holder);
            holder.AddComponent<LogRecorder>();
        }

        static void Record(string message, string stackTrace, LogType type)
        {
            file.Write(LogLine.Format(DateTime.Now, type, message, stackTrace));
            // Other lines wait for the pause or quit flush, but a crash gives neither, and a failure is the line that
            // has to survive one.
            if (LogLine.IsFailure(type)) file.Flush();
        }

        static void Flush() => file?.Flush();

        void OnApplicationPause(bool paused)
        {
            if (paused) Flush();
        }
    }
}
