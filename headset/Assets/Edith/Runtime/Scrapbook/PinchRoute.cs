using System.Collections.Generic;
using UnityEngine;

namespace Edith.Scrapbook
{
    /// Which part of the Scrapbook a pinch acts on, judged along the line from the eye through the pinch point.
    public static class PinchRoute
    {
        /// A pinch this many degrees off a pin still chooses it. The map's cities and spots can sit this
        /// close, and the nearest one wins.
        public const float PinDegrees = 6f;
        /// The index of the pin nearest the line from `eye` through `through`, among those within PinDegrees of it,
        /// or -1. `degrees` is how far off the line that pin is.
        public static int NearestPin(Vector3 eye, Vector3 through, IReadOnlyList<Vector3> pins, out float degrees)
        {
            var nearest = -1;
            degrees = PinDegrees;
            for (var i = 0; i < pins.Count; i++)
            {
                var off = Vector3.Angle(through - eye, pins[i] - eye);
                if (off > degrees) continue;
                degrees = off;
                nearest = i;
            }
            return nearest;
        }
    }
}
