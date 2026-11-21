using Edith.Scrapbook;
using NUnit.Framework;
using UnityEngine;

namespace Edith.Tests
{
    public class PinchIntentTests
    {
        static readonly Vector3 Pressed = new Vector3(0.1f, 0f, 0.4f);

        [Test]
        public void Reaching_forward_with_a_little_sideways_drift_is_a_choice()
        {
            Assert.IsFalse(PinchIntent.Scrubs(Pressed, Pressed + new Vector3(0.02f, 0f, 0.15f)));
            var intent = new PinchIntent(Pressed);
            Assert.IsFalse(intent.Move(Pressed + new Vector3(0.02f, 0f, 0.15f)));
            Assert.IsFalse(intent.Scrubbing);
        }

        [Test]
        public void Moving_eight_centimetres_sideways_is_a_scrub()
        {
            Assert.IsTrue(PinchIntent.Scrubs(Pressed, Pressed + new Vector3(0.08f, 0f, 0f)));
            Assert.IsTrue(PinchIntent.Scrubs(Pressed, Pressed + new Vector3(-0.08f, 0f, 0f)));
            Assert.IsTrue(new PinchIntent(Pressed).Move(Pressed + new Vector3(0.08f, 0f, 0f)));
        }

        [Test]
        public void Moving_ten_centimetres_up_or_down_is_a_choice()
        {
            Assert.IsFalse(PinchIntent.Scrubs(Pressed, Pressed + new Vector3(0f, 0.10f, 0f)));
            Assert.IsFalse(PinchIntent.Scrubs(Pressed, Pressed + new Vector3(0f, -0.10f, 0f)));
        }

        [Test]
        public void The_threshold_is_six_centimetres_sideways()
        {
            Assert.IsFalse(PinchIntent.Scrubs(Pressed, Pressed + new Vector3(0.059f, 0f, 0f)));
            Assert.IsTrue(PinchIntent.Scrubs(Pressed, Pressed + new Vector3(0.061f, 0f, 0f)));
        }

        [Test]
        public void Once_it_has_scrubbed_coming_back_does_not_make_it_a_choice()
        {
            var intent = new PinchIntent(Pressed);
            intent.Move(Pressed + new Vector3(0.09f, 0f, 0f));
            Assert.IsTrue(intent.Move(Pressed));
            Assert.AreEqual(0.09f, intent.Sideways, 1e-5f);
        }
    }
}
