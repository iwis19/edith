using System;
using System.Collections.Generic;
using UnityEngine;

namespace Edith.Scrapbook
{
    /// A place on Earth, in degrees.
    public readonly struct GeoPoint
    {
        const double EarthRadiusMetres = 6371008.8;

        public readonly double Lat, Lon;

        public GeoPoint(double lat, double lon)
        {
            Lat = lat;
            Lon = lon;
        }

        /// The great-circle distance.
        public double MetresTo(GeoPoint other)
        {
            const double toRadians = Math.PI / 180.0;
            var dLat = (other.Lat - Lat) * toRadians;
            var dLon = (other.Lon - Lon) * toRadians;
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(Lat * toRadians) * Math.Cos(other.Lat * toRadians) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return 2 * EarthRadiusMetres * Math.Asin(Math.Min(1.0, Math.Sqrt(a)));
        }

        /// The mean of `points`, measured from the first the short way round, so a group across longitude 180 averages there.
        public static GeoPoint Mean(IReadOnlyList<GeoPoint> points)
        {
            var first = points[0];
            double lat = 0, east = 0;
            foreach (var point in points)
            {
                lat += point.Lat;
                var d = point.Lon - first.Lon;
                east += d > 180 ? d - 360 : d < -180 ? d + 360 : d;
            }
            var lon = first.Lon + east / points.Count;
            return new GeoPoint(lat / points.Count, lon > 180 ? lon - 360 : lon < -180 ? lon + 360 : lon);
        }

        /// GET /map/area centres its image on the point rounded like this, to four decimal places.
        public GeoPoint Rounded() =>
            new GeoPoint(Math.Round(Lat, 4, MidpointRounding.AwayFromZero), Math.Round(Lon, 4, MidpointRounding.AwayFromZero));
    }

    /// Web Mercator as slippy maps use it: at tile zoom z the world is a square of 256 * 2^z pixels, x east from
    /// longitude -180 and y south from latitude +85.0511.
    public static class MercatorMath
    {
        /// The latitude where the square world ends.
        public const double MaxLatitude = 85.0511287798066;
        /// The side of a map image from GET /map/area, in pixels.
        public const int ImagePixels = 1024;
        public const int MinWorldZoom = 2, MaxWorldZoom = 12;
        public const int MinCityZoom = 12, MaxCityZoom = 16;

        public static double WorldPixels(int zoom) => 256.0 * (1 << zoom);

        /// The world pixel of `point` at `zoom`. A latitude beyond the square world lands on its edge.
        public static (double x, double y) Pixel(GeoPoint point, int zoom)
        {
            var size = WorldPixels(zoom);
            var sin = Math.Sin(Math.Max(-MaxLatitude, Math.Min(MaxLatitude, point.Lat)) * Math.PI / 180.0);
            return ((point.Lon + 180.0) / 360.0 * size, (0.5 - Math.Log((1 + sin) / (1 - sin)) / (4 * Math.PI)) * size);
        }

        public static GeoPoint Point(double x, double y, int zoom)
        {
            var size = WorldPixels(zoom);
            var n = Math.PI - 2 * Math.PI * y / size;
            return new GeoPoint(Math.Atan(Math.Sinh(n)) * 180.0 / Math.PI, x / size * 360.0 - 180.0);
        }

        /// Where `point` is on the world image as a texture coordinate: u east from longitude -180, v north from
        /// latitude -85.0511.
        public static Vector2 UV(GeoPoint point)
        {
            var (x, y) = Pixel(point, 0);
            return new Vector2((float)(x / 256.0), (float)(1.0 - y / 256.0));
        }

        /// Where `point` is on the unit globe: north is +y, and longitude 0 on the equator is -z, so that seen from
        /// outside with north up, east is to the right.
        public static Vector3 Direction(GeoPoint point)
        {
            var lat = point.Lat * Math.PI / 180.0;
            var lon = point.Lon * Math.PI / 180.0;
            return new Vector3((float)(Math.Cos(lat) * Math.Sin(lon)), (float)Math.Sin(lat), (float)(-Math.Cos(lat) * Math.Cos(lon)));
        }

        /// The pixel of `point` on the area image centred on `centre` at `zoom`, x right and y down from the image's
        /// top left corner. Longitudes are taken the short way round, as a low zoom shows the world repeating.
        public static Vector2 OnImage(GeoPoint point, GeoPoint centre, int zoom)
        {
            var (dx, dy) = From(point, centre, zoom);
            return new Vector2((float)dx + ImagePixels / 2f, (float)dy + ImagePixels / 2f);
        }

        /// The largest zoom from `minZoom` to `maxZoom` at which every one of `points` is within `half` pixels of
        /// `centre`, across and down, or `minZoom` when they are too spread out for any.
        public static int ZoomToFit(GeoPoint centre, IReadOnlyList<GeoPoint> points, Vector2 half, int minZoom, int maxZoom)
        {
            for (var zoom = maxZoom; zoom > minZoom; zoom--)
            {
                var fits = true;
                foreach (var point in points)
                {
                    var (dx, dy) = From(point, centre, zoom);
                    if (Math.Abs(dx) > half.x || Math.Abs(dy) > half.y) fits = false;
                }
                if (fits) return zoom;
            }
            return minZoom;
        }

        static (double dx, double dy) From(GeoPoint point, GeoPoint centre, int zoom)
        {
            var size = WorldPixels(zoom);
            var (x, y) = Pixel(point, zoom);
            var (cx, cy) = Pixel(centre, zoom);
            var dx = x - cx;
            return (dx > size / 2 ? dx - size : dx < -size / 2 ? dx + size : dx, y - cy);
        }
    }
}
