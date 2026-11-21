using System.Collections.Generic;
using UnityEngine;

namespace Edith.Scrapbook
{
    public enum DragTarget { Deck, Globe }

    /// Which part of the Scrapbook a pinch acts on, judged along the line from the eye through the pinch point.
    public static class PinchRoute
    {
        /// A pinch this many degrees off a pin still chooses it. The globe's cities and the map's spots can sit this
        /// close, and the nearest one wins.
        public const float PinDegrees = 6f;
        // A pin this close to the globe's edge is seen too side-on to pick out.
        const float EdgeCosine = 0.15f;

        /// What a pinch-drag that started at `start` moves: the globe when it can spin and the line from `eye`
        /// through `start` crosses it, and the deck otherwise.
        public static DragTarget DragStart(bool globeSpins, Vector3 eye, Vector3 start, Vector3 globeCentre, float globeRadius) =>
            globeSpins && HitsSphere(eye, start, globeCentre, globeRadius) ? DragTarget.Globe : DragTarget.Deck;

        public static bool HitsSphere(Vector3 eye, Vector3 through, Vector3 centre, float radius)
        {
            var direction = (through - eye).normalized;
            var toCentre = centre - eye;
            var along = Vector3.Dot(toCentre, direction);
            return along > 0f && toCentre.sqrMagnitude - along * along <= radius * radius;
        }

        /// True when a pin at `pin` on a globe centred at `centre` is on the side `eye` sees.
        public static bool FacesEye(Vector3 centre, Vector3 pin, Vector3 eye) =>
            Vector3.Dot((pin - centre).normalized, (eye - pin).normalized) > EdgeCosine;

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
