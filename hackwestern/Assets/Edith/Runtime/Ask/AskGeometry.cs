using System.Collections.Generic;
using Edith.Snapshots;
using UnityEngine;

namespace Edith.Ask
{
    public static class AskGeometry
    {
        /// The index of the point with the smallest angle between `gaze` and the direction from `eye` to it, or -1
        /// when there are none.
        public static int NearestToGaze(Vector3 eye, Vector3 gaze, IReadOnlyList<Vector3> points)
        {
            var nearest = -1;
            var best = float.PositiveInfinity;
            for (var i = 0; i < points.Count; i++)
            {
                var to = points[i] - eye;
                if (to.sqrMagnitude < 1e-8f) continue;
                var angle = Vector3.Angle(gaze, to);
                if (angle >= best) continue;
                best = angle;
                nearest = i;
            }
            return nearest;
        }

        /// True when `point`, seen from `eye`, lies on `box` or within `margin` (a tangent, about margin radians) of it.
        public static bool InLine(in SightBox box, Vector3 eye, Vector3 point, float margin)
        {
            var toPoint = point - eye;
            if (toPoint.sqrMagnitude < 1e-8f) return false;
            if (box.SeenFrom(new Pose(eye, Quaternion.LookRotation(toPoint))) is not { } seen) return false;
            return seen.xMin - margin <= 0f && seen.xMax + margin >= 0f && seen.yMin - margin <= 0f && seen.yMax + margin >= 0f;
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
