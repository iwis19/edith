namespace Edith.Hands
{
    public enum RayEvent { None, Pressed, Clicked, Released, Cancelled }

    /// What the hand rays did this frame. Targets are the page's own numbers, and -1 is nothing.
    public readonly struct RayReading
    {
        public readonly RayEvent Event;
        /// The hand whose pinch is under way, or null between pinches.
        public readonly HandSide? Owner;
        /// What the pinching hand's ray is on, or between pinches what either ray is on, the right hand first.
        public readonly int Hovered;
        /// What the pinch under way was pressed on, also on the frame it ends.
        public readonly int Pressed;

        public RayReading(RayEvent rayEvent, HandSide? owner, int hovered, int pressed)
        {
            Event = rayEvent;
            Owner = owner;
            Hovered = hovered;
            Pressed = pressed;
        }
    }

    /// One hand's ray this frame, as it is drawn.
    public struct RayAim
    {
        public bool Shown;
        public UnityEngine.Ray Ray;
        /// How far the ray reaches: to the page where it meets it, or a fixed length when it misses the page.
        public float Distance;
        /// The ray meets the page, where the cursor shows, whether or not on something selectable.
        public bool OnPage;
        public bool OnTarget;
        /// How closed the pinch is, 0 to 1.
        public float Pinch;
        /// This hand's pinch is under way.
        public bool Held;
    }

    /// Finds what `ray` points at: a target number, or -1, and how far along the ray it meets the page, or 0 when it
    /// misses the page.
    public delegate int RayTargets(UnityEngine.Ray ray, out float distance);

    /// Selecting with Horizon OS's hand rays, as its own menus do: aim, pinch, and let go on the same target to click
    /// it. One hand selects at a time. The pinch flag must stay off for ReleaseSeconds to count as letting go, and
    /// the target is read on the first frame it went off, before the opening fingers swing the ray. Deterministic in
    /// HandFrame.Time.
    public sealed class RaySelect
    {
        public const float ReleaseSeconds = 0.08f;
        /// A pinching hand lost for this long gives up its pinch.
        public const float LostSeconds = 0.2f;
        /// A ray that misses the page is drawn this long.
        public const float FreeLength = 0.5f;

        readonly RayAim[] aims = new RayAim[2];
        readonly bool[] wasPinching = { true, true };
        HandSide? owner;
        int pressed = -1, releasedOn = -1;
        float offSince = -1f, lostSince = -1f;

        public RayAim Aim(HandSide side) => aims[(int)side];

        /// Forgets the pinch under way. A pinch already held when this is called selects nothing until it is let go.
        public void Reset()
        {
            owner = null;
            pressed = -1;
            wasPinching[0] = wasPinching[1] = true;
            aims[0] = aims[1] = default;
        }

        public RayReading Step(in HandFrame right, in HandFrame left, RayTargets targets)
        {
            var onRight = Point(0, right, targets);
            var onLeft = Point(1, left, targets);
            var happened = RayEvent.None;
            var endedOn = -1;

            if (owner is HandSide side)
            {
                var hand = side == HandSide.Right ? right : left;
                var on = side == HandSide.Right ? onRight : onLeft;
                if (!hand.Pointing || !hand.Tracked)
                {
                    if (lostSince < 0f) lostSince = hand.Time;
                    if (hand.Time - lostSince >= LostSeconds) happened = RayEvent.Cancelled;
                }
                else
                {
                    lostSince = -1f;
                    if (hand.Pinching) offSince = -1f;
                    else
                    {
                        if (offSince < 0f)
                        {
                            offSince = hand.Time;
                            releasedOn = on;
                        }
                        if (hand.Time - offSince >= ReleaseSeconds)
                            happened = pressed >= 0 && releasedOn == pressed ? RayEvent.Clicked : RayEvent.Released;
                    }
                }
                if (happened != RayEvent.None)
                {
                    endedOn = pressed;
                    owner = null;
                    pressed = -1;
                }
            }
            else if (Presses(right, 0))
                Press(HandSide.Right, onRight, out happened);
            else if (Presses(left, 1))
                Press(HandSide.Left, onLeft, out happened);

            wasPinching[0] = right.Pinching;
            wasPinching[1] = left.Pinching;
            if (owner is HandSide holding) aims[(int)holding].Held = true;

            var hovered = owner == HandSide.Right ? onRight : owner == HandSide.Left ? onLeft : onRight >= 0 ? onRight : onLeft;
            return new RayReading(happened, owner, hovered, happened is RayEvent.None or RayEvent.Pressed ? pressed : endedOn);
        }

        bool Presses(in HandFrame hand, int index) => hand.Pointing && hand.Pinching && !wasPinching[index];

        void Press(HandSide side, int on, out RayEvent happened)
        {
            owner = side;
            pressed = on;
            offSince = lostSince = -1f;
            happened = RayEvent.Pressed;
        }

        int Point(int index, in HandFrame hand, RayTargets targets)
        {
            var aim = new RayAim { Shown = hand.Pointing, Pinch = hand.PinchStrength };
            var on = -1;
            if (hand.Pointing)
            {
                aim.Ray = hand.Pointer;
                on = targets(aim.Ray, out var distance);
                aim.OnTarget = on >= 0;
                aim.OnPage = distance > 0f;
                aim.Distance = aim.OnPage ? distance : FreeLength;
            }
            aims[index] = aim;
            return on;
        }
    }
}
