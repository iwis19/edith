using System;
using System.Collections.Generic;
using System.Globalization;
using Edith.Look;
using UnityEngine;

namespace Edith.Scrapbook
{
    /// One collected memory, as its card shows it.
    public sealed class Memory
    {
        public readonly int Id;
        public readonly string Name;
        /// Null when the Look ended without a fact.
        public readonly string Fact;
        /// Null when the Look ended without a rarity.
        public readonly Rarity? Tier;
        /// Null when the headset had no place for it.
        public readonly string Place;
        /// The local calendar day it was collected, with no time of day.
        public readonly DateTime Date;

        public Memory(int id, string name, string fact, Rarity? tier, string place, DateTime date)
        {
            Id = id;
            Name = name;
            Fact = fact;
            Tier = tier;
            Place = place;
            Date = date.Date;
        }

        /// The card's last line: the place and the day as month/day, or the day alone without a place.
        public string Caption => Place == null ? Day(Date) : $"{Place} · {Day(Date)}";

        public static string Day(DateTime date) => date.ToString("M/d", CultureInfo.InvariantCulture);
    }

    /// The body of GET /memories.
    public static class MemoryList
    {
        [Serializable]
        sealed class Wire
        {
            public Entry[] memories;
        }

        [Serializable]
        sealed class Entry
        {
            public int id;
            public string name;
            public string fact;
            public string rarity;
            public string place;
            public string date;
        }

        /// The memories in the backend's order, newest first. An entry whose date cannot be read is left out, and a
        /// rarity this build does not know reads as none. Throws FormatException when `json` is not a memory list.
        public static IReadOnlyList<Memory> Parse(string json)
        {
            Wire wire;
            try
            {
                wire = JsonUtility.FromJson<Wire>(json);
            }
            catch (ArgumentException e)
            {
                throw new FormatException("the memory list is not JSON", e);
            }
            // JsonUtility may fill a field the JSON lacks with an empty array, which would read as nothing collected.
            if (wire?.memories == null || !json.Contains("\"memories\"")) throw new FormatException("the memory list has no memories field");

            var memories = new List<Memory>(wire.memories.Length);
            foreach (var entry in wire.memories)
            {
                if (!DateTime.TryParseExact(entry.date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    continue;
                memories.Add(new Memory(entry.id, entry.name ?? "", Text(entry.fact),
                    LookEvent.TryTier(entry.rarity, out var tier) ? tier : (Rarity?)null, Text(entry.place), date));
            }
            return memories;
        }

        // JsonUtility reads a JSON null as an empty string.
        static string Text(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
