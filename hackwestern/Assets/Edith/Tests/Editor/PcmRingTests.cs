using Edith.Ask;
using NUnit.Framework;

namespace Edith.Tests
{
    public class PcmRingTests
    {
        static byte[] Pcm(params float[] samples)
        {
            var bytes = new byte[samples.Length * 2];
            Pcm16.Write(samples, samples.Length, bytes);
            return bytes;
        }

        [Test]
        public void Speech_plays_back_in_order_then_silence()
        {
            var ring = new PcmRing(8);
            ring.Write(Pcm(0.5f, -0.5f));
            ring.Write(Pcm(1f));
            var into = new float[5];
            Assert.AreEqual(3, ring.Read(into));
            Assert.AreEqual(0.5f, into[0], 1e-3f);
            Assert.AreEqual(-0.5f, into[1], 1e-3f);
            Assert.AreEqual(1f, into[2], 1e-3f);
            Assert.AreEqual(0f, into[3]);
            Assert.AreEqual(0, ring.Count);
        }

        [Test]
        public void Writes_wrap_around_the_end_and_a_full_ring_keeps_what_fits()
        {
            var ring = new PcmRing(4);
            ring.Write(Pcm(0.1f, 0.2f, 0.3f));
            ring.Read(new float[2]);
            Assert.IsTrue(ring.Write(Pcm(0.4f, 0.5f, 0.6f)));
            Assert.IsFalse(ring.Write(Pcm(0.7f)));
            var into = new float[4];
            Assert.AreEqual(4, ring.Read(into));
            var expected = new[] { 0.3f, 0.4f, 0.5f, 0.6f };
            for (var i = 0; i < expected.Length; i++) Assert.AreEqual(expected[i], into[i], 1e-3f);
        }

        [Test]
        public void Clear_drops_what_is_waiting()
        {
            var ring = new PcmRing(4);
            ring.Write(Pcm(0.1f, 0.2f));
            ring.Clear();
            Assert.AreEqual(0, ring.Read(new float[2]));
        }

        [Test]
        public void Speech_waits_for_the_prebuffer_and_refills_after_running_dry()
        {
            var ring = new PcmRing(16, prebuffer: 3);
            var into = new float[2];
            ring.Write(Pcm(0.1f, 0.2f));
            Assert.AreEqual(0, ring.Read(into), "two samples are not enough to start");
            Assert.AreEqual(0f, into[0]);
            ring.Write(Pcm(0.3f));
            Assert.AreEqual(2, ring.Read(into));
            Assert.AreEqual(0.1f, into[0], 1e-3f);
            Assert.AreEqual(1, ring.Read(into), "the last sample plays, then the ring is dry");
            Assert.AreEqual(1, ring.Underruns);

            ring.Write(Pcm(0.4f, 0.5f));
            Assert.AreEqual(0, ring.Read(into), "after running dry it waits to refill");
            ring.Write(Pcm(0.6f));
            Assert.AreEqual(2, ring.Read(into));
            Assert.AreEqual(0.4f, into[0], 1e-3f);
            Assert.AreEqual(5, ring.Played);
        }

        [Test]
        public void A_finished_answer_drains_below_the_prebuffer()
        {
            var ring = new PcmRing(16, prebuffer: 4);
            ring.Write(Pcm(0.1f, 0.2f, 0.3f));
            var into = new float[2];
            Assert.AreEqual(0, ring.Read(into));
            ring.Finish();
            Assert.AreEqual(2, ring.Read(into));
            Assert.AreEqual(1, ring.Read(into));
            Assert.AreEqual(0.3f, into[0], 1e-3f);
            Assert.AreEqual(0, ring.Count);
            Assert.AreEqual(0, ring.Underruns, "the end of the answer is not a dropout");

            ring.Clear();
            ring.Write(Pcm(0.1f));
            Assert.AreEqual(0, ring.Read(into), "a new answer waits for the prebuffer again");
        }

        [Test]
        public void Microphone_samples_clip_past_full_scale()
        {
            var bytes = new byte[6];
            Assert.AreEqual(6, Pcm16.Write(new[] { 2f, -2f, 0f, 9f }, 3, bytes));
            CollectionAssert.AreEqual(new byte[] { 0xFF, 0x7F, 0x01, 0x80, 0, 0 }, bytes);
        }
    }
}
