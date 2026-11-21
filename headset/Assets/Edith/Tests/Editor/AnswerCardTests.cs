using Edith.Ask;
using NUnit.Framework;
using UnityEngine;

namespace Edith.Tests
{
    public class AnswerCardTests
    {
        GameObject eye;
        AnswerCard card;

        [SetUp]
        public void SetUp()
        {
            eye = new GameObject("Eye");
            eye.transform.SetPositionAndRotation(new Vector3(0f, 1.6f, 0f), Quaternion.Euler(0f, 40f, 0f));
            card = AnswerCard.Create(eye.transform, Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"), null);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(card.gameObject);
            Object.DestroyImmediate(eye);
        }

        [Test]
        public void A_new_card_rests_below_the_line_of_sight_facing_the_eye()
        {
            var local = eye.transform.InverseTransformPoint(card.transform.position);
            Assert.That(local.z, Is.EqualTo(0.55f).Within(1e-3f));
            Assert.That(local.y, Is.EqualTo(-0.12f).Within(1e-3f));
            Assert.That(Vector3.Dot(card.transform.forward, (card.transform.position - eye.transform.position).normalized),
                Is.GreaterThan(0.999f));
        }

        [Test]
        public void What_was_heard_is_one_tidy_quoted_line()
        {
            Assert.AreEqual("“what is this”", AnswerCard.HeardLine(" what  is\nthis "));
            Assert.AreEqual("", AnswerCard.HeardLine("  "));
            Assert.AreEqual("", AnswerCard.HeardLine(null));
        }

        [Test]
        public void A_long_question_keeps_its_latest_words_from_a_word_break()
        {
            var words = "can you tell me what kind of plant this is and whether it needs a lot of water every week";
            var line = AnswerCard.HeardLine(words);
            Assert.That(line, Does.StartWith("“…"));
            Assert.That(line, Does.EndWith(" every week”"));
            Assert.That(line.Length, Is.LessThanOrEqualTo(AnswerCard.MaxHeardChars + 3));
            var kept = line.Substring(2, line.Length - 3);
            Assert.That(words, Does.EndWith(" " + kept), "it starts on a whole word");
        }


        [Test]
        public void Sources_show_as_one_line_of_at_most_three_names()
        {
            Assert.AreEqual("", AnswerCard.SourcesLine(new string[0]));
            Assert.AreEqual("Sources: a.com", AnswerCard.SourcesLine(new[] { "a.com" }));
            Assert.AreEqual("Sources: a.com · b.com · c.com", AnswerCard.SourcesLine(new[] { "a.com", "b.com", "c.com", "d.com" }));
        }
    }
}
