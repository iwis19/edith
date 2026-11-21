using System;
using UnityEngine;

namespace Edith.Look
{
    public enum LookEventKind { Accepted, Name, Fact, Rarity, Done, Error }

    public enum Rarity { Common, Uncommon, Rare, Epic, Legendary, Mythic }

    /// One line of the backend's /look stream, reduced to what the card uses. Only the fields of its Kind are set.
    public readonly struct LookEvent
    {
        public readonly LookEventKind Kind;
        /// The backend's id for the Look, for Accepted.
        public readonly string LookRef;
        /// For Name. A pending name is the text written so far, and a later Name replaces it.
        public readonly string Name;
        public readonly float Confidence;
        public readonly bool Pending;
        /// The fact, for Fact.
        public readonly string Text;
        public readonly Rarity Tier;
        public readonly string Code, Message;

        LookEvent(LookEventKind kind, string lookRef = null, string name = null, float confidence = 0f, bool pending = false,
            string text = null, Rarity tier = Rarity.Common, string code = null, string message = null)
        {
            Kind = kind;
            LookRef = lookRef;
            Name = name;
            Confidence = confidence;
            Pending = pending;
            Text = text;
            Tier = tier;
            Code = code;
            Message = message;
        }

        /// Done and Error each end a stream.
        public bool Ends => Kind == LookEventKind.Done || Kind == LookEventKind.Error;

        public static LookEvent Accepted(string lookRef) => new LookEvent(LookEventKind.Accepted, lookRef: lookRef);

        public static LookEvent Named(string name, float confidence = 1f, bool pending = false) =>
            new LookEvent(LookEventKind.Name, name: name, confidence: confidence, pending: pending);

        public static LookEvent Fact(string text) => new LookEvent(LookEventKind.Fact, text: text);

        public static LookEvent Rated(Rarity tier) => new LookEvent(LookEventKind.Rarity, tier: tier);

        public static LookEvent Done() => new LookEvent(LookEventKind.Done);

        public static LookEvent Failure(string code, string message) =>
            new LookEvent(LookEventKind.Error, code: code, message: message);

        [Serializable]
        sealed class Wire
        {
            public string type;
            public string look_ref;
            public string name;
            public float confidence;
            public bool pending;
            public string text;
            public string tier;
            public string code;
            public string message;
        }

        /// False for a line that is not JSON, has a type this build does not know, or a rarity it does not know, so
        /// a newer backend can add events. Never throws.
        public static bool TryParse(string line, out LookEvent parsed)
        {
            parsed = default;
            if (string.IsNullOrWhiteSpace(line)) return false;
            Wire wire;
            try
            {
                wire = JsonUtility.FromJson<Wire>(line);
            }
            catch (ArgumentException)
            {
                return false;
            }
            switch (wire?.type)
            {
                case "accepted":
                    parsed = Accepted(wire.look_ref ?? "");
                    return true;
                case "name":
                    parsed = Named(wire.name ?? "", wire.confidence, wire.pending);
                    return true;
                case "fact":
                    parsed = Fact(wire.text ?? "");
                    return true;
                case "rarity":
                    if (!TryTier(wire.tier, out var tier)) return false;
                    parsed = Rated(tier);
                    return true;
                case "done":
                    parsed = Done();
                    return true;
                case "error":
                    parsed = Failure(wire.code ?? "unknown", wire.message ?? "");
                    return true;
                default:
                    return false;
            }
        }

        static bool TryTier(string name, out Rarity tier)
        {
            switch (name)
            {
                case "common": tier = Rarity.Common; return true;
                case "uncommon": tier = Rarity.Uncommon; return true;
                case "rare": tier = Rarity.Rare; return true;
                case "epic": tier = Rarity.Epic; return true;
                case "legendary": tier = Rarity.Legendary; return true;
                case "mythic": tier = Rarity.Mythic; return true;
                default: tier = default; return false;
            }
        }
    }
}
