using System.Collections.Generic;
using System.Text;

namespace Edith.Look
{
    /// Splits a response body that arrives in chunks into its lines. A multibyte character cut by a chunk edge is
    /// completed by the next chunk, and a line without its newline yet waits for it.
    public sealed class NdjsonParser
    {
        readonly Decoder decoder = new UTF8Encoding(false).GetDecoder();
        readonly StringBuilder pending = new StringBuilder();
        char[] chars = new char[1024];

        /// Appends every line the first `count` bytes of `data` complete to `lines`, without its line ending. Empty
        /// lines are skipped.
        public void Feed(byte[] data, int count, List<string> lines)
        {
            var needed = decoder.GetCharCount(data, 0, count);
            if (chars.Length < needed) chars = new char[needed];
            var decoded = decoder.GetChars(data, 0, count, chars, 0);
            for (var i = 0; i < decoded; i++)
            {
                if (chars[i] != '\n')
                {
                    pending.Append(chars[i]);
                    continue;
                }
                if (pending.Length > 0 && pending[pending.Length - 1] == '\r') pending.Length--;
                if (pending.Length > 0) lines.Add(pending.ToString());
                pending.Length = 0;
            }
        }
    }
}
