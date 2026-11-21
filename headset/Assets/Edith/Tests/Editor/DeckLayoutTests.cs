using Edith.Scrapbook;
using NUnit.Framework;
using UnityEngine;

namespace Edith.Tests
{
    public class DeckLayoutTests
    {
        [Test]
        public void The_middle_card_is_full_size_square_on_and_readable()
        {
            var pose = DeckLayout.Pose(0f);
            Assert.AreEqual(0f, pose.Position.x);
            Assert.AreEqual(0f, pose.Yaw);
            Assert.AreEqual(1f, pose.Scale);
            Assert.AreEqual(1f, pose.Alpha);
            Assert.AreEqual(1f, pose.TextAlpha);
        }

        [Test]
        public void The_middle_card_hangs_out_of_the_way_of_the_hands_at_eye_level()
        {
            Assert.AreEqual(ScrapbookPlacement.CardCentre.y, DeckLayout.Pose(0f).Position.y, 1e-6f);
            Assert.AreEqual(0.70f, DeckLayout.Pose(0f).Position.z, 1e-6f);
        }

        [Test]
        public void Side_cards_are_smaller_further_back_and_turned_toward_the_middle()
        {
            var middle = DeckLayout.Pose(0f);
            var right = DeckLayout.Pose(1f);
            Assert.That(right.Position.x, Is.GreaterThan(DeckLayout.CardWidth * 0.5f), "clear of the middle card's edge");
            Assert.That(right.Position.z, Is.GreaterThan(middle.Position.z));
            Assert.That(right.Scale, Is.LessThan(1f));
            // The face of a card looks back along its forward axis, so a face turned toward the middle looks to -x.
            Assert.That((right.Rotation * Vector3.back).x, Is.LessThan(-0.5f));
            Assert.That((DeckLayout.Pose(-1f).Rotation * Vector3.back).x, Is.GreaterThan(0.5f));
        }

        [Test]
        public void An_older_card_sits_to_the_right()
        {
            Assert.That(DeckLayout.Pose(1f).Position.x, Is.GreaterThan(0f));
            Assert.That(DeckLayout.Pose(-1f).Position.x, Is.LessThan(0f));
        }

        [Test]
        public void Each_card_further_out_sits_further_to_the_side()
        {
            for (var away = 0f; away < 2.95f; away += 0.1f)
                Assert.That(DeckLayout.Pose(away + 0.1f).Position.x, Is.GreaterThan(DeckLayout.Pose(away).Position.x), $"at {away}");
        }

        [Test]
        public void The_two_sides_mirror_each_other()
        {
            foreach (var away in new[] { 0.25f, 0.5f, 1f, 1.7f, 2f, 2.6f, 3f })
            {
                var right = DeckLayout.Pose(away);
                var left = DeckLayout.Pose(-away);
                Assert.AreEqual(right.Position.x, -left.Position.x, 1e-6f);
                Assert.AreEqual(right.Position.y, left.Position.y, 1e-6f);
                Assert.AreEqual(right.Position.z, left.Position.z, 1e-6f);
                Assert.AreEqual(right.Yaw, -left.Yaw, 1e-4f);
                Assert.AreEqual(right.Scale, left.Scale, 1e-6f);
                Assert.AreEqual(right.Alpha, left.Alpha, 1e-6f);
                Assert.AreEqual(right.TextAlpha, left.TextAlpha, 1e-6f);
            }
        }

        [Test]
        public void Only_the_middle_card_shows_its_text()
        {
            Assert.That(DeckLayout.Pose(0.25f).TextAlpha, Is.GreaterThan(0f).And.LessThan(1f));
            Assert.AreEqual(0f, DeckLayout.Pose(0.5f).TextAlpha);
            Assert.AreEqual(0f, DeckLayout.Pose(1f).TextAlpha);
            Assert.AreEqual(0f, DeckLayout.Pose(-2f).TextAlpha);
        }

        [Test]
        public void Two_cards_a_side_are_solid_and_the_third_has_faded_out()
        {
            Assert.AreEqual(1f, DeckLayout.Pose(1f).Alpha);
            Assert.AreEqual(1f, DeckLayout.Pose(2f).Alpha);
            Assert.That(DeckLayout.Pose(2.5f).Alpha, Is.GreaterThan(0f).And.LessThan(1f));
            Assert.AreEqual(0f, DeckLayout.Pose(3f).Alpha);
            Assert.AreEqual(0f, DeckLayout.Pose(-7f).Alpha);
        }

        [Test]
        public void A_card_moves_smoothly_as_the_deck_scrolls()
        {
            const float step = 0.01f;
            for (var offset = -3.5f; offset < 3.5f; offset += step)
            {
                var a = DeckLayout.Pose(offset);
                var b = DeckLayout.Pose(offset + step);
                Assert.That(Vector3.Distance(a.Position, b.Position), Is.LessThan(0.005f), $"position at {offset}");
                Assert.That(Mathf.Abs(a.Yaw - b.Yaw), Is.LessThan(1f), $"yaw at {offset}");
                Assert.That(Mathf.Abs(a.Scale - b.Scale), Is.LessThan(0.01f), $"scale at {offset}");
                Assert.That(Mathf.Abs(a.Alpha - b.Alpha), Is.LessThan(0.03f), $"alpha at {offset}");
                Assert.That(Mathf.Abs(a.TextAlpha - b.TextAlpha), Is.LessThan(0.03f), $"text at {offset}");
            }
        }

        [Test]
        public void The_deck_faces_the_way_the_eye_faces_and_stays_level()
        {
            var eye = new Pose(new Vector3(1f, 1.6f, -2f), Quaternion.Euler(35f, 70f, 20f));
            var frame = DeckLayout.Frame(eye);
            Assert.AreEqual(eye.position, frame.position);
            Assert.AreEqual(0f, Vector3.Angle(frame.rotation * Vector3.up, Vector3.up), 0.01f);
            Assert.AreEqual(70f, frame.rotation.eulerAngles.y, 0.01f);
        }

        [Test]
        public void An_eye_looking_straight_down_still_gives_a_level_deck_ahead_of_the_head()
        {
            var frame = DeckLayout.Frame(new Pose(Vector3.zero, Quaternion.Euler(90f, 40f, 0f)));
            Assert.AreEqual(0f, Vector3.Angle(frame.rotation * Vector3.up, Vector3.up), 0.01f);
            Assert.AreEqual(40f, frame.rotation.eulerAngles.y, 0.5f);
        }

        [Test]
        public void A_pinch_in_line_with_a_panel_hits_it_from_any_depth()
        {
            var centre = new Vector3(0f, 0f, 0.5f);
            var size = new Vector2(0.2f, 0.1f);
            Assert.IsTrue(SightLine.Hits(Vector3.zero, new Vector3(0.03f, 0.01f, 0.25f), centre, Quaternion.identity, size));
            Assert.IsTrue(SightLine.Hits(Vector3.zero, new Vector3(0.09f, 0.04f, 0.5f), centre, Quaternion.identity, size));
            Assert.IsFalse(SightLine.Hits(Vector3.zero, new Vector3(0.06f, 0f, 0.25f), centre, Quaternion.identity, size), "past the right edge");
            Assert.IsFalse(SightLine.Hits(Vector3.zero, new Vector3(0f, 0.03f, 0.25f), centre, Quaternion.identity, size), "past the top edge");
        }

        [Test]
        public void A_pinch_just_off_a_panel_hits_only_within_the_margin()
        {
            var centre = new Vector3(0f, 0f, 0.5f);
            var size = new Vector2(0.2f, 0.1f);
            var through = new Vector3(0.103f, 0f, 0.5f);
            Assert.IsFalse(SightLine.Hits(Vector3.zero, through, centre, Quaternion.identity, size));
            Assert.IsTrue(SightLine.Hits(Vector3.zero, through, centre, Quaternion.identity, size, 0.005f));
        }

        [Test]
        public void A_turned_panel_is_hit_where_it_is_seen()
        {
            var pose = DeckLayout.Pose(1f);
            var size = new Vector2(0.14f, 0.1f);
            Assert.IsTrue(SightLine.Hits(Vector3.zero, pose.Position * 0.5f, pose.Position, pose.Rotation, size));
            var outerEdge = pose.Position + pose.Rotation * new Vector3(0.069f, 0f, 0f);
            var beyond = pose.Position + pose.Rotation * new Vector3(0.08f, 0f, 0f);
            Assert.IsTrue(SightLine.Hits(Vector3.zero, outerEdge * 0.6f, pose.Position, pose.Rotation, size));
            Assert.IsFalse(SightLine.Hits(Vector3.zero, beyond * 0.6f, pose.Position, pose.Rotation, size));
        }

        [Test]
        public void A_panel_behind_the_eye_is_never_hit()
        {
            Assert.IsFalse(SightLine.Hits(Vector3.zero, new Vector3(0f, 0f, 0.3f), new Vector3(0f, 0f, -0.5f), Quaternion.identity, Vector2.one));
        }
    }
}
