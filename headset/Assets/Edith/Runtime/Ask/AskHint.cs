using System;
using UnityEngine;

namespace Edith.Ask
{
    /// The Scan pin nearest the line of sight when a question starts. The backend names it in the session persona, so
    /// "this" and "it" mean the pin.
    public readonly struct AskHint
    {
        public readonly string Name, Hook;

        public AskHint(string name, string hook)
        {
            Name = name;
            Hook = hook;
        }

        [Serializable] sealed class Body { public Wire hint; }
        [Serializable] sealed class Wire { public string name, hook; }

        /// The POST /ask/session body: `{}` without a hint, and `{"navigate":true}` for a session the directions
        /// gesture opened, which never carries a hint. A `voice` the person picked in the welcome is added to either,
        /// and so are their `interests` when there are any.
        public static string RequestBody(AskHint? hint, bool navigate = false, string voice = null, string[] interests = null)
        {
            var body = navigate ? "{\"navigate\":true}"
                : hint is { } h
                    ? JsonUtility.ToJson(new Body { hint = new Wire { name = h.Name, hook = h.Hook ?? "" } })
                    : "{}";
            // The voice is one of Gemini's plain voice names, so it needs no escaping.
            if (!string.IsNullOrEmpty(voice)) body = Append(body, "\"voice\":\"" + voice + "\"");
            if (interests != null && interests.Length > 0)
                body = Append(body, "\"interests\":[" + string.Join(",", System.Array.ConvertAll(interests, Quote)) + "]");
            return body;
        }

        static string Append(string body, string field) =>
            body == "{}" ? "{" + field + "}" : body.Substring(0, body.Length - 1) + "," + field + "}";

        static string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
