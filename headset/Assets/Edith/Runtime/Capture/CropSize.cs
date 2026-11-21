using UnityEngine;

namespace Edith.Capture
{
    public static class CropSize
    {
        /// The longest side of a Look's photo, which is both what hangs and what the backend gets.
        public const int MaxSide = 768;

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
