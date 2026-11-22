using System;

namespace Edith.Scan
{
    /// Scan thresholds. Distances are metres.
    [Serializable]
    public sealed class ScanTuning
    {
        /// GrabTracker.Curl above this is an open hand, the same value that opens a fist in GrabTuning.
        public float openCurl = 1.45f;
        /// The palm point, as a fraction of the way from the wrist to the middle knuckle.
        public float palmAlong = 0.75f;
        /// dot(palm normal, the line from the eye to the palm) above this means the palm faces away from the user.
        /// Measured along that line, not the eye's forward: hands held out at chest height sit well below the gaze,
        /// where palms read 0.6 to 0.9 this way but only 0.0 to 0.3 against the forward axis.
        public float palmAwayMin = 0.5f;
        /// dot(palm normal, straight down) above this means the palm faces the floor. Hands typing or resting over
        /// a desk sit below the eye, so they pass palmAwayMin; their palms read 0.92 to 0.99 this way.
        public float palmDownMax = 0.7f;
        /// A palm past palmDownMax still counts when dot(palm normal, where the head points) is above this: the user
        /// looks down about 45 degrees or more and holds the palms out the same way. Hands on a keyboard the user
        /// glances at read about 0.5 to 0.6.
        public float palmAlongLookMin = 0.7f;
        /// Each palm must be at least this far in front of the eye along its forward axis. Palms-out holds measure
        /// 0.13 to 0.19 m.
        public float minAheadMeters = 0.08f;
        /// Each palm at most this far below the eye. Held out at the chest they measure 0.35 to 0.41 m; a hand
        /// hanging relaxed at the side, palm turned away, measures 0.57 m and must not scan.
        public float maxBelowMeters = 0.48f;
        public float holdSeconds = 0.5f;
        /// After a scan fires, the next one waits this long, and the hands must also leave the pose first.
        public float cooldownSeconds = 2f;
        /// A hand lost, or out of the pose, for no longer than this does not restart the hold.
        public float graceSeconds = 0.15f;
        /// Looser limits that only decide when the pose is close enough to log.
        public float nearPalmAway = 0.2f;
        public float nearCurl = 1.25f;

        /// A pinch opens the pin nearest its eye line within this angle. Wide, because a pin is a point that can be
        /// metres away.
        public float pinTapDegrees = 7f;
        /// How far depth is trusted for a pin, and the distance assumed when it has no answer.
        public float maxPinMeters = 6f;
        public float assumedPinMeters = 1.5f;
        /// Depth nearer than this is a hand still raised from the pose, not the object.
        public float minPinMeters = 0.35f;
        /// Depth along a captured frame's sight lines is read only while the camera is within this angle and distance
        /// of where it was at capture. Depth covers only what the headset sees now: once the head has turned or
        /// moved, a sight line's near part can fall out of view or behind something, and the first surface depth
        /// still sees along it is something farther away.
        public float depthPoseDegrees = 5f;
        public float depthPoseMeters = 0.1f;
        /// A pick whose sight line meets nothing within maxPinMeters is beyond depth's reach, such as a building
        /// down the street. Its pin only shows the way: it sits this far out, where it barely slides as the user
        /// walks, and fades once they come within farFadeMeters of it, where the guessed spot would be visibly wrong.
        public float farPinMeters = 25f;
        public float farFadeMeters = 10f;
        /// An opened pin's card hangs at the nearer of this depth and this share of the way to the pin.
        public float cardDepthMeters = 0.45f;
        public float cardDepthShare = 0.8f;
        /// Looking at a pin opens it: the pin must stay within this angle of where the head points for
        /// gazeOpenSeconds while the head turns no faster than gazeStillDegreesPerSecond. The card hangs on the pin's
        /// object. The headset does not track the eyes and shows no cursor, so this is wide.
        public float gazeOpenDegrees = 8f;
        public float gazeOpenSeconds = 0.8f;
        public float gazeStillDegreesPerSecond = 20f;
    }
}
