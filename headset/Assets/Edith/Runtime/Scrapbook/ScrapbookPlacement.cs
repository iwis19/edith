using UnityEngine;

namespace Edith.Scrapbook
{
    /// Where the Scrapbook hangs, in the deck's frame: the eye that opened it is at the origin, the frame is level and
    /// +z is ahead. Everything is at least 0.6 m from the eye, so it is clear of the hands and of near furniture,
    /// and is sized to look as large as it would nearer. Metres and degrees.
    public static class ScrapbookPlacement
    {
        /// The point `ahead` metres in front of the eye and `degrees` below its level, or above it when negative.
        public static Vector3 Below(float ahead, float degrees) =>
            new Vector3(0f, -ahead * Mathf.Tan(degrees * Mathf.Deg2Rad), ahead);

        /// The cards' sizes and spacing are those of a deck 0.45 m out, grown by this much.
        public const float CardScale = 1.25f;
        /// The middle card's photo is centred here.
        public const float CardAhead = 0.70f;
        public static readonly Vector3 CardCentre = Below(CardAhead, -5f);
        public const float CardWidth = 0.20f * CardScale;
        public const float FirstAside = 0.16f * CardScale, NextAside = 0.07f * CardScale, Back = 0.06f * CardScale;

        /// The lens bar's two rows grow by this much and are centred here, the tabs over the chips.
        public const float BarScale = 1.3f;
        public static readonly Vector3 BarCentre = Below(0.66f, 13f);
        public const float BarRowGap = 0.021f * BarScale;

        /// The map panel faces the eye and leans back from that by MapLean. It shows the full width of a square map
        /// image and the share of its height that makes it MapHeight tall.
        public static readonly Vector3 MapCentre = Below(0.80f, 23f);
        public const float MapWidth = 0.30f;
        public const float MapVisibleShare = 0.6f;
        public const float MapHeight = MapWidth * MapVisibleShare;
        public const float MapLean = 10f;
        public static Quaternion MapRotation => Quaternion.LookRotation(MapCentre, Vector3.up) * Quaternion.Euler(MapLean, 0f, 0f);
    }
}
