using UnityEngine;

namespace Edith.Scrapbook
{
    /// Where a row of `Count` cards is scrolled to. Position is the card index in the middle, which can sit between
    /// two cards while it moves; Target is the card it comes to rest on. Index 0 is the newest card and a higher
    /// index is older.
    public sealed class Deck
    {
        // The rate of the exponential approach, per second: 12 closes 63% of the gap in 83 ms.
        const float EaseRate = 12f;
        const float Settled = 0.001f;

        bool scrubbing;

        public Deck(int count, int start = 0)
        {
            Count = Mathf.Max(0, count);
            Target = Clamp(start);
            Position = Target;
        }

        public int Count { get; }
        public float Position { get; private set; }
        public int Target { get; private set; }

        int Last => Mathf.Max(0, Count - 1);

        int Clamp(int index) => Mathf.Clamp(index, 0, Last);

        /// Moves the target by `direction` cards, stopping at either end.
        public void Step(int direction) => Target = Clamp(Target + direction);

        public void JumpTo(int index) => Target = Clamp(index);

        /// Moves the deck by `cards` under a dragging pinch. The deck follows the hand, not the target, until Release.
        public void Scrub(float cards)
        {
            scrubbing = true;
            Position = Mathf.Clamp(Position + cards, 0f, Last);
        }

        /// Ends a scrub: the nearest card becomes the target.
        public void Release()
        {
            if (!scrubbing) return;
            scrubbing = false;
            Target = Clamp(Mathf.RoundToInt(Position));
        }

        /// Eases Position toward Target.
        public void Advance(float deltaTime)
        {
            if (scrubbing) return;
            Position = Mathf.Lerp(Position, Target, 1f - Mathf.Exp(-EaseRate * deltaTime));
            if (Mathf.Abs(Position - Target) < Settled) Position = Target;
        }
    }
}
