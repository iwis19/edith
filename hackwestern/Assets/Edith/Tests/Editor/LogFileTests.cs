using System;
using System.IO;
using System.Threading;
using Edith.Diagnostics;
using NUnit.Framework;
using UnityEngine;

namespace Edith.Tests
{
    public class LogFileTests
    {
        string dir;
        string path;

        [SetUp]
        public void SetUp()
        {
            dir = Path.Combine(Path.GetTempPath(), "edith-log-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            path = Path.Combine(dir, "edith.log");
        }

        [TearDown]
        public void TearDown() => Directory.Delete(dir, true);

        static string ReadShared(string file)
        {
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
        }

        [Test]
        public void A_message_is_one_timestamped_line_with_its_level()
        {
            var line = LogLine.Format(new DateTime(2026, 11, 21, 9, 5, 7, 42), LogType.Warning, "no hands", "at Foo");
            Assert.AreEqual("09:05:07.042 WARN no hands", line);
        }

        [Test]
        public void Errors_and_exceptions_carry_an_indented_stack_trace_but_info_does_not()
        {
            var time = new DateTime(2026, 11, 21, 9, 5, 7);
            Assert.AreEqual("09:05:07.000 EXCEPTION boom\n    A.B()\n    C.D()",
                LogLine.Format(time, LogType.Exception, "boom", "A.B()\r\nC.D()\r\n"));
            Assert.AreEqual("09:05:07.000 INFO hi", LogLine.Format(time, LogType.Log, "hi", "A.B()"));
            Assert.AreEqual("09:05:07.000 ERROR bad", LogLine.Format(time, LogType.Error, "bad", ""));
        }

        [Test]
        public void Lines_are_appended_to_what_an_earlier_run_left()
        {
            using (var first = new LogFile(path, 1000)) first.Write("one");
            using (var second = new LogFile(path, 1000)) second.Write("two");
            Assert.AreEqual("one\ntwo\n", ReadShared(path));
        }

        [Test]
        public void Past_the_cap_the_file_moves_to_old_and_a_new_one_starts()
        {
            using (var log = new LogFile(path, 10))
            {
                log.Write("aaaa");
                log.Write("bbbb");
                log.Write("cccc");
                log.Flush();
                Assert.AreEqual("cccc\n", ReadShared(path));
                Assert.AreEqual("aaaa\nbbbb\n", ReadShared(log.OldPath));
                log.Write("dddd");
                log.Write("eeee");
            }
            Assert.AreEqual("eeee\n", ReadShared(path));
            Assert.AreEqual("cccc\ndddd\n", ReadShared(path + ".old"));
        }

        [Test]
        public void A_line_longer_than_the_cap_is_still_written()
        {
            using (var log = new LogFile(path, 3)) log.Write("a long line");
            Assert.AreEqual("a long line\n", ReadShared(path));
        }

        [Test]
        public void Concurrent_writers_never_split_a_line()
        {
            using (var log = new LogFile(path, 1 << 20))
            {
                var threads = new Thread[4];
                for (var t = 0; t < threads.Length; t++)
                {
                    var id = t;
                    threads[t] = new Thread(() =>
                    {
                        for (var i = 0; i < 200; i++) log.Write($"thread {id} line {i}");
                    });
                    threads[t].Start();
                }
                foreach (var thread in threads) thread.Join();
            }
            var lines = File.ReadAllLines(path);
            Assert.AreEqual(800, lines.Length);
            foreach (var line in lines) StringAssert.IsMatch(@"^thread \d line \d+$", line);
        }

        [Test]
        public void An_unwritable_path_stops_the_log_without_throwing()
        {
            using (var log = new LogFile(Path.Combine(dir, "missing", "edith.log"), 100))
            {
                Assert.IsFalse(log.Write("x"));
                Assert.IsFalse(log.Write("y"));
                Assert.DoesNotThrow(log.Flush);
            }
        }
    }
}
