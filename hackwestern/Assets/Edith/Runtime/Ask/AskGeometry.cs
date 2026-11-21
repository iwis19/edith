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
    }
}
