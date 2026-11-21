using Edith.Snapshots;
using NUnit.Framework;
using UnityEngine;

namespace Edith.Tests
{
    public class SightBoxWideningTests
    {
        static SightBox Box(float width, float height = 0.2f) =>
            new SightBox(Pose.identity, new Rect(0.1f - width * 0.5f, -height * 0.5f, width, height), 0.5f);

        [Test]
        public void A_skinny_box_widens_about_its_centre_to_the_minimum_and_keeps_its_height()
        {
            var box = Box(0.08f);
            var shown = box.WidenedTo(CardView.MinPhotoTangent);
            Assert.AreEqual(CardView.MinPhotoTangent, shown.Tangent.width, 1e-6f);
            Assert.AreEqual(box.Tangent.center.x, shown.Tangent.center.x, 1e-6f);
            Assert.AreEqual(box.Tangent.yMin, shown.Tangent.yMin, 1e-6f);
            Assert.AreEqual(box.Tangent.height, shown.Tangent.height, 1e-6f);
            Assert.AreEqual(box.Depth, shown.Depth);
        }

        [Test]
        public void A_box_already_wide_enough_is_left_as_it_is()
        {
            var box = Box(0.4f);
            Assert.AreEqual(box.Tangent, box.WidenedTo(CardView.MinPhotoTangent).Tangent);
        }

        [Test]
        public void The_minimum_is_about_fifteen_degrees_wide()
        {
            Assert.AreEqual(14.5f, 2f * Mathf.Atan(CardView.MinPhotoTangent * 0.5f) * Mathf.Rad2Deg, 0.5f);
        }
    }
}
