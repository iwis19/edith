using UnityEngine;

namespace Edith.Hands
{
    /// Says when a panel has the hands to itself. While Held, pinches and hand poses belong to the panel that set
    /// it, and Look and Scan leave them alone.
    public sealed class HandGate : MonoBehaviour
    {
        public bool Held { get; set; }
    }
}
