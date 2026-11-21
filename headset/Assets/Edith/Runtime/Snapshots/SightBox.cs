using UnityEngine;

namespace Edith.Snapshots
{
    /// A box the user drew, as they saw it: a rect in the eye's tangent space (x/z, y/z) and the depth it was drawn at.
    /// The box in the world is that rect on the plane `Depth` metres in front of the eye, facing the eye.
    public readonly struct SightBox
    {
        public readonly Pose Eye;
        public readonly Rect Tangent;
        public readonly float Depth;

        public SightBox(Pose eye, Rect tangent, float depth)
        {
            Eye = eye;
            Tangent = tangent;
            Depth = depth;
        }

        public Vector3 Center => Point(Tangent.center.x, Tangent.center.y);

        /// Width and height in metres.
        public Vector2 Size => Tangent.size * Depth;

        /// The bounds of this box in the tangent space of `eye`, or null when any corner is at or behind that eye.
        public Rect? SeenFrom(Pose eye)
        {
            const float minCornerDepth = 0.05f;
            var toEye = Quaternion.Inverse(eye.rotation);
            float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
            for (var i = 0; i < 4; i++)
            {
                var corner = Point(i % 3 == 0 ? Tangent.xMin : Tangent.xMax, i < 2 ? Tangent.yMin : Tangent.yMax);
                var p = toEye * (corner - eye.position);
                if (p.z <= minCornerDepth) return null;
                xMin = Mathf.Min(xMin, p.x / p.z);
                xMax = Mathf.Max(xMax, p.x / p.z);
                yMin = Mathf.Min(yMin, p.y / p.z);
                yMax = Mathf.Max(yMax, p.y / p.z);
            }
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        /// True when `point`, seen from `eye`, lies on this box or within `margin` (a tangent, about margin radians) of it.
        public bool InLine(Vector3 eye, Vector3 point, float margin)
        {
            var toPoint = point - eye;
            if (toPoint.sqrMagnitude < 1e-8f) return false;
            if (SeenFrom(new Pose(eye, Quaternion.LookRotation(toPoint))) is not { } seen) return false;
            return seen.xMin - margin <= 0f && seen.xMax + margin >= 0f && seen.yMin - margin <= 0f && seen.yMax + margin >= 0f;
        }

        /// The point of the box's tangent space (`tx`, `ty`) on the plane `depth` metres in front of the eye.
        public Vector3 PointAt(float tx, float ty, float depth) =>
            Eye.position + Eye.rotation * new Vector3(tx * depth, ty * depth, depth);

        Vector3 Point(float tx, float ty) => PointAt(tx, ty, Depth);
    }
}
