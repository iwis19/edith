using UnityEngine;

namespace Edith.Scrapbook
{
    /// What a pinch in the Scrapbook means once it is under way: a choice at the point it was pressed, or a scrub. It
    /// scrubs only once the pinch point has moved this far sideways, so reaching toward a target, which moves it
    /// forward or up and down, never turns a choice into a scrub. Points are in the deck's frame, where x is sideways.
    public sealed class PinchIntent
    {
        /// A pinch that moves this far sideways from where it was pressed scrubs the deck.
        public const float ScrubMeters = 0.06f;
        /// A pinch pressed on the globe spins it once it moves this far in any direction across the view.
        public const float SpinMeters = 0.025f;

        readonly Vector3 pressed;
        readonly bool anyDirection;
        readonly float threshold;
        float widest;

        public bool Scrubbing { get; private set; }

        /// The farthest the pinch point has moved from where it was pressed, in metres, along the directions that count.
        public float Sideways => widest;

        /// Whether a pinch pressed at `pressed` and now at `at` has moved far enough sideways to scrub the deck.
        public static bool Scrubs(Vector3 pressed, Vector3 at) => Mathf.Abs(at.x - pressed.x) >= ScrubMeters;

        /// A pinch over the deck, which only a sideways sweep scrubs: reaching in or moving up and down still chooses.
        public PinchIntent(Vector3 pressed) : this(pressed, false, ScrubMeters) { }

        /// A pinch pressed on the globe, which spins up, down and sideways alike after a short move.
        public static PinchIntent ForGlobe(Vector3 pressed) => new PinchIntent(pressed, true, SpinMeters);

        PinchIntent(Vector3 pressed, bool anyDirection, float threshold)
        {
            this.pressed = pressed;
            this.anyDirection = anyDirection;
            this.threshold = threshold;
        }

        /// Takes the pinch point's latest position and returns true once the pinch drags. Once it has, it keeps dragging.
        public bool Move(Vector3 at)
        {
            var moved = anyDirection ? new Vector2(at.x - pressed.x, at.y - pressed.y).magnitude : Mathf.Abs(at.x - pressed.x);
            widest = Mathf.Max(widest, moved);
            if (widest >= threshold) Scrubbing = true;
            return Scrubbing;
        }
    }
}
