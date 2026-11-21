using Edith.Ask;
using NUnit.Framework;

namespace Edith.Tests
{
    public class SpokenNumbersTests
    {
        [TestCase("It cost twelve hundred dollars.", "It cost $1200.")]
        [TestCase("About twelve hundred of them.", "About 1200 of them.")]
        [TestCase("They launched in December twenty twenty, building on prototypes.", "They launched in December 2020, building on prototypes.")]
        [TestCase("They retail for five hundred forty-nine dollars, though.", "They retail for $549, though.")]
        [TestCase("Built in nineteen ninety-nine.", "Built in 1999.")]
        [TestCase("Since twenty twenty-five.", "Since 2025.")]
        [TestCase("A nineteen oh five design.", "A 1905 design.")]
        [TestCase("Patented in seventeen sixty.", "Patented in 1760.")]
        [TestCase("In two thousand and five it sold out.", "In 2005 it sold out.")]
        [TestCase("Over one hundred and twenty parts.", "Over 120 parts.")]
        [TestCase("About ten thousand were made.", "About 10,000 were made.")]
        [TestCase("Roughly three million visitors.", "Roughly 3,000,000 visitors.")]
        [TestCase("It weighs three point eight five kilograms.", "It weighs 3.85 kilograms.")]
        [TestCase("Twelve drivers and twenty five magnets.", "12 drivers and 25 magnets.")]
        [TestCase("Fifty percent lighter, nine percent louder.", "50% lighter, 9% louder.")]
        [TestCase("Two euros each.", "€2 each.")]
        public void Spoken_numbers_become_digits(string spoken, string written)
        {
            Assert.AreEqual(written, SpokenNumbers.ToDigits(spoken));
        }

        [TestCase("One of the few, about six years old, one to three sentences.")]
        [TestCase("A hundred reasons, and then some.")]
        [TestCase("Oh, and point taken.")]
        [TestCase("Someone nine-tenths done.")]
        [TestCase("")]
        public void Small_numbers_and_ordinary_words_stay_as_they_are(string text)
        {
            Assert.AreEqual(text, SpokenNumbers.ToDigits(text));
        }

        [Test]
        public void A_number_still_being_spoken_reads_as_digits_once_it_is_whole()
        {
            Assert.AreEqual("in December 20", SpokenNumbers.ToDigits("in December twenty"));
            Assert.AreEqual("in December 2020", SpokenNumbers.ToDigits("in December twenty twenty"));
        }

        [Test]
        public void Null_stays_null() => Assert.IsNull(SpokenNumbers.ToDigits(null));
    }
}
