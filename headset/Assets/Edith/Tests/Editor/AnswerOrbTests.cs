using Edith.Ask;
using NUnit.Framework;
using UnityEngine;

namespace Edith.Tests
{
    public class AnswerOrbTests
    {
        const string LongAnswer =
            "The Eiffel Tower was built for the 1889 World's Fair and was meant to stand for only twenty years before " +
            "radio antennas on its top made it too useful to tear down, so it still stands in Paris today.";

        GameObject eye;
        AnswerOrb orb;

        [SetUp]
        public void SetUp()
        {
            eye = new GameObject("Eye");
            eye.transform.SetPositionAndRotation(new Vector3(0f, 1.6f, 0f), Quaternion.Euler(0f, 40f, 0f));
            orb = AnswerOrb.Create(eye.transform, Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"), null);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(orb.gameObject);
            Object.DestroyImmediate(eye);
        }

        [Test]
        public void On_a_photo_the_orb_and_caption_span_its_width()
        {
            var width = 0.22f;
            Assert.That(AnswerOrb.OrbMetres + AnswerOrb.GapMetres + AnswerOrb.PerchedCaptionMetres(width),
                Is.EqualTo(width).Within(1e-5f));
            Assert.AreEqual(AnswerOrb.MinCaptionMetres, AnswerOrb.PerchedCaptionMetres(0.05f), "a narrow photo keeps a readable caption");
            Assert.AreEqual(AnswerOrb.HomeCaptionMetres, AnswerOrb.PerchedCaptionMetres(1f), "a wide photo keeps lines short");
        }

        [Test]
        public void A_long_answer_shows_only_its_last_two_lines_from_a_word()
        {
            orb.ShowAnswer(LongAnswer, 1f);
            var shown = orb.Caption;
            Assert.That(shown.Length, Is.LessThan(LongAnswer.Length), "earlier lines scrolled off");
            Assert.That(LongAnswer, Does.EndWith(shown));
            Assert.AreEqual(' ', LongAnswer[LongAnswer.Length - shown.Length - 1], "it starts on a whole word");
            Assert.That(shown, Does.StartWith(shown.TrimStart()));
        }

        [Test]
        public void A_short_answer_shows_whole()
        {
            orb.ShowAnswer("It's a monstera.", 1f);
            Assert.AreEqual("It's a monstera.", orb.Caption);
        }

        [Test]
        public void The_answer_types_out_as_it_is_spoken_and_never_takes_back_letters()
        {
            orb.ShowAnswer("It's a monstera.", 0.5f);
            Assert.AreEqual("It's a m", orb.Caption);
            orb.ShowAnswer("It's a monstera.", 0.25f);
            Assert.AreEqual("It's a m", orb.Caption);
            orb.ShowAnswer("It's a monstera. It", 0.5f);
            Assert.AreEqual("It's a mon", orb.Caption);
            orb.ShowAnswer("It's a monstera. It", 1f);
            Assert.AreEqual("It's a monstera. It", orb.Caption);
        }

        [Test]
        public void A_different_answer_types_out_from_its_start()
        {
            orb.ShowAnswer("…", 1f);
            orb.ShowAnswer("Sure, that's a fern.", 0f);
            Assert.AreEqual("", orb.Caption);
        }

        [Test]
        public void What_was_heard_shows_in_tidy_quotes()
        {
            Assert.AreEqual("“what is this”", AnswerOrb.Quoted(" what  is\nthis "));
            Assert.AreEqual("", AnswerOrb.Quoted("  "));
            Assert.AreEqual("", AnswerOrb.Quoted(null));
            orb.ShowHeard("   ");
            Assert.AreEqual("listening…", orb.Caption, "nothing heard keeps listening");
            orb.ShowHeard("what is this");
            Assert.AreEqual("“what is this”", orb.Caption);
        }
    }
}
