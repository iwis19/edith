using System.Collections.Generic;
using UnityEngine;

namespace Edith.Hands
{
    /// Says when a panel has the hands to itself. While Held, pinches and hand poses belong to the panels holding
    /// it, and Look and Scan leave them alone.
    public sealed class HandGate : MonoBehaviour
    {
        readonly HashSet<object> holders = new HashSet<object>();

        public bool Held => holders.Count > 0;

        /// Takes the hands for `holder`, or gives them back. Each panel holds and lets go on its own.
        public void Hold(object holder, bool held)
        {
            if (held) holders.Add(holder);
            else holders.Remove(holder);
        }
    }
}
