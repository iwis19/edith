using System.Collections.Generic;
using Edith.Capture;
using Edith.Snapshots;
using UnityEngine;

namespace Edith.Scan
{
    /// What depth said along a pick's sight line when its pin was placed.
    public enum DepthAnswer
    {
        /// No depth reading: the raycaster is missing or not ready, or the line is outside what depth covers now.
        None,
        Hit,
        /// Depth covered the line and it met nothing within reach.
        Nothing,
    }

    public enum PinSource { Measured, Remembered, Far, Assumed }

    public static class ScanGeometry
    {
        /// How far along its sight line a pin sits, and why. `hitMeters` is the distance of a Hit. `remembered` is the
        /// depth kept for the line while the reply was awaited, measured along the camera's forward axis, and
        /// `cosToFacing` is the cosine between the line and that axis.
        public static (float meters, PinSource source) PinDistance(
            DepthAnswer answer, float hitMeters, float? remembered, float cosToFacing, ScanTuning tuning)
        {
            // The reply often lands while the hands are still up from the pose, and depth then meets a hand.
            var onHand = answer == DepthAnswer.Hit && hitMeters < tuning.minPinMeters;
            if (answer == DepthAnswer.Hit && !onHand) return (hitMeters, PinSource.Measured);
            if (remembered is { } forward && cosToFacing > 1e-3f) return (forward / cosToFacing, PinSource.Remembered);
            if (answer == DepthAnswer.Nothing) return (tuning.farPinMeters, PinSource.Far);
            return (tuning.assumedPinMeters, PinSource.Assumed);
        }

        /// The index of the point at the smallest angle from `line`, among those within `maxDegrees` of it, or -1 when
        /// there is none.
        public static int NearestToLine(Ray line, IReadOnlyList<Vector3> points, float maxDegrees)
        {
            var best = -1;
            var bestAngle = maxDegrees;
            for (var i = 0; i < points.Count; i++)
            {
                var to = points[i] - line.origin;
                if (to.sqrMagnitude < 1e-8f) continue;
                var angle = Vector3.Angle(line.direction, to);
                if (angle > bestAngle) continue;
                bestAngle = angle;
                best = i;
            }
            return best;
        }

        /// Where four sight lines meet the plane through `through` that faces along `normal`: a box in an image as a
        /// flat quad at its object's distance. Null when a line runs along the plane or meets it behind its origin.
        public static WorldQuad? QuadOnPlane(Ray bottomLeft, Ray bottomRight, Ray topRight, Ray topLeft, Vector3 through, Vector3 normal)
        {
            Vector3? Meet(Ray ray)
            {
                var along = Vector3.Dot(ray.direction, normal);
                if (Mathf.Abs(along) < 1e-5f) return null;
                var distance = Vector3.Dot(through - ray.origin, normal) / along;
                return distance > 0f ? ray.GetPoint(distance) : (Vector3?)null;
            }

            if (Meet(bottomLeft) is { } bl && Meet(bottomRight) is { } br && Meet(topRight) is { } tr && Meet(topLeft) is { } tl)
                return new WorldQuad(bl, br, tr, tl);
            return null;
        }

        /// The box a person at `eye` would have drawn around `quad`, hung `depth` metres from them. The box's eye is
        /// turned to the quad's centre, so the card faces the person squarely wherever the quad is in their view.
        /// Null when a corner is at or behind that eye.
        public static SightBox? BoxAround(Vector3 eye, in WorldQuad quad, float depth)
        {
            const float minCornerDepth = 0.05f;
            var centre = (quad.BottomLeft + quad.BottomRight + quad.TopRight + quad.TopLeft) * 0.25f;
            if ((centre - eye).sqrMagnitude < 1e-8f) return null;
            var pose = new Pose(eye, Quaternion.LookRotation(centre - eye, Vector3.up));
            var toEye = Quaternion.Inverse(pose.rotation);
            float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
            foreach (var corner in new[] { quad.BottomLeft, quad.BottomRight, quad.TopRight, quad.TopLeft })
            {
                var p = toEye * (corner - eye);
                if (p.z <= minCornerDepth) return null;
                xMin = Mathf.Min(xMin, p.x / p.z);
                xMax = Mathf.Max(xMax, p.x / p.z);
                yMin = Mathf.Min(yMin, p.y / p.z);
                yMax = Mathf.Max(yMax, p.y / p.z);
            }
            return new SightBox(pose, Rect.MinMaxRect(xMin, yMin, xMax, yMax), depth);
        }
    }
}
