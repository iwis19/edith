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
        /// The side of a map image from GET /map/world and GET /map/area, in pixels.
        public const int ImagePixels = 1024;
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
        /// top left corner.
        public static Vector2 OnImage(GeoPoint point, GeoPoint centre, int zoom)
        {
            var (x, y) = Pixel(point, zoom);
            var (cx, cy) = Pixel(centre, zoom);
            return new Vector2((float)(x - cx + ImagePixels / 2), (float)(y - cy + ImagePixels / 2));
        }

        /// The largest city zoom at which every one of `points` is within `radiusPixels` of `centre`, or the smallest
        /// city zoom when they are too spread out for any.
        public static int ZoomToFit(GeoPoint centre, IReadOnlyList<GeoPoint> points, double radiusPixels)
        {
            for (var zoom = MaxCityZoom; zoom > MinCityZoom; zoom--)
            {
                var (cx, cy) = Pixel(centre, zoom);
                var fits = true;
                foreach (var point in points)
                {
                    var (x, y) = Pixel(point, zoom);
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) > radiusPixels * radiusPixels) fits = false;
                }
                if (fits) return zoom;
            }
            return MinCityZoom;
        }
    }
}
