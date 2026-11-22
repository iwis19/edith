using System.Collections.Generic;
using Edith.Hands;
using Edith.Navigation;
using NUnit.Framework;
using UnityEngine;

namespace Edith.Tests
{
    public sealed class FingerGunTests
    {
        static readonly FingerGunTuning Tuning = new FingerGunTuning();
        static readonly Vector3 Wrist = new Vector3(0f, -0.3f, 0.3f);

        /// A hand in front of an eye at the origin that faces +Z, its index pointing +Z before `turn` about the wrist.
        static HandFrame Hand(Quaternion turn, float time = 0f, HandSide side = HandSide.Right, bool thumbTucked = false,
            bool middleOut = false, bool visible = true)
        {
            Vector3 At(float x, float y, float z) => Wrist + turn * new Vector3(x, y, z);
            return new HandFrame
            {
                Side = side, Time = time, EyePosition = Vector3.zero, EyeRotation = Quaternion.identity,
                Visible = visible, Tracked = visible,
                Wrist = Wrist,
                IndexKnuckle = At(0f, 0f, 0.09f), IndexTip = At(0f, 0f, 0.17f),
                MiddleKnuckle = At(0f, -0.02f, 0.085f),
                MiddleTip = middleOut ? At(0f, -0.02f, 0.165f) : At(0f, -0.04f, 0.06f),
                RingKnuckle = At(0f, -0.04f, 0.08f), RingTip = At(0f, -0.06f, 0.055f),
                LittleKnuckle = At(0f, -0.06f, 0.075f),
                ThumbTip = thumbTucked ? At(0f, 0.01f, 0.08f) : At(0f, 0.05f, 0.06f),
            };
        }

        static readonly Quaternion Ahead = Quaternion.identity;
        static readonly Quaternion Up = Quaternion.Euler(-90f, 0f, 0f);

        [Test]
        public void AGunPointingAheadOrUpIsRead()
        {
            Assert.That(FingerGuns.Classify(Hand(Ahead), Tuning), Is.EqualTo(GunPose.Forward));
            Assert.That(FingerGuns.Classify(Hand(Up), Tuning), Is.EqualTo(GunPose.Up));
            // Slightly off either way still counts.
            Assert.That(FingerGuns.Classify(Hand(Quaternion.Euler(-15f, 20f, 0f)), Tuning), Is.EqualTo(GunPose.Forward));
            Assert.That(FingerGuns.Classify(Hand(Quaternion.Euler(-70f, 0f, 0f)), Tuning), Is.EqualTo(GunPose.Up));
        }

        [Test]
        public void OtherHandShapesAndDirectionsAreNotGuns()
        {
            Assert.That(FingerGuns.Classify(Hand(Quaternion.Euler(0f, 90f, 0f)), Tuning), Is.EqualTo(GunPose.None));
            Assert.That(FingerGuns.Classify(Hand(Quaternion.Euler(90f, 0f, 0f)), Tuning), Is.EqualTo(GunPose.None));
            Assert.That(FingerGuns.Classify(Hand(Ahead, middleOut: true), Tuning), Is.EqualTo(GunPose.None));
            Assert.That(FingerGuns.Classify(Hand(Ahead, thumbTucked: true), Tuning), Is.EqualTo(GunPose.None));
            Assert.That(FingerGuns.Classify(Hand(Ahead, visible: false), Tuning), Is.EqualTo(GunPose.None));
        }

        /// Steps both hands from `from` to `to` seconds in 20 ms frames and returns the gestures that fired.
        static List<(float, NavGesture)> Run(FingerGunPair pair, float from, float to, Quaternion right, Quaternion? left)
        {
            var fired = new List<(float, NavGesture)>();
            for (var t = from; t < to - 1e-4f; t += 0.02f)
            {
                var l = left is { } turn ? Hand(turn, t, HandSide.Left) : Hand(Ahead, t, HandSide.Left, visible: false);
                var g = pair.Step(Hand(right, t), l);
                if (g != NavGesture.None) fired.Add((t, g));
            }
            return fired;
        }

        [Test]
        public void BothHandsMustHoldThePoseAndItFiresOnce()
        {
            var pair = new FingerGunPair(Tuning);
            Assert.That(Run(pair, 0f, 1f, Ahead, null), Is.Empty);
            var fired = Run(pair, 1f, 3f, Ahead, Ahead);
            Assert.That(fired, Has.Count.EqualTo(1));
            Assert.That(fired[0].Item2, Is.EqualTo(NavGesture.Start));
            Assert.That(fired[0].Item1, Is.EqualTo(1f + Tuning.HoldSeconds).Within(0.03f));
        }

        [Test]
        public void ABriefTrackingDropDoesNotFireAgain()
        {
            var pair = new FingerGunPair(Tuning);
            Assert.That(Run(pair, 0f, 2f, Ahead, Ahead), Has.Count.EqualTo(1));
            Assert.That(Run(pair, 2f, 2.1f, Ahead, null), Is.Empty);
            Assert.That(Run(pair, 2.1f, 4f, Ahead, Ahead), Is.Empty);
        }

        [Test]
        public void TurningTheGunsUpAfterStartingStops()
        {
            var pair = new FingerGunPair(Tuning);
            Assert.That(Run(pair, 0f, 0.5f, Ahead, Ahead)[0].Item2, Is.EqualTo(NavGesture.Start));
            var fired = Run(pair, 0.5f, 3f, Up, Up);
            Assert.That(fired, Has.Count.EqualTo(1));
            Assert.That(fired[0].Item2, Is.EqualTo(NavGesture.Stop));
            // Not before the cooldown since the start.
            Assert.That(fired[0].Item1, Is.GreaterThanOrEqualTo(0.3f + Tuning.CooldownSeconds - 0.03f));
        }

        [Test]
        public void LoweringTheHandsAfterStoppingDoesNotStartAgain()
        {
            var pair = new FingerGunPair(Tuning);
            Assert.That(Run(pair, 0f, 0.5f, Up, Up)[0].Item2, Is.EqualTo(NavGesture.Stop));
            // On the way down the guns point ahead for a while, well past the cooldown.
            Assert.That(Run(pair, 0.5f, 1.2f, Up, Up), Is.Empty);
            Assert.That(Run(pair, 1.2f, 4f, Ahead, Ahead), Is.Empty);
        }

        [Test]
        public void DroppingTheGunsBetweenGesturesLetsTheNextOneStart()
        {
            var pair = new FingerGunPair(Tuning);
            Assert.That(Run(pair, 0f, 0.5f, Up, Up)[0].Item2, Is.EqualTo(NavGesture.Stop));
            Assert.That(Run(pair, 0.5f, 1f, Quaternion.Euler(0f, 90f, 0f), Quaternion.Euler(0f, 90f, 0f)), Is.Empty);
            var fired = Run(pair, 2f, 3f, Ahead, Ahead);
            Assert.That(fired, Has.Count.EqualTo(1));
            Assert.That(fired[0].Item2, Is.EqualTo(NavGesture.Start));
        }

        [Test]
        public void OneHandFlickeringWhileHoldingDoesNotStartTwice()
        {
            var pair = new FingerGunPair(Tuning);
            Assert.That(Run(pair, 0f, 1f, Ahead, Ahead), Has.Count.EqualTo(1));
            Assert.That(Run(pair, 1f, 1.4f, Ahead, null), Is.Empty);
            Assert.That(Run(pair, 1.4f, 4f, Ahead, Ahead), Is.Empty);
        }

        [Test]
        public void HandsPointingDifferentWaysDoNothing()
        {
            Assert.That(Run(new FingerGunPair(Tuning), 0f, 2f, Ahead, Up), Is.Empty);
        }
    }
}
