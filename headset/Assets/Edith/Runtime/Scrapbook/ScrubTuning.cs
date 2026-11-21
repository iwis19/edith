namespace Edith.Scrapbook
{
    /// The numbers that decide how a deck drag moves the cards and where it comes to rest.
    public static class ScrubTuning
    {
        /// A pinch dragged this far sideways scrolls one card.
        public const float MetersPerCard = 0.07f;
        /// Hand movement against the drag's direction smaller than this, measured from the furthest point reached, does
        /// not move the deck.
        public const float DeadbandMeters = 0.015f;
        /// On release the deck snaps from where it was this long before: opening the fingers and the hand settling move
        /// the pinch point just before the pinch ends.
        public const float LookbackSeconds = 0.12f;
        /// A drag that moved the deck this many cards in its direction lands on the next card that way, and a smaller
        /// reversal at its end does not turn it back.
        public const float SnapCards = 0.25f;
        /// How long the positions of a drag are kept.
        public const float HistorySeconds = 2f;
    }
}
