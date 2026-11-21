using System;
using System.Globalization;
using UnityEngine;

namespace Edith.Look
{
    /// The `meta` field of a POST /look.
    public static class LookMeta
    {
        [Serializable]
        sealed class Wire
        {
            public int look_id;
            public string source = "pinch";
            public string captured_at;
        }

        public static string Json(int lookId, DateTime capturedAt) => JsonUtility.ToJson(new Wire
        {
            look_id = lookId,
            captured_at = capturedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
        });
    }
}
