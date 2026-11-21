using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Edith.Ask
{
    /// Writes spelled-out numbers as digits: "twelve hundred" as 1200, "nineteen ninety-nine" as 1999, "five hundred
    /// forty-nine dollars" as $549. An answer card shows Gemini's transcript of its own voice, which spells every
    /// number out. A lone number below ten stays a word ("one of the", "six years") unless a unit follows it.
    public static class SpokenNumbers
    {
        enum Kind { None, Unit, Teen, Tens, Scale, And, Oh, Point }

        readonly struct Word
        {
            public readonly int Start, End;
            public readonly string Lower;
            public readonly Kind Kind;

            public Word(int start, int end, string lower)
            {
                Start = start;
                End = end;
                Lower = lower;
                Kind = KindOf(lower);
            }
        }

        static readonly Dictionary<string, int> Units = new Dictionary<string, int>
        {
            ["zero"] = 0, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6,
            ["seven"] = 7, ["eight"] = 8, ["nine"] = 9,
        };
        static readonly Dictionary<string, int> Teens = new Dictionary<string, int>
        {
            ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["thirteen"] = 13, ["fourteen"] = 14, ["fifteen"] = 15,
            ["sixteen"] = 16, ["seventeen"] = 17, ["eighteen"] = 18, ["nineteen"] = 19,
        };
        static readonly Dictionary<string, int> Tens = new Dictionary<string, int>
        {
            ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40, ["fifty"] = 50, ["sixty"] = 60, ["seventy"] = 70,
            ["eighty"] = 80, ["ninety"] = 90,
        };
        static readonly Dictionary<string, long> Scales = new Dictionary<string, long>
        {
            ["hundred"] = 100, ["thousand"] = 1000, ["million"] = 1000000, ["billion"] = 1000000000,
        };
        static readonly Dictionary<string, (string prefix, string suffix)> Symbols = new Dictionary<string, (string, string)>
        {
            ["dollar"] = ("$", ""), ["dollars"] = ("$", ""), ["euro"] = ("€", ""), ["euros"] = ("€", ""),
            ["percent"] = ("", "%"),
        };

        static Kind KindOf(string w) =>
            Units.ContainsKey(w) ? Kind.Unit
            : Teens.ContainsKey(w) ? Kind.Teen
            : Tens.ContainsKey(w) ? Kind.Tens
            : Scales.ContainsKey(w) ? Kind.Scale
            : w == "and" ? Kind.And
            : w == "oh" ? Kind.Oh
            : w == "point" ? Kind.Point
            : Kind.None;

        static bool Joiner(Kind kind) => kind == Kind.And || kind == Kind.Oh || kind == Kind.Point;

        public static string ToDigits(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var words = Words(text);
            var output = new StringBuilder(text.Length);
            var copied = 0;
            var run = new List<string>();
            for (var i = 0; i < words.Count;)
            {
                var end = RunEnd(text, words, i);
                if (end == i)
                {
                    i++;
                    continue;
                }
                run.Clear();
                for (var k = i; k < end; k++) run.Add(words[k].Lower);

                var last = end;
                var prefix = "";
                var suffix = "";
                if (last < words.Count && Adjacent(text, words[last - 1], words[last])
                    && Symbols.TryGetValue(words[last].Lower, out var symbol))
                {
                    (prefix, suffix) = symbol;
                    last++;
                }
                var value = Value(run);
                var loneSmall = run.Count == 1 && Units.ContainsKey(run[0]) && last == end;
                if (value == null || loneSmall)
                {
                    i = end;
                    continue;
                }
                output.Append(text, copied, words[i].Start - copied);
                output.Append(prefix).Append(value).Append(suffix);
                copied = words[last - 1].End;
                i = last;
            }
            output.Append(text, copied, text.Length - copied);
            return output.ToString();
        }

        static List<Word> Words(string text)
        {
            var words = new List<Word>();
            for (var i = 0; i < text.Length;)
            {
                if (!IsLetter(text[i]))
                {
                    i++;
                    continue;
                }
                var start = i;
                while (i < text.Length && IsLetter(text[i])) i++;
                words.Add(new Word(start, i, text.Substring(start, i - start).ToLowerInvariant()));
            }
            return words;
        }

        static bool IsLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        /// Words with only spaces or a hyphen between them belong to one number.
        static bool Adjacent(string text, in Word a, in Word b)
        {
            for (var p = a.End; p < b.Start; p++)
                if (text[p] != ' ' && text[p] != '-') return false;
            return true;
        }

        /// The index just past the number words that start at `start`, or `start` when no number starts there.
        static int RunEnd(string text, List<Word> words, int start)
        {
            var first = words[start].Kind;
            // "a hundred", "and", "oh" and "point" never begin a number.
            if (first == Kind.None || first == Kind.Scale || Joiner(first)) return start;
            var end = start + 1;
            while (end < words.Count && Adjacent(text, words[end - 1], words[end]))
            {
                var kind = words[end].Kind;
                if (kind == Kind.None) break;
                if (Joiner(kind))
                {
                    // A joiner belongs to the number only when a number word follows it.
                    if (end + 1 >= words.Count || !Adjacent(text, words[end], words[end + 1])) break;
                    var next = words[end + 1].Kind;
                    if (next == Kind.None || next == Kind.Scale || Joiner(next)) break;
                }
                end++;
            }
            return end;
        }

        /// The digits for a run of number words, or null when the run does not read as one number.
        static string Value(List<string> run)
        {
            var point = run.IndexOf("point");
            if (point > 0)
            {
                var whole = Value(run.GetRange(0, point));
                var decimals = new StringBuilder();
                for (var k = point + 1; k < run.Count; k++)
                {
                    if (!Units.TryGetValue(run[k], out var digit)) return null;
                    decimals.Append(digit);
                }
                return whole == null || decimals.Length == 0 ? null : whole.Replace(",", "") + "." + decimals;
            }
            return Year(run) ?? Cardinal(run);
        }

        /// Two pairs of digits read apart: "nineteen ninety-nine", "twenty twenty", "nineteen oh five".
        static string Year(List<string> run)
        {
            if (run.Count < 2 || run.Contains("and")) return null;
            foreach (var w in run)
                if (Scales.ContainsKey(w)) return null;

            int century;
            if (Teens.TryGetValue(run[0], out var teen) && teen >= 11) century = teen;
            else if (Tens.TryGetValue(run[0], out var tens)) century = tens;
            else return null;

            var rest = run.GetRange(1, run.Count - 1);
            int year;
            if (rest[0] == "oh" && rest.Count == 2 && Units.TryGetValue(rest[1], out var unit)) year = unit;
            else if (rest.Count == 1 && Teens.TryGetValue(rest[0], out var t)) year = t;
            else if (rest.Count <= 2 && Tens.TryGetValue(rest[0], out var tensYear))
            {
                year = tensYear;
                if (rest.Count == 2)
                {
                    if (!Units.TryGetValue(rest[1], out var u) || u == 0) return null;
                    year += u;
                }
            }
            // "twenty five" is 25: only a second tens, a teen or "oh" makes a year.
            else return null;
            return (century * 100 + year).ToString(CultureInfo.InvariantCulture);
        }

        static string Cardinal(List<string> run)
        {
            long total = 0, current = 0;
            var last = Kind.None;
            foreach (var w in run)
            {
                var kind = KindOf(w);
                switch (kind)
                {
                    case Kind.Unit:
                        if (last == Kind.Unit || last == Kind.Teen) return null;
                        current += Units[w];
                        break;
                    case Kind.Teen:
                    case Kind.Tens:
                        if (last == Kind.Unit || last == Kind.Teen || last == Kind.Tens) return null;
                        current += kind == Kind.Teen ? Teens[w] : Tens[w];
                        break;
                    case Kind.Scale:
                        var scale = Scales[w];
                        if (scale == 100)
                        {
                            current = (current == 0 ? 1 : current) * 100;
                        }
                        else
                        {
                            total += (current == 0 ? 1 : current) * scale;
                            current = 0;
                        }
                        break;
                    case Kind.And:
                        if (last != Kind.Scale) return null;
                        continue;
                    default:
                        return null;
                }
                last = kind;
            }
            var value = total + current;
            return value >= 10000
                ? value.ToString("N0", CultureInfo.InvariantCulture)
                : value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
