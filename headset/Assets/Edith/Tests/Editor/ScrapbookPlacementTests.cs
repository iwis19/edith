using Edith.Scrapbook;
using NUnit.Framework;
using UnityEngine;

namespace Edith.Tests
{
    public class ScrapbookPlacementTests
    {
        const float MinMeters = 0.6f;

        static float Elevation(Vector3 at) => Mathf.Atan2(-at.y, at.z) * Mathf.Rad2Deg;

        [Test]
        public void The_middle_card_photo_sits_just_above_eye_level()
        {
            Assert.AreEqual(5f, -Elevation(ScrapbookPlacement.CardCentre), 0.01f);
            Assert.AreEqual(0.70f, ScrapbookPlacement.CardCentre.z, 1e-6f);
            Assert.AreEqual(0.25f, DeckLayout.CardWidth, 1e-6f);
        }

        [Test]
        public void The_cards_look_at_least_three_quarters_as_wide_as_they_did_nearer()
        {
            var before = 0.20f / 0.45f;
            var now = DeckLayout.CardWidth / ScrapbookPlacement.CardCentre.z;
            Assert.That(now, Is.GreaterThanOrEqualTo(before * 0.75f));
        }

        [Test]
        public void Every_card_is_far_enough_from_the_eye()
        {
            for (var offset = -3f; offset <= 3f; offset += 0.25f)
            {
                var pose = DeckLayout.Pose(offset);
                var half = pose.Rotation * Vector3.right * (DeckLayout.CardWidth * pose.Scale * 0.5f);
                Assert.That((pose.Position - half).magnitude, Is.GreaterThanOrEqualTo(MinMeters), $"left edge of the card at {offset}");
                Assert.That((pose.Position + half).magnitude, Is.GreaterThanOrEqualTo(MinMeters), $"right edge of the card at {offset}");
            }
        }

        [Test]
        public void The_side_cards_fan_out_with_the_card_size()
        {
            Assert.AreEqual(0.20f, DeckLayout.Pose(1f).Position.x, 1e-5f);
            Assert.AreEqual(0.2875f, DeckLayout.Pose(2f).Position.x, 1e-5f);
        }

        // The fact under the photo hangs about this far below the photo's bottom edge, and the chip row reaches about
        // this far above and below its centre.
        const float TextDropDegrees = 6.5f, ChipHalfDegrees = 1.5f;

        static float PhotoBottom => Elevation(ScrapbookPlacement.CardCentre - new Vector3(0f, DeckLayout.CardHeight * 0.5f, 0f));

        [Test]
        public void The_chip_row_sits_under_the_card_text_without_touching_it()
        {
            Assert.AreEqual(13f, Elevation(ScrapbookPlacement.ChipsCentre), 0.01f);
            Assert.That(ScrapbookPlacement.ChipsCentre.magnitude, Is.GreaterThanOrEqualTo(MinMeters));
            Assert.That(PhotoBottom + TextDropDegrees, Is.LessThan(Elevation(ScrapbookPlacement.ChipsCentre) - ChipHalfDegrees));
        }

        [Test]
        public void Nothing_is_centred_more_than_26_degrees_below_the_eye()
        {
            Assert.That(Elevation(ScrapbookPlacement.CardCentre), Is.LessThanOrEqualTo(26f));
            Assert.That(Elevation(ScrapbookPlacement.ChipsCentre), Is.LessThanOrEqualTo(26f));
        }
    }
}
