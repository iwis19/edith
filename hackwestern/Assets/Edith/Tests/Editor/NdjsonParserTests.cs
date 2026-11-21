using System.Collections.Generic;
using System.Text;
using Edith.Look;
using NUnit.Framework;

namespace Edith.Tests
{
    public class NdjsonParserTests
    {
        static List<string> Feed(NdjsonParser parser, byte[] bytes, int from, int count)
        {
            var lines = new List<string>();
            var chunk = new byte[count];
            System.Array.Copy(bytes, from, chunk, 0, count);
            parser.Feed(chunk, count, lines);
            return lines;
        }

        static List<string> FeedAll(NdjsonParser parser, string text) =>
            Feed(parser, Encoding.UTF8.GetBytes(text), 0, Encoding.UTF8.GetByteCount(text));

        [Test]
        public void A_chunk_with_several_lines_gives_each_of_them()
        {
            CollectionAssert.AreEqual(new[] { "{\"a\":1}", "{\"b\":2}" }, FeedAll(new NdjsonParser(), "{\"a\":1}\n{\"b\":2}\n"));
        }

        [Test]
        public void A_line_split_across_chunks_comes_out_whole()
        {
            var parser = new NdjsonParser();
            Assert.IsEmpty(FeedAll(parser, "{\"type\":\"na"));
            Assert.IsEmpty(FeedAll(parser, "me\",\"name\":\"mug\"}"));
            CollectionAssert.AreEqual(new[] { "{\"type\":\"name\",\"name\":\"mug\"}" }, FeedAll(parser, "\n"));
        }

        [Test]
        public void A_multibyte_character_split_across_chunks_is_not_garbled()
        {
            var bytes = Encoding.UTF8.GetBytes("{\"name\":\"café ☕ \U0001F600\"}\n");
            var parser = new NdjsonParser();
            var lines = new List<string>();
            // One byte per chunk cuts every multibyte character.
            for (var i = 0; i < bytes.Length; i++) lines.AddRange(Feed(parser, bytes, i, 1));
            CollectionAssert.AreEqual(new[] { "{\"name\":\"café ☕ \U0001F600\"}" }, lines);
        }

        [Test]
        public void Windows_line_endings_are_dropped_and_empty_lines_skipped()
        {
            CollectionAssert.AreEqual(new[] { "one", "two" }, FeedAll(new NdjsonParser(), "one\r\n\r\ntwo\r\n\n"));
        }

        [Test]
        public void A_trailing_partial_line_is_held_until_its_newline()
        {
            var parser = new NdjsonParser();
            CollectionAssert.AreEqual(new[] { "first" }, FeedAll(parser, "first\nsecond"));
            CollectionAssert.AreEqual(new[] { "second" }, FeedAll(parser, "\n"));
        }

        [Test]
        public void A_malformed_line_is_skipped_by_the_event_parse_and_the_next_one_still_parses()
        {
            var lines = FeedAll(new NdjsonParser(), "{\"type\":\"name\",\"na\n{\"type\":\"done\"}\n");
            var events = new List<LookEvent>();
            foreach (var line in lines)
                if (LookEvent.TryParse(line, out var e)) events.Add(e);
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(LookEventKind.Done, events[0].Kind);
        }

        [Test]
        public void Bytes_that_are_not_utf8_do_not_throw()
        {
            var lines = new List<string>();
            Assert.DoesNotThrow(() => new NdjsonParser().Feed(new byte[] { 0xFF, 0xFE, (byte)'\n' }, 3, lines));
            Assert.AreEqual(1, lines.Count);
        }
    }
}
