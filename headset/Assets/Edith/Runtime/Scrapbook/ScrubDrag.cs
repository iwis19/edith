using System;
using System.Collections.Generic;
using UnityEngine;

namespace Edith.Scrapbook
{
    /// One drag of the deck by a pinch. It turns the hand's movement, in cards, into a deck position, and says which
    /// card the deck comes to rest on when the pinch ends.
    public sealed class ScrubDrag
    {
        readonly float start;
        readonly int last;
        readonly List<(float time, float position)> history = new List<(float, float)>();
        // Where the hand has taken the deck, and where the deck is showing: it trails the hand by up to half the
        // deadband, so a backward shake smaller than the deadband leaves it still.
        float raw, shown;

        /// A drag of a deck of cards 0 to `last`, starting with the deck at `start`.
        public ScrubDrag(float start, int last)
        {
            this.start = start;
            this.last = last;
            Position = start;
        }

        public float Position { get; private set; }

        /// The hand moved the deck by `cards` at time `now`, in seconds.
        public void Move(float cards, float now)
        {
            if (history.Count == 0) history.Add((now, Position));
            raw += cards;
            var half = ScrubTuning.DeadbandMeters / ScrubTuning.MetersPerCard * 0.5f;
            shown = Mathf.Clamp(shown, raw - half, raw + half);
            Position = Mathf.Clamp(start + shown, 0f, last);
            history.Add((now, Position));
            while (history.Count > 1 && now - history[0].time > ScrubTuning.HistorySeconds) history.RemoveAt(0);
        }

        /// The card the deck rests on when the pinch ends at `now`. Movement in the last LookbackSeconds is undone. A
        /// drag lands on the next card in its direction once it has moved SnapCards that way, and never behind where
        /// it had got to because of a smaller reversal at its end.
        public int Land(float now)
        {
            if (history.Count == 0) return Mathf.Clamp(Mathf.RoundToInt(start), 0, last);
            var cut = now - ScrubTuning.LookbackSeconds;
            var at = history[0].position;
            var furthest = 0f;
            foreach (var (time, position) in history)
            {
                if (time > cut) break;
                at = position;
                if (Mathf.Abs(position - start) > Mathf.Abs(furthest)) furthest = position - start;
            }
            var direction = Math.Sign(furthest);
            var rests = at;
            var reversed = (start + furthest - at) * direction;
            if (direction != 0 && reversed > 0f && reversed < ScrubTuning.SnapCards) rests = start + furthest;
            var card = direction > 0 ? Mathf.Floor(rests + 1f - ScrubTuning.SnapCards)
                : direction < 0 ? Mathf.Ceil(rests - 1f + ScrubTuning.SnapCards)
                : Mathf.Round(rests);
            return Mathf.Clamp((int)card, 0, last);
        }
    }
}
