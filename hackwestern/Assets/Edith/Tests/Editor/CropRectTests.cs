using Edith.Capture;
using NUnit.Framework;
using UnityEngine;

namespace Edith.Tests
{
    public class CropRectTests
    {
        static readonly Vector2Int Image = new Vector2Int(1280, 960);

        static RectInt? Crop(float x0, float y0, float x1, float y1) =>
            CropRect.FromViewport(new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1), Image);

        [Test]
        public void A_region_inside_the_image_becomes_its_pixel_bounds()
        {
            Assert.AreEqual(new RectInt(256, 192, 512, 384), Crop(0.2f, 0.2f, 0.6f, 0.6f));
        }

        [Test]
        public void The_bounds_of_a_slanted_region_cover_all_four_corners()
        {
            var crop = CropRect.FromViewport(
                new Vector2(0.2f, 0.2f), new Vector2(0.6f, 0.25f), new Vector2(0.65f, 0.7f), new Vector2(0.25f, 0.6f), Image);
            Assert.AreEqual(new RectInt(256, 192, 576, 480), crop);
        }

        [Test]
        public void A_region_that_spills_off_the_edge_is_clamped()
        {
            Assert.AreEqual(new RectInt(0, 192, 640, 384), Crop(-0.1f, 0.2f, 0.5f, 0.6f));
        }

        [Test]
        public void A_region_mostly_off_the_image_is_refused()
        {
            Assert.IsNull(Crop(-0.6f, 0.2f, 0.2f, 0.6f));
        }

        [Test]
        public void A_region_of_a_few_pixels_is_refused()
        {
            Assert.IsNull(Crop(0.5f, 0.5f, 0.52f, 0.52f));
        }

        [Test]
        public void An_empty_or_inverted_region_is_refused()
        {
            Assert.IsNull(Crop(0.5f, 0.5f, 0.5f, 0.5f));
            Assert.IsNull(CropRect.FromViewport(Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Image));
        }
    }
}
