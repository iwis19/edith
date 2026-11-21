using System.Collections.Generic;
using Edith.Hands;
using Edith.Snapshots;

namespace Edith.Grab
{
    /// A fist grab per hand, each on its own tracker. A card one hand holds is not offered to the other, and when
    /// both close on the same card in one frame the right hand takes it.
    public sealed class GrabPair
    {
        readonly GrabTracker[] trackers;
        readonly List<(int id, SightBox box)> offered = new List<(int id, SightBox box)>();

        public GrabPair(GrabTuning tuning) =>
            trackers = new[] { new GrabTracker(tuning), new GrabTracker(tuning) };

        public GrabTracker Tracker(HandSide side) => trackers[(int)side];

        public (GrabReading right, GrabReading left) Step(in HandFrame right, in HandFrame left,
            IReadOnlyList<(int id, SightBox box)> cards)
        {
            var rightReading = trackers[0].Step(right, Without(cards, trackers[1]));
            var leftReading = trackers[1].Step(left, Without(cards, trackers[0]));
            return (rightReading, leftReading);
        }

        IReadOnlyList<(int id, SightBox box)> Without(IReadOnlyList<(int id, SightBox box)> cards, GrabTracker holder)
        {
            if (!holder.IsHolding) return cards;
            offered.Clear();
            for (var i = 0; i < cards.Count; i++)
                if (cards[i].id != holder.HeldCard) offered.Add(cards[i]);
            return offered;
        }
    }
}
