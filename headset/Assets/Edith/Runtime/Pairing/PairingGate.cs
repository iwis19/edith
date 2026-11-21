using System;
using UnityEngine;

namespace Edith.Pairing
{
    /// Where the headset remembers that a phone is paired, so the gate stays open across launches.
    public interface IPairedStore
    {
        bool Paired { get; set; }
    }

    public sealed class PlayerPrefsPairedStore : IPairedStore
    {
        const string Key = "edith.phonePaired";

        public bool Paired
        {
            get => PlayerPrefs.GetInt(Key, 0) == 1;
            set
            {
                PlayerPrefs.SetInt(Key, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }
    }

    /// Open once the backend confirms a phone pairing, closed again when that pairing is lost. `bypass` keeps it open
    /// regardless, for the Unity editor.
    public sealed class PairingLock
    {
        readonly IPairedStore store;
        readonly bool bypass;
        bool paired;

        public PairingLock(IPairedStore store, bool bypass)
        {
            this.store = store;
            this.bypass = bypass;
            paired = store.Paired;
        }

        public bool Open => bypass || paired;

        /// Whether a phone is paired, as last confirmed or remembered. Unlike Open it ignores the bypass.
        public bool Paired
        {
            get => paired;
            set
            {
                if (paired == value) return;
                var wasOpen = Open;
                paired = value;
                store.Paired = value;
                if (Open != wasOpen) Changed?.Invoke(Open);
            }
        }

        public event Action<bool> Changed;
    }

    /// The one gate every gesture and panel other than the pairing panel checks. The hand sources hand out untracked
    /// frames and the thumb gestures drop out while it is closed, so Look, Collect, Ask, Scan and the diagnostics
    /// toggle all stay inert until the phone is paired.
    public static class PairingGate
    {
        static PairingLock current;

        public static PairingLock Current
        {
            get => current ??= new PairingLock(new PlayerPrefsPairedStore(), Application.isEditor);
            set => current = value;
        }

        public static bool Open => Current.Open;
    }
}
