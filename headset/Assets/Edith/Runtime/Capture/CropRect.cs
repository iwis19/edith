using UnityEngine;

namespace Edith.Capture
{
    public static class CropRect
    {
        /// The pixel rect to read back for a region whose corners are at `bl`, `br`, `tr` and `tl` in the camera's
        /// viewport space ((0, 0) is the bottom left): their bounding box, clamped to the image. Null when a clamped
        /// side is under `minPixels` or under half of the region is on the image.
        public static RectInt? FromViewport(Vector2 bl, Vector2 br, Vector2 tr, Vector2 tl, Vector2Int resolution, int minPixels = 64)
        {
            var x0 = Mathf.Min(Mathf.Min(bl.x, br.x), Mathf.Min(tr.x, tl.x)) * resolution.x;
            var x1 = Mathf.Max(Mathf.Max(bl.x, br.x), Mathf.Max(tr.x, tl.x)) * resolution.x;
            var y0 = Mathf.Min(Mathf.Min(bl.y, br.y), Mathf.Min(tr.y, tl.y)) * resolution.y;
            var y1 = Mathf.Max(Mathf.Max(bl.y, br.y), Mathf.Max(tr.y, tl.y)) * resolution.y;

            var area = (x1 - x0) * (y1 - y0);
            if (!(area > 0f)) return null;

            var cx0 = Mathf.Clamp(x0, 0, resolution.x);
            var cx1 = Mathf.Clamp(x1, 0, resolution.x);
            var cy0 = Mathf.Clamp(y0, 0, resolution.y);
            var cy1 = Mathf.Clamp(y1, 0, resolution.y);
            if ((cx1 - cx0) * (cy1 - cy0) < area * 0.5f) return null;

            var left = Mathf.FloorToInt(cx0);
            var bottom = Mathf.FloorToInt(cy0);
            var width = Mathf.CeilToInt(cx1) - left;
            var height = Mathf.CeilToInt(cy1) - bottom;
            if (width < minPixels || height < minPixels) return null;
            return new RectInt(left, bottom, width, height);
        }
    }
}
