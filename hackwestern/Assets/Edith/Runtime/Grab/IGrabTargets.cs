using System.Collections.Generic;
using Edith.Snapshots;

namespace Edith.Grab
{
    /// An owner of cards a fist can grab. Every owner registered with one GrabInput keeps its ids apart from the
    /// others': snapshots use ids from 0 up, answer cards ids from -1 down.
    public interface IGrabTargets
    {
        /// Adds this owner's grabbable cards to `targets`, which already holds other owners' cards.
        void AddGrabTargets(List<(int id, SightBox box)> targets);
    }
}
