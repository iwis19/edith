using Edith.Ask;
using Edith.Snapshots;
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
        public void A_snapshot_keeps_the_photo_it_shows()
        {
            var parent = new GameObject("Room");
            var photo = new Texture2D(4, 4);
            try
            {
                var box = new SightBox(new Pose(Vector3.zero, Quaternion.identity), Rect.MinMaxRect(-0.1f, -0.1f, 0.1f, 0.1f), 1f);
                Assert.AreSame(photo, Snapshot.Create(box, parent.transform, photo).Photo);
                Assert.IsNull(Snapshot.Create(box, parent.transform).Photo);
            }
            finally
            {
                Object.DestroyImmediate(parent);
                Object.DestroyImmediate(photo);
            }
        }
    }
}
