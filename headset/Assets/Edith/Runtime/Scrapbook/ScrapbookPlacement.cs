using UnityEngine;

namespace Edith.Scrapbook
{
    /// Where the Scrapbook hangs, in the deck's frame: the eye that opened it is at the origin, the frame is level and
    /// +z is ahead. Everything is at least 0.6 m from the eye, so it is clear of the hands and of near furniture,
    /// and is sized to look as large as it would nearer. Metres and degrees.
    public static class ScrapbookPlacement
    {
        /// The point `ahead` metres in front of the eye and `degrees` below its level.
        public static Vector3 Below(float ahead, float degrees) =>
            new Vector3(0f, -ahead * Mathf.Tan(degrees * Mathf.Deg2Rad), ahead);

        /// The cards' sizes and spacing are those of a deck 0.45 m out, grown by this much.
        public const float CardScale = 1.5f;
        /// The middle card's photo is centred here.
        public const float CardAhead = 0.70f;
        public static readonly Vector3 CardCentre = new Vector3(0f, -0.04f, CardAhead);
        public const float CardWidth = 0.20f * CardScale;
        public const float FirstAside = 0.16f * CardScale, NextAside = 0.07f * CardScale, Back = 0.06f * CardScale;

        /// The lens bar's two rows grow by this much and are centred here, the tabs over the chips.
        public const float BarScale = 1.5f;
        public static readonly Vector3 BarCentre = Below(0.66f, 19f);
        public const float BarRowGap = 0.021f * BarScale;

        public static readonly Vector3 GlobeCentre = Below(0.70f, 30f);
        public const float GlobeRadius = 0.12f;
        /// Pins, labels and captions on the globe and the map are 1.4 times what they were at the size of a 0.085 m globe
        /// or a 0.30 m map.
        public const float MapDetailScale = 1.4f;

        public static readonly Vector3 MapCentre = Below(0.78f, 32f);
        public const float MapWidth = 0.42f;
        /// The map leans back this far from upright.
        public const float MapLean = 40f;
    }
}
