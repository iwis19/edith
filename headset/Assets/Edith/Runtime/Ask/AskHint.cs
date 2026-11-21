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
        /// gesture opened, which never carries a hint.
        public static string RequestBody(AskHint? hint, bool navigate = false) => navigate ? "{\"navigate\":true}"
            : hint is { } h
                ? JsonUtility.ToJson(new Body { hint = new Wire { name = h.Name, hook = h.Hook ?? "" } })
                : "{}";
    }
}
