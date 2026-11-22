using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Edith.Ask;
using Edith.Grab;
using Edith.Scan;
using Edith.Snapshots;
using NUnit.Framework;
using UnityEngine;
using static Edith.Tests.SnapshotShelfTests;
using Object = UnityEngine.Object;

namespace Edith.Tests
{
    public class HandActionTests
    {
        [Test]
        public void The_hand_action_calls_are_read_with_their_ids_and_names()
        {
            var message = LiveMessage.Parse(
                "{\"toolCall\":{\"functionCalls\":[" +
                "{\"id\":\"a\",\"name\":\"scan\",\"args\":{}}," +
                "{\"id\":\"b\",\"name\":\"collect_snapshot\"}," +
                "{\"id\":\"c\",\"name\":\"clear_snapshots\",\"args\":{}}]}}");

            Assert.AreEqual(3, message.ToolCalls.Count);
            Assert.AreEqual(("a", ToolCall.Scan), Read(message.ToolCalls[0]));
            Assert.AreEqual(("b", ToolCall.CollectSnapshot), Read(message.ToolCalls[1]));
            Assert.AreEqual(("c", ToolCall.ClearSnapshots), Read(message.ToolCalls[2]));
            Assert.AreEqual(("scan", "collect_snapshot", "clear_snapshots"),
                (ToolCall.Scan, ToolCall.CollectSnapshot, ToolCall.ClearSnapshots));
        }

        static (string, string) Read(ToolCall call) => (call.Id, call.Name);

        [Test]
        public void A_scan_that_pinned_answers_each_pins_name_and_hook_in_order()
        {
            var outcome = ScanOutcome.Pinned(new[]
            {
                new ScanPin(Vector3.forward, "the \"red\" kettle", "Boils in a minute.", false),
                new ScanPin(Vector3.up, "a fern", "Older than dinosaurs.", true),
            });

            Assert.AreEqual(
                "{\"pins\":[{\"name\":\"the \\\"red\\\" kettle\",\"hook\":\"Boils in a minute.\"}," +
                "{\"name\":\"a fern\",\"hook\":\"Older than dinosaurs.\"}]}",
                HandReplies.Scanned(outcome));
        }

        [Test]
        public void A_scan_with_no_pins_answers_an_error_that_says_why()
        {
            Assert.AreEqual("{\"error\":{\"code\":\"scan_busy\",\"message\":\"A scan is already running.\"}}",
                HandReplies.Scanned(ScanOutcome.Busy));
            Assert.AreEqual("{\"error\":{\"code\":\"nothing_found\",\"message\":\"Nothing stands out here.\"}}",
                HandReplies.Scanned(ScanOutcome.NothingFound));
            Assert.AreEqual("{\"error\":{\"code\":\"scan_failed\",\"message\":\"The camera isn't ready.\"}}",
                HandReplies.Scanned(ScanOutcome.Failed("The camera isn't ready.")));
            StringAssert.StartsWith("{\"error\":{\"code\":\"nothing_found\"", HandReplies.Scanned(ScanOutcome.Pinned(new ScanPin[0])));
        }

        [Test]
        public void Collect_and_clear_answer_what_they_did()
        {
            Assert.AreEqual("{\"collected\":\"a brass \\\"owl\\\"\"}", HandReplies.Collected("a brass \"owl\""));
            Assert.AreEqual("{\"cleared\":3}", HandReplies.Cleared(3));
            Assert.AreEqual("{\"cleared\":0}", HandReplies.Cleared(0));
            StringAssert.StartsWith("{\"error\":{\"code\":\"nothing_in_view\",", HandReplies.NothingInView);
            StringAssert.StartsWith("{\"error\":{\"code\":\"still_looking\",", HandReplies.StillLooking);
            StringAssert.StartsWith("{\"error\":{\"code\":\"scan_timeout\",", HandReplies.ScanTimedOut);
            Assert.AreEqual("{\"error\":{\"code\":\"unknown_function\",\"message\":\"No \\\"such\\\" thing.\"}}",
                HandReplies.Error("unknown_function", "No \"such\" thing."));
        }

        static Vector3 At(float yaw) => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * 0.5f;

        static int Pick(params Vector3[] centres) =>
            AskGeometry.NearestToGaze(Vector3.zero, Vector3.forward, new List<Vector3>(centres), out _,
                AskGeometry.CollectGazeDegrees);

        [Test]
        public void Save_this_picks_the_snapshot_nearest_the_line_of_sight_within_a_small_angle()
        {
            Assert.AreEqual(1, Pick(At(-8f), At(4f), At(30f)));
            Assert.AreEqual(0, Pick(At(9.5f)));
            Assert.AreEqual(-1, Pick(At(11f)), "just outside the limit");
            Assert.AreEqual(-1, Pick(At(20f), At(-15f)), "near enough for a question's hint, not to collect");
            Assert.AreEqual(-1, Pick());
            Assert.Less(AskGeometry.CollectGazeDegrees, AskGeometry.MaxGazeDegrees);
            Assert.AreEqual(0, AskGeometry.NearestToGaze(Vector3.zero, Vector3.forward, new List<Vector3> { At(20f) }, out _),
                "a question's hint still reaches 25 degrees");
        }

        [Test]
        public void A_scan_that_never_ends_answers_the_timeout()
        {
            var never = new TaskCompletionSource<string>();
            var reply = HandReplies.WithTimeout(never.Task, TimeSpan.FromMilliseconds(50), HandReplies.ScanTimedOut);

            Assert.IsTrue(reply.Wait(5000), "the reply ends without the scan");
            Assert.AreEqual(HandReplies.ScanTimedOut, reply.Result);
        }

        [Test]
        public void A_scan_that_ends_in_time_answers_its_own_result()
        {
            var scan = new TaskCompletionSource<string>();
            var reply = HandReplies.WithTimeout(scan.Task, TimeSpan.FromSeconds(30), HandReplies.ScanTimedOut);
            scan.SetResult("{\"pins\":[]}");

            Assert.IsTrue(reply.Wait(5000));
            Assert.AreEqual("{\"pins\":[]}", reply.Result);
        }

        [Test]
        public void Clearing_makes_every_snapshot_leave_and_counts_them()
        {
            var parent = new GameObject("Room");
            try
            {
                var room = new SnapshotRoom(parent.transform);
                Assert.AreEqual(0, room.Clear());
                var placed = new[] { room.Place(Box(-0.5f)), room.Place(Box(0f)), room.Place(Box(0.5f)) };

                Assert.AreEqual(3, room.Clear());

                Assert.AreEqual(0, room.Snapshots.Count);
                foreach (var snapshot in placed) Assert.IsTrue(snapshot.Leaving);
                Assert.AreEqual(0, room.Clear());
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void Collecting_pockets_the_snapshot_as_the_pull_does_and_only_once()
        {
            var parent = new GameObject("Room");
            try
            {
                var room = new SnapshotRoom(parent.transform);
                var kept = room.Place(Box(-0.5f));
                var snapshot = room.Place(Box(0.5f));
                var pocketed = new List<Snapshot>();
                room.Pocketed += pocketed.Add;

                Assert.IsTrue(SnapshotGrab.Collect(room, snapshot, Vector3.down));
                Assert.IsFalse(SnapshotGrab.Collect(room, snapshot, Vector3.down));

                CollectionAssert.AreEqual(new[] { snapshot }, pocketed);
                Assert.IsTrue(snapshot.Leaving);
                Assert.AreSame(kept, room.Snapshots[0].item);
                Assert.AreEqual(1, room.Snapshots.Count);
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }
    }
}
