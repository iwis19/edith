using System.Collections.Generic;
using Edith.Snapshots;
using UnityEngine;

namespace Edith.Ask
{
    public static class AskGeometry
    {
        /// How far from the line of sight a point may be and still count as looked at.
        public const float MaxGazeDegrees = 25f;

        /// The index of the point with the smallest angle between `gaze` and the direction from `eye` to it, or -1
        /// when none is within MaxGazeDegrees of `gaze`.
        public static int NearestToGaze(Vector3 eye, Vector3 gaze, IReadOnlyList<Vector3> points)
        {
            var nearest = -1;
            var best = float.PositiveInfinity;
            for (var i = 0; i < points.Count; i++)
            {
                var to = points[i] - eye;
                if (to.sqrMagnitude < 1e-8f) continue;
                var angle = Vector3.Angle(gaze, to);
                if (angle > MaxGazeDegrees || angle >= best) continue;
                best = angle;
                nearest = i;
            }
            return nearest;
        }

        /// `width` by `height` scaled down to at most `maxLongSide` on its longer side, keeping the aspect ratio.
        /// Never scales up.
        public static Vector2Int FitLongSide(int width, int height, int maxLongSide)
        {
            var longSide = Mathf.Max(width, height);
            if (longSide <= maxLongSide) return new Vector2Int(Mathf.Max(1, width), Mathf.Max(1, height));
            var scale = maxLongSide / (float)longSide;
            return new Vector2Int(
                width >= height ? maxLongSide : Mathf.Max(1, Mathf.RoundToInt(width * scale)),
                height >= width ? maxLongSide : Mathf.Max(1, Mathf.RoundToInt(height * scale)));
        }
    }
}
