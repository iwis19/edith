using System.Collections.Generic;
using Edith.UI;
using UnityEngine;

namespace Edith.Ask
{
    /// Where the answer orb's row, the orb with its caption to the right, can go without covering a snapshot. Spots in
    /// the view are points in the eye's tangent space (x/z, y/z), so a rect there is the patch of view something takes.
    public static class OrbPlacement
    {
        /// How far ahead of the eye a row that follows the head sits.
        public const float Depth = 0.55f;
        /// The usual spot, as the orb's centre: low and left of the line of sight, about 17° left and 11° down, so the
        /// row stays inside the centre crop of a headset recording.
        public static readonly Vector2 Home = new Vector2(-0.3f, -0.2f);
        /// The row's half height; the two-line caption is a little shorter than the orb.
        public const float RowHalfHeightMetres = 0.02f;
        /// The clear space between a snapshot photo's top edge and an orb perched on it, past the photo's border.
        public const float PerchGapMetres = 0.012f;
        /// The clear space kept between the row and a snapshot, in tangent units (about 2°).
        public const float Margin = 0.035f;

        // The spots tried when home is covered: a grid over the part of the view that is comfortable to read.
        const float Step = 0.05f, MinLeft = -0.8f, MaxRight = 0.75f, MinY = -0.6f, MaxY = 0.4f;
        // What a snapshot takes up besides its photo: the border around it and the card text under it.
        const float BorderMetres = 0.003f, TextGapMetres = 0.006f, TextMetres = 0.06f, MinTextWidthMetres = 0.09f;
        const float NearestDepth = 0.05f;
        const float CoveredCost = 1000f;

        /// The patch of view a snapshot photo centred at `photo`, `size` metres wide and high, takes from `eye`,
        /// with its border and card text. Null when any of it is at or behind the eye.
        public static Rect? Covered(Pose eye, Pose photo, Vector2 size)
        {
            var halfWidth = Mathf.Max(size.x * 0.5f + BorderMetres, MinTextWidthMetres * 0.5f);
            var top = size.y * 0.5f + BorderMetres;
            var bottom = -(size.y * 0.5f + TextGapMetres + TextMetres);
            return Seen(eye, photo.position, photo.rotation, -halfWidth, halfWidth, bottom, top);
        }

        /// The patch of view a panel `quad` takes from `eye`, or null when any of it is at or behind the eye.
        public static Rect? Covered(Pose eye, PanelQuad quad)
        {
            var half = quad.Size * 0.5f;
            return Seen(eye, quad.Pose.position, quad.Pose.rotation, -half.x, half.x, -half.y, half.y);
        }

        /// The row's patch of view for an orb centred at the head-relative spot `orb`, facing the eye, with a row
        /// `rowMetres` long.
        public static Rect HeadRow(Vector2 orb, float rowMetres)
        {
            var eye = new Pose(Vector3.zero, Quaternion.identity);
            var centre = AtHead(eye, orb);
            return WorldRow(eye, centre, FacingEye(eye, centre), rowMetres).Value;
        }

        /// The turn that faces a row at `position` toward `eye`, upright to the head so it rolls with it.
        public static Quaternion FacingEye(Pose eye, Vector3 position) =>
            Quaternion.LookRotation(position - eye.position, eye.rotation * Vector3.up);

        /// The row's patch of view from `eye` for an orb centred at `orb` in a plane turned by `rotation`, or null
        /// when any of it is at or behind the eye.
        public static Rect? WorldRow(Pose eye, Vector3 orb, Quaternion rotation, float rowMetres)
        {
            var radius = AnswerOrb.OrbMetres * 0.5f;
            return Seen(eye, orb, rotation, -radius, rowMetres - radius, -RowHalfHeightMetres, RowHalfHeightMetres);
        }

        /// Where a head-relative spot is in the world.
        public static Vector3 AtHead(Pose eye, Vector2 spot) => eye.position + eye.rotation * new Vector3(spot.x * Depth, spot.y * Depth, Depth);

        /// The centre of an orb resting on a snapshot photo centred at `photo`, `size` metres wide and high: in the
        /// photo's plane, above its top edge, with the orb's left edge on the photo's left edge.
        public static Vector3 Perch(Pose photo, Vector2 size)
        {
            var radius = AnswerOrb.OrbMetres * 0.5f;
            return photo.position + photo.rotation * new Vector3(radius - size.x * 0.5f, size.y * 0.5f + PerchGapMetres + radius, 0f);
        }

        /// How much of `row` the patches in `covered` take, each grown by Margin. Zero when the row is clear.
        public static float Overlap(Rect row, IReadOnlyList<Rect> covered)
        {
            var total = 0f;
            for (var i = 0; i < covered.Count; i++)
            {
                var c = covered[i];
                var w = Mathf.Min(row.xMax, c.xMax + Margin) - Mathf.Max(row.xMin, c.xMin - Margin);
                var h = Mathf.Min(row.yMax, c.yMax + Margin) - Mathf.Max(row.yMin, c.yMin - Margin);
                if (w > 0f && h > 0f) total += w * h;
            }
            return total;
        }

        /// The clear head-relative spot nearest `from`, or the least covered one when none is clear.
        public static Vector2 Nearest(Vector2 from, IReadOnlyList<Rect> covered, float rowMetres)
        {
            var best = Home;
            var bestCost = Cost(Home, from, covered, rowMetres);
            var rightmost = MaxRight - HeadRow(Vector2.zero, rowMetres).xMax;
            for (var x = MinLeft + AnswerOrb.OrbMetres * 0.5f / Depth; x <= rightmost; x += Step)
            for (var y = MinY; y <= MaxY; y += Step)
            {
                var spot = new Vector2(x, y);
                var cost = Cost(spot, from, covered, rowMetres);
                if (cost >= bestCost) continue;
                bestCost = cost;
                best = spot;
            }
            return best;
        }

        static float Cost(Vector2 spot, Vector2 from, IReadOnlyList<Rect> covered, float rowMetres) =>
            Overlap(HeadRow(spot, rowMetres), covered) * CoveredCost + Vector2.Distance(spot, from);

        /// The bounds, in `eye`'s tangent space, of the rect from (left, bottom) to (right, top) in the plane at
        /// `centre` turned by `rotation`.
        static Rect? Seen(Pose eye, Vector3 centre, Quaternion rotation, float left, float right, float bottom, float top)
        {
            var toEye = Quaternion.Inverse(eye.rotation);
            float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
            for (var i = 0; i < 4; i++)
            {
                var corner = centre + rotation * new Vector3(i % 3 == 0 ? left : right, i < 2 ? bottom : top, 0f);
                var p = toEye * (corner - eye.position);
                if (p.z <= NearestDepth) return null;
                xMin = Mathf.Min(xMin, p.x / p.z);
                xMax = Mathf.Max(xMax, p.x / p.z);
                yMin = Mathf.Min(yMin, p.y / p.z);
                yMax = Mathf.Max(yMax, p.y / p.z);
            }
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }
    }
}
