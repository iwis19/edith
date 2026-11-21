using Edith.Capture;
using NUnit.Framework;
using UnityEngine;

namespace Edith.Tests
{
    public class CropSizeTests
    {
        [TestCase(1280, 960, 512, 512, 384)]
        [TestCase(960, 1280, 512, 384, 512)]
        [TestCase(1000, 1000, 512, 512, 512)]
        [TestCase(300, 200, 512, 300, 200)]
        [TestCase(4000, 10, 512, 512, 1)]
        public void Photos_shrink_to_the_long_side_limit_and_never_grow(int w, int h, int max, int ew, int eh)
        {
            Assert.AreEqual(new Vector2Int(ew, eh), CropSize.FitLongSide(w, h, max));
        }

        [Test]
        public void A_looks_photo_is_capped_at_768_px()
        {
            Assert.AreEqual(new Vector2Int(768, 576), CropSize.FitLongSide(1280, 960, CropSize.MaxSide));
        }
    }
}
