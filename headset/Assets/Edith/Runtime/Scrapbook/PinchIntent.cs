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

        readonly Vector3 pressed;
        float widest;

        public bool Scrubbing { get; private set; }

        /// The farthest the pinch point has moved sideways from where it was pressed, in metres.
        public float Sideways => widest;

        /// Whether a pinch pressed at `pressed` and now at `at` has moved far enough sideways to scrub the deck.
        public static bool Scrubs(Vector3 pressed, Vector3 at) => Mathf.Abs(at.x - pressed.x) >= ScrubMeters;

        /// A pinch over the deck, which only a sideways sweep scrubs: reaching in or moving up and down still chooses.
        public PinchIntent(Vector3 pressed)
        {
            this.pressed = pressed;
        }

        /// Takes the pinch point's latest position and returns true once the pinch drags. Once it has, it keeps dragging.
        public bool Move(Vector3 at)
        {
            widest = Mathf.Max(widest, Mathf.Abs(at.x - pressed.x));
            if (widest >= ScrubMeters) Scrubbing = true;
            return Scrubbing;
        }
    }
}
