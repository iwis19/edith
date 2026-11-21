using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Edith.Bounty
{
    public enum HuntStatus { Active, FoundAll, Stopped, Timeout }

    /// One of a hunt's targets. `Tier` is set only on a rarity target, which a Look of that tier or better ticks.
    public readonly struct HuntTarget
    {
        public readonly int Index;
        public readonly string Text;
        public readonly string Tier;
        public readonly bool Found;

        public HuntTarget(int index, string text, string tier, bool found)
        {
            Index = index;
            Text = text;
            Tier = tier;
            Found = found;
        }

        public HuntTarget AsFound() => new HuntTarget(Index, Text, Tier, true);
    }

    /// The backend's HuntState, as /hunt, /hunt/stop and the tool responses carry it.
    public sealed class HuntState
    {
        public readonly string HuntRef;
        public readonly HuntStatus Status;
        /// Seconds left when the backend wrote it; 0 once the hunt is over.
        public readonly float RemainingSeconds;
        public readonly IReadOnlyList<HuntTarget> Targets;

        public HuntState(string huntRef, HuntStatus status, float remainingSeconds, IReadOnlyList<HuntTarget> targets)
        {
            HuntRef = huntRef;
            Status = status;
            RemainingSeconds = remainingSeconds;
            Targets = targets;
        }

        public int Found
        {
            get
            {
                var found = 0;
                foreach (var target in Targets)
                    if (target.Found) found++;
                return found;
            }
        }

        [Serializable]
        sealed class Wire
        {
            public string hunt_ref;
            public string status;
            public float remaining_s;
            public WireTarget[] targets;
        }

        [Serializable]
        sealed class WireTarget
        {
            public int index;
            public string text;
            public string tier;
            public bool found;
        }

        // JsonUtility cannot read a null into a string, so the one nullable string this reads is made empty first.
        static readonly Regex NullTier = new Regex("\"tier\"\\s*:\\s*null", RegexOptions.Compiled);

        /// False for anything that is not a hunt with a ref, a known status and at least one target. Never throws.
        public static bool TryParse(string json, out HuntState state)
        {
            state = null;
            if (string.IsNullOrWhiteSpace(json)) return false;
            Wire wire;
            try
            {
                wire = JsonUtility.FromJson<Wire>(NullTier.Replace(json, "\"tier\":\"\""));
            }
            catch (ArgumentException)
            {
                return false;
            }
            if (wire == null || string.IsNullOrEmpty(wire.hunt_ref) || wire.targets == null || wire.targets.Length == 0) return false;
            if (!TryStatus(wire.status, out var status)) return false;
            var targets = new List<HuntTarget>(wire.targets.Length);
            foreach (var target in wire.targets)
                if (target != null)
                    targets.Add(new HuntTarget(target.index, target.text ?? "", string.IsNullOrEmpty(target.tier) ? null : target.tier, target.found));
            targets.Sort((a, b) => a.Index.CompareTo(b.Index));
            state = new HuntState(wire.hunt_ref, status, Mathf.Max(0f, wire.remaining_s), targets);
            return true;
        }

        static bool TryStatus(string name, out HuntStatus status)
        {
            switch (name)
            {
                case "active": status = HuntStatus.Active; return true;
                case "found_all": status = HuntStatus.FoundAll; return true;
                case "stopped": status = HuntStatus.Stopped; return true;
                case "timeout": status = HuntStatus.Timeout; return true;
                default: status = default; return false;
            }
        }
    }
}
