using System;
using Edith.Hands;
using UnityEngine;

namespace Edith.Scrapbook
{
    /// Wrist button thresholds. Distances are metres.
    [Serializable]
    public sealed class WristTuning
    {
        /// dot(out of the left palm, the line from the palm to the eye) above this means the palm faces the user:
        /// within 45 degrees.
        public float palmFacingMin = 0.7071f;
        /// The palm must face the user this long before the button shows.
        public float showSeconds = 0.25f;
        /// A palm turned away, or a hand lost, for no longer than this keeps the button. The right hand reaching
        /// over the left one hides part of it, and tracking drops for a moment.
        public float graceSeconds = 0.3f;
        /// The button floats this far above the wrist, along the eye's up.
        public float aboveWristMeters = 0.05f;
        public float buttonRadiusMeters = 0.0125f;
        /// The right index tip presses the button within this distance of its centre.
        public float pressMeters = 0.018f;
        /// After a press, and each time the button shows, the fingertip must be seen further than this from the
        /// button before it can press.
        public float rearmMeters = 0.03f;
    }

    public readonly struct WristReading
    {
        public readonly bool Shown;
        /// The centre of the button. Meaningful while Shown.
        public readonly Vector3 Position;
        /// True on the one step the fingertip presses the button.
        public readonly bool Pressed;

        public WristReading(bool shown, Vector3 position, bool pressed)
        {
            Shown = shown;
            Position = position;
            Pressed = pressed;
        }
    }

    /// A button over the left wrist, shown while the left palm faces the user and pressed by touching it with the
    /// right index fingertip. No pinch takes part: a pinch with the palm facing the user is Horizon OS's own
    /// gesture. Deterministic in HandFrame.Time.
    public sealed class WristButton
    {
        readonly WristTuning tuning;
        float facingSince = -1f, lastFacing = float.NegativeInfinity;
        bool shown, armed;
        Vector3 position;

        public WristButton(WristTuning tuning) => this.tuning = tuning;

        public WristReading Step(in HandFrame left, in HandFrame right)
        {
            var time = left.Time;
            if (Facing(left))
            {
                if (facingSince < 0f) facingSince = time;
                lastFacing = time;
                position = left.Wrist + left.EyeRotation * Vector3.up * tuning.aboveWristMeters;
            }
            else if (time - lastFacing > tuning.graceSeconds)
            {
                facingSince = -1f;
            }

            var show = facingSince >= 0f && time - facingSince >= tuning.showSeconds - 1e-4f;
            if (show && !shown) armed = false;
            shown = show;
            if (!shown || !right.Tracked) return new WristReading(shown, position, false);

            var away = Vector3.Distance(right.IndexTip, position);
            if (away > tuning.rearmMeters) armed = true;
            var pressed = armed && away <= tuning.pressMeters;
            if (pressed) armed = false;
            return new WristReading(true, position, pressed);
        }

        bool Facing(in HandFrame left)
        {
            if (!left.Tracked) return false;
            // HandFrame.PalmNormal is flipped for a right hand, so on the left it points into the palm.
            var toEye = (left.EyePosition - left.MiddleKnuckle).normalized;
            return Vector3.Dot(-left.PalmNormal, toEye) > tuning.palmFacingMin;
        }
    }
}
