using System.Linq;
using Edith.Bounty;
using NUnit.Framework;

namespace Edith.Tests
{
    public class BountySoundsTests
    {
        static readonly BountyCue[] Cues = { BountyCue.Tick, BountyCue.Find, BountyCue.Fanfare, BountyCue.TimeUp };

        [Test]
        public void Every_cue_is_audible_and_never_clips()
        {
            foreach (var cue in Cues)
            {
                var peak = BountySounds.Samples(cue).Max(sample => System.Math.Abs(sample));
                Assert.Greater(peak, 0.1f, cue.ToString());
                Assert.LessOrEqual(peak, 1f, cue.ToString());
            }
        }

        [Test]
        public void The_tick_is_the_shortest_cue_and_the_fanfare_the_longest_and_none_runs_past_two_seconds()
        {
            var seconds = Cues.ToDictionary(cue => cue, cue => BountySounds.Samples(cue).Length / 48000f);

            Assert.Less(seconds[BountyCue.Tick], seconds[BountyCue.Find]);
            Assert.Less(seconds[BountyCue.Find], seconds[BountyCue.Fanfare]);
            Assert.LessOrEqual(seconds.Values.Max(), 2f);
        }

        [Test]
        public void A_cue_ends_quietly()
        {
            foreach (var cue in Cues)
            {
                var samples = BountySounds.Samples(cue);
                Assert.Less(System.Math.Abs(samples[samples.Length - 1]), 0.01f, cue.ToString());
            }
        }
    }
}
