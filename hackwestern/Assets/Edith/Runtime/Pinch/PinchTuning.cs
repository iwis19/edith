using System;

namespace Edith.Pinch
{
    /// Pinch-and-drag thresholds. Distances are metres; tangents are unitless (x/z).
    [Serializable]
    public sealed class PinchTuning
    {
        /// A shorter pinch-and-release is a tap.
        public float minDragMeters = 0.05f;
        /// The pinch flag must stay off this long to count as letting go; fast drags flicker it off for a frame or two.
        public float releaseSeconds = 0.08f;
        /// Hand tracking can drop the pinch for up to about 0.35 s mid-drag and resume within 8 cm, while a real
        /// release followed by a new pinch starts 28 cm or more away. A pinch within these limits continues the drag.
        public float rejoinSeconds = 0.40f;
        public float rejoinMeters = 0.08f;
        /// A lost hand is tolerated this long mid-drag.
        public float lostSeconds = 0.20f;
        /// The released box ends where the pinch was this long before letting go, because the arm drops and the
        /// pinch point slides as the fingers open while the pinch still reads as held.
        public float releaseLookbackSeconds = 0.10f;
        /// Pinch points nearer the eye are ignored.
        public float minDepthMeters = 0.15f;
        /// Smallest box side, about 3.4 degrees, and the widest aspect ratio, both checked on release.
        public float minTangentSize = 0.06f;
        public float maxAspect = 6f;
        /// Above this dot(palm normal, toward eye) the palm faces the user, which is Horizon OS's system gesture.
        public float palmFacingMax = 0.5f;
    }
}
