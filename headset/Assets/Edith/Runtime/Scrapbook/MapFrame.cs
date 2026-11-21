using System.Collections.Generic;
using UnityEngine;

namespace Edith.Scrapbook
{
    /// What one GET /map/area image shows: the image is centred on `Centre` at tile zoom `Zoom`.
    public readonly struct MapFrame
    {
        /// Points are kept within this share of the visible crop's half size, so a pin and its label stay clear of the edge.
        public const float Margin = 0.8f;
        /// The map panel shows the full width of the image and this share of its height.
        static readonly Vector2 VisibleHalfPixels = new Vector2(1f, ScrapbookPlacement.MapVisibleShare) * (MercatorMath.ImagePixels * 0.5f);

        /// Already rounded the way the backend rounds it.
        public readonly GeoPoint Centre;
        public readonly int Zoom;

        public MapFrame(GeoPoint centre, int zoom)
        {
            Centre = centre.Rounded();
            Zoom = zoom;
        }

        /// The frame centred on `centre` at the largest zoom from `minZoom` to `maxZoom` that holds every one of
        /// `points` inside the visible crop, with a margin.
        public static MapFrame Fit(GeoPoint centre, IReadOnlyList<GeoPoint> points, int minZoom, int maxZoom) =>
            new MapFrame(centre, MercatorMath.ZoomToFit(centre.Rounded(), points, VisibleHalfPixels * Margin, minZoom, maxZoom));

        /// The pixel of `point` on the image, x right and y down from its top left corner.
        public Vector2 OnImage(GeoPoint point) => MercatorMath.OnImage(point, Centre, Zoom);
    }
}
