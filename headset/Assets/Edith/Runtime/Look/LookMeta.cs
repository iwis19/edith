using System;
using System.Globalization;
using UnityEngine;

namespace Edith.Look
{
    /// The `meta` fields of a POST /look and a POST /collect.
    public static class LookMeta
    {
        [Serializable]
        sealed class Wire
        {
            public int look_id;
            public string source = "pinch";
            public string captured_at;
        }

        [Serializable]
        sealed class CollectWire
        {
            public string look_ref;
            public string collected_at;
        }

        public static string Json(int lookId, DateTime capturedAt) => JsonUtility.ToJson(new Wire
        {
            look_id = lookId,
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
