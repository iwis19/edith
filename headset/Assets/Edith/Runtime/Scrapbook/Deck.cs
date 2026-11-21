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
        const float FormSeconds = 0.2f;

        bool scrubbing;
        float elapsed;

        /// `reforming` starts the deck unformed, with its cards folded into the middle, and Form rises to 1 as it advances.
        public Deck(int count, int start = 0, bool reforming = false)
        {
            Form = reforming ? 0f : 1f;
            Count = Mathf.Max(0, count);
            Target = Clamp(start);
            Position = Target;
        }

        public int Count { get; }
        public float Position { get; private set; }
        public int Target { get; private set; }
        /// How far a deck that has just replaced another has re-formed, eased from 0 to 1 over FormSeconds.
        public float Form { get; private set; }

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

        /// Eases Position toward Target, and Form toward 1.
        public void Advance(float deltaTime)
        {
            if (Form < 1f)
            {
                elapsed += deltaTime;
                var t = Mathf.Clamp01(elapsed / FormSeconds);
                Form = t * t * (3f - 2f * t);
            }
            if (scrubbing) return;
            Position = Mathf.Lerp(Position, Target, 1f - Mathf.Exp(-EaseRate * deltaTime));
            if (Mathf.Abs(Position - Target) < Settled) Position = Target;
        }
    }
}
