using System;
using Edith.Look;
using Edith.Scrapbook;
using NUnit.Framework;

namespace Edith.Tests
{
    public class MemoryListTests
    {
        const string Two =
            "{\"memories\":[" +
            "{\"id\":12,\"name\":\"Red mug\",\"fact\":\"Mugs are old.\",\"rarity\":\"rare\",\"place\":\"London, Ontario\",\"date\":\"2026-11-21\"}," +
            "{\"id\":9,\"name\":\"Fern\",\"fact\":null,\"rarity\":null,\"place\":null,\"date\":\"2026-03-04\"}]}";

        [Test]
        public void A_memory_reads_every_field_its_card_shows()
        {
            var memory = MemoryList.Parse(Two)[0];
            Assert.AreEqual(12, memory.Id);
            Assert.AreEqual("Red mug", memory.Name);
            Assert.AreEqual("Mugs are old.", memory.Fact);
            Assert.AreEqual(Rarity.Rare, memory.Tier);
            Assert.AreEqual("London, Ontario", memory.Place);
            Assert.AreEqual(new DateTime(2026, 11, 21), memory.Date);
        }

        [Test]
        public void The_backends_order_is_kept()
        {
            var memories = MemoryList.Parse(Two);
            Assert.AreEqual(2, memories.Count);
            Assert.AreEqual(9, memories[1].Id);
        }

        [Test]
        public void Missing_fact_rarity_and_place_read_as_none()
        {
            var memory = MemoryList.Parse(Two)[1];
            Assert.IsNull(memory.Fact);
            Assert.IsNull(memory.Tier);
            Assert.IsNull(memory.Place);
        }

        [Test]
        public void An_unknown_rarity_reads_as_none()
        {
            var memory = MemoryList.Parse("{\"memories\":[{\"id\":1,\"name\":\"x\",\"rarity\":\"cosmic\",\"date\":\"2026-11-21\"}]}")[0];
            Assert.IsNull(memory.Tier);
        }

        [Test]
        public void Fields_this_build_does_not_know_are_ignored()
        {
            var memories = MemoryList.Parse(
                "{\"memories\":[{\"id\":1,\"name\":\"x\",\"date\":\"2026-11-21\",\"weather\":{\"sky\":\"clear\"}}],\"next\":null}");
            Assert.AreEqual(1, memories.Count);
        }

        [Test]
        public void A_point_the_backend_sends_is_ignored()
        {
            var memories = MemoryList.Parse(
                "{\"memories\":[{\"id\":1,\"name\":\"x\",\"date\":\"2026-11-21\",\"point\":[43.00542,-81.2748]}]}");
            Assert.AreEqual(1, memories.Count);
        }

        [Test]
        public void A_memory_without_a_readable_date_is_left_out()
        {
            var memories = MemoryList.Parse(
                "{\"memories\":[{\"id\":1,\"name\":\"x\",\"date\":\"yesterday\"},{\"id\":2,\"name\":\"y\",\"date\":\"2026-11-21\"}]}");
            Assert.AreEqual(1, memories.Count);
            Assert.AreEqual(2, memories[0].Id);
        }

        [Test]
        public void An_empty_list_is_no_memories()
        {
            Assert.AreEqual(0, MemoryList.Parse("{\"memories\":[]}").Count);
        }

        [Test]
        public void An_answer_that_is_not_a_memory_list_throws()
        {
            Assert.Throws<FormatException>(() => MemoryList.Parse("<html>502</html>"));
            Assert.Throws<FormatException>(() => MemoryList.Parse("{\"detail\":\"nope\"}"));
        }
    }
}
