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
        }

        [Serializable]
        sealed class CollectWire
        {
            public string look_ref;
            public string collected_at;
        }

        public static string Json(int lookId, LookSource source, DateTime capturedAt) => JsonUtility.ToJson(new Wire
        {
            look_id = lookId,
            source = source == LookSource.Scan ? "scan" : "pinch",
            captured_at = Utc(capturedAt),
        });

        public static string CollectJson(string lookRef, DateTime collectedAt) => JsonUtility.ToJson(new CollectWire
        {
            look_ref = lookRef,
            collected_at = Utc(collectedAt),
        });

        static string Utc(DateTime time) =>
            time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    }
}
