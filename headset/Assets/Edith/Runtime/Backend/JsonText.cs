using System.Globalization;
using System.Text;

namespace Edith.Backend
{
    /// Hand-built JSON for the few messages JsonUtility cannot write, such as a string inside a larger body.
    public static class JsonText
    {
        /// `value` as a JSON string, quotes included. A null reads as an empty string.
        public static string Quote(string value)
        {
            var quoted = new StringBuilder("\"");
            foreach (var c in value ?? "")
            {
                switch (c)
                {
                    case '"': quoted.Append("\\\""); break;
                    case '\\': quoted.Append("\\\\"); break;
                    case '\n': quoted.Append("\\n"); break;
                    case '\r': quoted.Append("\\r"); break;
                    case '\t': quoted.Append("\\t"); break;
                    default:
                        if (c < ' ') quoted.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else quoted.Append(c);
                        break;
                }
            }
            return quoted.Append('"').ToString();
        }
    }
}
