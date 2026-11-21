using System;
using UnityEngine;

namespace Edith.Ask
{
    /// The backend's answer to POST /ask/session: a Gemini Live websocket URL carrying a single-use token, the setup
    /// frame to send first on it, and the model that setup names.
    public readonly struct AskGrant
    {
        public readonly string Url;
        /// A complete `{"setup": {...}}` message.
        public readonly string SetupFrame;
        public readonly string Model;

        public AskGrant(string url, string setupFrame, string model)
        {
            Url = url;
            SetupFrame = setupFrame;
            Model = model;
        }

        [Serializable] sealed class Wire { public string url, model; }
        [Serializable] sealed class Text { public string value; }

        /// False when `json` has no url or no setup object. `setup` may be the setup object itself, the whole setup
        /// message, or either one encoded as a JSON string.
        public static bool TryParse(string json, out AskGrant grant)
        {
            grant = default;
            Wire wire;
            try
            {
                wire = JsonUtility.FromJson<Wire>(json);
            }
            catch (ArgumentException)
            {
                return false;
            }
            if (wire == null || string.IsNullOrEmpty(wire.url)) return false;

            var setup = Json.Field(json, "setup");
            if (setup != null && setup.StartsWith("\"", StringComparison.Ordinal))
            {
                try
                {
                    setup = JsonUtility.FromJson<Text>("{\"value\":" + setup + "}")?.value?.Trim();
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }
            if (string.IsNullOrEmpty(setup) || setup[0] != '{' || !Json.IsObject(setup)) return false;

            var frame = Json.Field(setup, "setup") != null ? setup : "{\"setup\":" + setup + "}";
            grant = new AskGrant(wire.url, frame, wire.model ?? "");
            return true;
        }
    }

    /// Finds a top-level field of a JSON object as raw text, which JsonUtility cannot do for an object value.
    static class Json
    {
        /// The raw text of `key`'s value in the object `json`, or null when it has none or `json` is malformed.
        public static string Field(string json, string key)
        {
            var i = Skip(json, 0);
            if (i >= json.Length || json[i] != '{') return null;
            i = Skip(json, i + 1);
            if (i < json.Length && json[i] == '}') return null;
            while (true)
            {
                if (i >= json.Length || json[i] != '"') return null;
                var keyEnd = StringEnd(json, i);
                if (keyEnd < 0) return null;
                var name = json.Substring(i + 1, keyEnd - i - 2);
                i = Skip(json, keyEnd);
                if (i >= json.Length || json[i] != ':') return null;
                i = Skip(json, i + 1);
                var valueEnd = ValueEnd(json, i);
                if (valueEnd < 0) return null;
                if (name == key) return json.Substring(i, valueEnd - i);
                i = Skip(json, valueEnd);
                if (i >= json.Length || json[i] != ',') return null;
                i = Skip(json, i + 1);
            }
        }

        /// True when `json` is exactly one balanced object.
        public static bool IsObject(string json)
        {
            var i = Skip(json, 0);
            if (i >= json.Length || json[i] != '{') return false;
            var end = ValueEnd(json, i);
            return end > 0 && Skip(json, end) == json.Length;
        }

        static int Skip(string s, int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            return i;
        }

        /// The index just past the string that opens at `i`, or -1 when it never closes.
        static int StringEnd(string s, int i)
        {
            for (var j = i + 1; j < s.Length; j++)
            {
                if (s[j] == '\\') j++;
                else if (s[j] == '"') return j + 1;
            }
            return -1;
        }

        /// The index just past the value that starts at `i`, or -1 when it is cut off.
        static int ValueEnd(string s, int i)
        {
            if (i >= s.Length) return -1;
            if (s[i] == '"') return StringEnd(s, i);
            if (s[i] == '{' || s[i] == '[')
            {
                var depth = 0;
                for (var j = i; j < s.Length; j++)
                {
                    var c = s[j];
                    if (c == '"')
                    {
                        j = StringEnd(s, j);
                        if (j < 0) return -1;
                        j--;
                    }
                    else if (c == '{' || c == '[') depth++;
                    else if ((c == '}' || c == ']') && --depth == 0) return j + 1;
                }
                return -1;
            }
            var k = i;
            while (k < s.Length && s[k] != ',' && s[k] != '}' && s[k] != ']' && !char.IsWhiteSpace(s[k])) k++;
            return k > i ? k : -1;
        }
    }
}
