using System;

namespace Edith.Grab
{
    /// Fist-grab thresholds. Distances are metres. Curl is the mean fingertip-to-wrist over knuckle-to-wrist distance
    /// of the index, middle and ring fingers: about 1.9 with the hand open and under 1.1 in a fist.
    [Serializable]
    public sealed class GrabTuning
    {
        /// Below this the hand is closed. Fists cross it at 1.01 to 1.15.
        public float closedCurl = 1.15f;
        /// Above this the hand is open again. Opening hands cross it at 1.40 to 1.79; between the two the state holds.
        public float openCurl = 1.45f;
        /// A grab needs the hand above openCurl this recently. A hand relaxing after a pinch sits at 1.14 to 1.22,
        /// so it closes without ever having been open.
        public float openWithinSeconds = 1f;
        /// The grip point, as a fraction of the way from the wrist to the middle knuckle.
        public float gripAlongPalm = 0.75f;
        /// The grip touches a card within this distance beside its rect and within touchDepthMeters of its plane.
        public float touchMeters = 0.06f;
        public float touchDepthMeters = 0.15f;
        /// The pocket is this far straight below the eye. A pull counts by how much nearer the grip gets to it.
        public float pocketBelowEyeMeters = 0.30f;
        /// The pull needed is this fraction of the reach at the grab, kept between the two limits.
        public float pullFraction = 0.35f;
        public float pullMinMeters = 0.08f;
        public float pullMaxMeters = 0.18f;
        /// A hand lost this long while it holds a card lets it go.
        public float graceSeconds = 0.30f;
    }
}
