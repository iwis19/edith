using System.Collections.Generic;
using System.IO;
using System.Linq;
using Edith.Grab;
using Edith.Hands;
using Edith.Pairing;
using Edith.Pinch;
using Edith.Scan;
using Edith.Snapshots;
using NUnit.Framework;
using UnityEngine;

namespace Edith.Tests
{
    public class PairingGateTests
    {
        sealed class MemoryStore : IPairedStore
        {
            public bool Paired { get; set; }
        }

        static HandFrame OpenHandPinching(float time) => new HandFrame
        {
            Time = time,
            Tracked = true,
            Visible = true,
            Pinching = true,
            EyeRotation = Quaternion.identity,
        };

        [Test]
        public void AnUnpairedHeadsetIsLocked()
        {
            Assert.IsFalse(new PairingLock(new MemoryStore(), false).Open);
        }

        [Test]
        public void PairingOpensTheGateAndIsRememberedAtTheNextLaunch()
        {
            var store = new MemoryStore();
            var gate = new PairingLock(store, false);
            var opened = 0;
            gate.Changed += open => opened += open ? 1 : 0;
            gate.Paired = true;
            Assert.IsTrue(gate.Open);
            Assert.AreEqual(1, opened);
            Assert.IsTrue(new PairingLock(store, false).Open);
        }

        [Test]
        public void ALostPairingClosesTheGateAndIsRememberedAtTheNextLaunch()
        {
            var store = new MemoryStore { Paired = true };
            var gate = new PairingLock(store, false);
            gate.Paired = false;
            Assert.IsFalse(gate.Open);
            Assert.IsFalse(new PairingLock(store, false).Open);
        }

        [Test]
        public void TheEditorBypassKeepsTheGateOpenWithoutPairing()
        {
            var gate = new PairingLock(new MemoryStore(), true);
            Assert.IsTrue(gate.Open);
            Assert.IsFalse(gate.Paired);
        }

        [Test]
        public void AClosedGateHidesTheHandButKeepsItsClock()
        {
            var frame = OpenHandPinching(4.5f);
            var gated = frame.Gated(false);
            Assert.IsFalse(gated.Tracked || gated.Visible || gated.Pinching);
            Assert.AreEqual(4.5f, gated.Time);
            Assert.AreEqual(frame, frame.Gated(true));
        }

        [Test]
        public void APinchThroughAClosedGateNeverStarts()
        {
            var tracker = new PinchTracker(new PinchTuning());
            for (var i = 0; i < 120; i++)
            {
                var reading = tracker.Step(OpenHandPinching(i / 60f).Gated(false));
                Assert.AreEqual(PinchPhase.Idle, reading.Phase);
                Assert.AreEqual(PinchEvent.None, reading.Event);
            }
        }

        [Test]
        public void ScanPosesThroughAClosedGateNeverFire()
        {
            var tracker = new ScanPoseTracker(new ScanTuning());
            for (var i = 0; i < 300; i++)
            {
                var hand = OpenHandPinching(i / 60f).Gated(false);
                var pose = tracker.Step(hand, hand);
                Assert.IsFalse(pose.Fired);
                Assert.AreEqual(0f, pose.Progress);
            }
        }

        [Test]
        public void AGrabThroughAClosedGateNeverClaimsTheHand()
        {
            var tracker = new GrabTracker(new GrabTuning());
            var targets = new List<(int id, SightBox box)>();
            for (var i = 0; i < 120; i++)
                Assert.IsFalse(tracker.Step(OpenHandPinching(i / 60f).Gated(false), targets).Claimed);
        }

        // Every runtime script that reads OVRHand, the microgestures or the controllers, or builds a HandReader, must
        // check the gate, so a new gesture cannot skip it. HandReader itself is only built by the gated hand sources.
        [Test]
        public void EveryDirectHandOrGestureReaderChecksTheGate()
        {
            var runtime = Path.Combine(Application.dataPath, "Edith", "Runtime");
            string[] readers = { "OVRHand", "OVRMicrogestureEventSource", "OVRInput", "new HandReader" };
            var unguarded = Directory.GetFiles(runtime, "*.cs", SearchOption.AllDirectories)
                .Where(path => Path.GetFileName(path) != "HandReader.cs")
                .Select(path => (name: Path.GetFileName(path), text: File.ReadAllText(path)))
                .Where(file => readers.Any(reader => file.text.Contains(reader)) && !file.text.Contains("PairingGate.Open"))
                .Select(file => file.name)
                .ToArray();
            Assert.IsEmpty(unguarded, "These read hands or gestures without checking PairingGate.Open");
        }
    }
}
