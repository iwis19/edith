namespace Edith.Snapshots
{
    /// How narrow a card may be, shared by snapshots and the Scrapbook so their cards match.
    public static class CardWidth
    {
        /// No card's photo is narrower than this over its distance from the eye, about 14.5 degrees, so a fact never
        /// wraps into a thin column under it.
        public const float MinPhotoTangent = 0.18f / 0.70f;
    }
}
