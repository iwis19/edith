using System.Collections.Generic;
using Edith.Scrapbook;
using NUnit.Framework;

namespace Edith.Tests
{
    public class RecentCacheTests
    {
        List<string> evicted;
        RecentCache<string> cache;

        [SetUp]
        public void SetUp()
        {
            evicted = new List<string>();
            cache = new RecentCache<string>(3, evicted.Add);
        }

        [Test]
        public void It_holds_its_capacity_and_the_next_evicts_the_oldest()
        {
            cache.Put(1, "a");
            cache.Put(2, "b");
            cache.Put(3, "c");
            CollectionAssert.IsEmpty(evicted);
            cache.Put(4, "d");
            CollectionAssert.AreEqual(new[] { "a" }, evicted);
            Assert.IsFalse(cache.TryGet(1, out _));
            Assert.IsTrue(cache.TryGet(4, out var value));
            Assert.AreEqual("d", value);
        }

        [Test]
        public void A_value_just_read_outlives_one_that_was_not()
        {
            cache.Put(1, "a");
            cache.Put(2, "b");
            cache.Put(3, "c");
            cache.TryGet(1, out _);
            cache.Put(4, "d");
            CollectionAssert.AreEqual(new[] { "b" }, evicted);
            Assert.IsTrue(cache.TryGet(1, out _));
        }

        [Test]
        public void Putting_a_key_again_evicts_its_old_value()
        {
            cache.Put(1, "a");
            cache.Put(1, "b");
            CollectionAssert.AreEqual(new[] { "a" }, evicted);
            Assert.AreEqual(1, cache.Count);
        }

        [Test]
        public void Clearing_evicts_everything()
        {
            cache.Put(1, "a");
            cache.Put(2, "b");
            cache.Clear();
            CollectionAssert.AreEquivalent(new[] { "a", "b" }, evicted);
            Assert.AreEqual(0, cache.Count);
            Assert.IsFalse(cache.TryGet(2, out _));
        }
    }
}
