using System;
using System.Globalization;
using UnityEngine;

namespace Edith.Look
{
    /// What started a Look: a box pinched around something, or a scan pin opened with a pinch.
    public enum LookSource { Pinch, Scan }

    /// The `meta` fields of a POST /look and a POST /collect.
    public static class LookMeta
    {
        [Serializable]
        sealed class Wire
        {
            public int look_id;
            public string source;
            public string captured_at;
            public string[] interests;
        }

        [Serializable]
        sealed class CollectWire
        {
            public string look_ref;
            public string collected_at;
        }

        public static string Json(int lookId, LookSource source, DateTime capturedAt, string[] interests = null) => JsonUtility.ToJson(new Wire
        {
            look_id = lookId,
            source = source == LookSource.Scan ? "scan" : "pinch",
            captured_at = Utc(capturedAt),
            interests = interests ?? new string[0],
        });

        /// `collectedAt` is the headset's local time. Its UTC offset goes with it, because the backend files the
        /// memory under the calendar day in that offset.
        public static string CollectJson(string lookRef, DateTimeOffset collectedAt) => JsonUtility.ToJson(new CollectWire
        {
            look_ref = lookRef,
            collected_at = collectedAt.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture),
        });

        static string Utc(DateTime time) =>
            time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    }
}
