using System;
using System.Globalization;
using Edith.Look;
using NUnit.Framework;
using UnityEngine;

namespace Edith.Tests
{
    public class LookMetaTests
    {
        [Serializable]
        sealed class Meta
        {
            public int look_id;
            public string source;
            public string captured_at;
        }

        [Test]
        public void The_meta_names_the_look_and_the_pinch()
        {
            var meta = JsonUtility.FromJson<Meta>(LookMeta.Json(7, LookSource.Pinch, new DateTime(2026, 11, 21, 15, 4, 5, 123, DateTimeKind.Utc)));
            Assert.AreEqual(7, meta.look_id);
            Assert.AreEqual("pinch", meta.source);
        }

        [Test]
        public void A_look_opened_from_a_scan_pin_says_so()
        {
            var meta = JsonUtility.FromJson<Meta>(LookMeta.Json(3, LookSource.Scan, new DateTime(2026, 11, 21, 15, 4, 5, DateTimeKind.Utc)));
            Assert.AreEqual("scan", meta.source);
        }

        [Test]
        public void The_capture_time_is_iso_8601_utc_with_a_z()
        {
            var meta = JsonUtility.FromJson<Meta>(LookMeta.Json(1, LookSource.Pinch, new DateTime(2026, 11, 21, 15, 4, 5, 123, DateTimeKind.Utc)));
            Assert.AreEqual("2026-11-21T15:04:05.123Z", meta.captured_at);
        }

        [Serializable]
        sealed class Collect
        {
            public string look_ref;
            public string collected_at;
        }

        [Test]
        public void A_collect_carries_the_local_time_with_its_utc_offset()
        {
            var local = new DateTimeOffset(2026, 11, 21, 20, 14, 3, 120, TimeSpan.FromHours(-5));
            var meta = JsonUtility.FromJson<Collect>(LookMeta.CollectJson("abc123", local));
            Assert.AreEqual("abc123", meta.look_ref);
            Assert.AreEqual("2026-11-21T20:14:03.120-05:00", meta.collected_at);
        }

        [Test]
        public void A_collect_late_in_the_evening_keeps_its_local_day()
        {
            var local = new DateTimeOffset(2026, 11, 21, 23, 30, 0, TimeSpan.FromHours(-5));
            var meta = JsonUtility.FromJson<Collect>(LookMeta.CollectJson("abc123", local));
            StringAssert.StartsWith("2026-11-21T23:30:00.000", meta.collected_at);
        }

        [Test]
        public void A_collect_east_of_utc_writes_a_plus_offset()
        {
            var local = new DateTimeOffset(2026, 11, 22, 9, 0, 0, new TimeSpan(5, 30, 0));
            var meta = JsonUtility.FromJson<Collect>(LookMeta.CollectJson("abc123", local));
            Assert.AreEqual("2026-11-22T09:00:00.000+05:30", meta.collected_at);
        }

        [Test]
        public void A_local_time_is_converted_to_utc()
        {
            var local = new DateTime(2026, 11, 21, 15, 4, 5, DateTimeKind.Local);
            var meta = JsonUtility.FromJson<Meta>(LookMeta.Json(1, LookSource.Pinch, local));
            Assert.AreEqual(local.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture), meta.captured_at);
        }
    }
}
