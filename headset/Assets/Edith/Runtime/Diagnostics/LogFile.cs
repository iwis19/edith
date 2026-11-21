using System;
using System.IO;
using System.Text;

namespace Edith.Diagnostics
{
    /// An append-only text file that moves to "<path>.old" once a line would pass maxBytes, replacing the previous
    /// .old, so the log never holds more than about twice the cap.
    public sealed class LogFile : IDisposable
    {
        static readonly Encoding Utf8 = new UTF8Encoding(false);

        readonly string path;
        readonly long maxBytes;
        readonly object gate = new object();
        StreamWriter writer;
        bool failed;
        long size;

        public LogFile(string path, long maxBytes)
        {
            this.path = path;
            this.maxBytes = maxBytes;
        }

        public string OldPath => path + ".old";

        /// False once the file cannot be written, and every later call does nothing. It never throws, because a log
        /// that fails inside a log handler would have nowhere to report it.
        public bool Write(string line)
        {
            lock (gate)
            {
                if (failed) return false;
                try
                {
                    var bytes = Utf8.GetByteCount(line) + 1;
                    if (writer == null) Open();
                    else if (size > 0 && size + bytes > maxBytes) Rotate();
                    writer.Write(line);
                    writer.Write('\n');
                    size += bytes;
                    return true;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    failed = true;
                    writer = null;
                    return false;
                }
            }
        }

        public void Flush()
        {
            lock (gate)
            {
                try { writer?.Flush(); }
                catch (IOException) { }
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                try { writer?.Dispose(); }
                catch (IOException) { }
                writer = null;
                failed = true;
            }
        }

        void Open()
        {
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            size = stream.Length;
            writer = new StreamWriter(stream, Utf8);
        }

        void Rotate()
        {
            writer.Dispose();
            writer = null;
            if (File.Exists(OldPath)) File.Delete(OldPath);
            File.Move(path, OldPath);
            Open();
        }
    }
}
