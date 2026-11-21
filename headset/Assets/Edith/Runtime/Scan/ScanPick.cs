using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Edith.Scan
{
    /// One thing a scan found worth a look. `ImagePoint` and `ImageBox` are fractions of the photo as /scan gives
    /// them: origin at the top left, y down.
    public readonly struct ScanPick
    {
        public readonly string Name, Hook;
        public readonly Vector2 ImagePoint;
        public readonly Rect ImageBox;
        /// The index of the open hunt target the thing fits, or null: always null outside a hunt.
        public readonly int? Target;

        public ScanPick(string name, string hook, Vector2 imagePoint, Rect imageBox, int? target = null)
        {
            Name = name;
            Hook = hook;
            ImagePoint = imagePoint;
            ImageBox = imageBox;
            Target = target;
        }

        /// Unity viewport space has its origin at the bottom left with y up, so y flips.
        public Vector2 ViewportPoint => new Vector2(ImagePoint.x, 1f - ImagePoint.y);
        public Rect ViewportBox => Rect.MinMaxRect(ImageBox.xMin, 1f - ImageBox.yMax, ImageBox.xMax, 1f - ImageBox.yMin);
    }

    public static class ScanReply
    {
        [Serializable]
        sealed class Body
        {
            public Pick[] picks;
        }

        [Serializable]
        sealed class Pick
        {
            public string name, hook;
            public float[] point, box;
            // JsonUtility cannot tell a null from a 0, so the reader turns a null into -1 first; so does a missing key.
            public int target = -1;
        }

        static readonly Regex NullTarget = new Regex(@"""target""\s*:\s*null", RegexOptions.Compiled);

        /// The picks in a /scan reply, most interesting first. A pick without a point, or with an empty box, is left
        /// out. Throws ArgumentException when `json` is not JSON.
        public static List<ScanPick> Parse(string json)
        {
            var picks = new List<ScanPick>();
            var body = JsonUtility.FromJson<Body>(NullTarget.Replace(json, "\"target\":-1"));
            if (body?.picks == null) return picks;
            foreach (var pick in body.picks)
            {
                if (pick?.point == null || pick.point.Length != 2 || pick.box == null || pick.box.Length != 4) continue;
                var box = Rect.MinMaxRect(pick.box[0], pick.box[1], pick.box[2], pick.box[3]);
                if (box.width <= 0f || box.height <= 0f) continue;
                picks.Add(new ScanPick(pick.name ?? "", pick.hook ?? "", new Vector2(pick.point[0], pick.point[1]), box,
                    pick.target >= 0 ? pick.target : (int?)null));
            }
            return picks;
        }
    }
}
