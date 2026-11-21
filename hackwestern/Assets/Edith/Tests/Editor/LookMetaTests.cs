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
            var meta = JsonUtility.FromJson<Meta>(LookMeta.Json(7, new DateTime(2026, 11, 21, 15, 4, 5, 123, DateTimeKind.Utc)));
            Assert.AreEqual(7, meta.look_id);
            Assert.AreEqual("pinch", meta.source);
        }

        [Test]
        public void The_capture_time_is_iso_8601_utc_with_a_z()
        {
            var meta = JsonUtility.FromJson<Meta>(LookMeta.Json(1, new DateTime(2026, 11, 21, 15, 4, 5, 123, DateTimeKind.Utc)));
            Assert.AreEqual("2026-11-21T15:04:05.123Z", meta.captured_at);
        }

        [Test]
        public void A_local_time_is_converted_to_utc()
        {
            var local = new DateTime(2026, 11, 21, 15, 4, 5, DateTimeKind.Local);
            var meta = JsonUtility.FromJson<Meta>(LookMeta.Json(1, local));
            Assert.AreEqual(local.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture), meta.captured_at);
        }
    }
}
