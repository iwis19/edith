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
        /// when none is within MaxGazeDegrees of `gaze`. `degrees` is that smallest angle.
        public static int NearestToGaze(Vector3 eye, Vector3 gaze, IReadOnlyList<Vector3> points, out float degrees)
        {
            var nearest = -1;
            var best = float.PositiveInfinity;
            degrees = best;
            for (var i = 0; i < points.Count; i++)
            {
                var to = points[i] - eye;
                if (to.sqrMagnitude < 1e-8f) continue;
                var angle = Vector3.Angle(gaze, to);
                if (angle > MaxGazeDegrees || angle >= best) continue;
                best = angle;
                nearest = i;
            }
            degrees = best;
            return nearest;
        }

        /// True when the Scan pin should be the question's hint instead of the snapshot photo: a pin was found and it
        /// is nearer the line of sight than the photo, or there is no photo.
        public static bool PinWins(bool hasPin, float pinDegrees, bool hasPhoto, float photoDegrees) =>
            hasPin && (!hasPhoto || pinDegrees < photoDegrees);
    }
}
