using UnityEngine;

namespace Edith.Scrapbook
{
    /// How the globe is turned in the deck's frame: spun about its own axis, then that axis tipped toward or away
    /// from the eye. Degrees.
    public readonly struct GlobeSpin
    {
        public const float MaxTilt = 60f;
        // The hand is nearer the eye than the globe, so the surface has to move further than the hand to stay under
        // it.
        const float DragGain = 1.5f;

        public readonly float Yaw, Tilt;

        public GlobeSpin(float yaw, float tilt)
        {
            Yaw = Mathf.Repeat(yaw + 180f, 360f) - 180f;
            Tilt = Mathf.Clamp(tilt, -MaxTilt, MaxTilt);
        }

        public Quaternion Rotation => Quaternion.Euler(Tilt, 0f, 0f) * Quaternion.Euler(0f, Yaw, 0f);

        /// The spin that turns `point` toward an eye straight in front of the globe, `eyeElevation` degrees above its
        /// centre, as nearly as the tilt limit lets it.
        public static GlobeSpin Facing(GeoPoint point, float eyeElevation) =>
            new GlobeSpin((float)point.Lon, eyeElevation - (float)point.Lat);

        /// The spin after a hand has dragged the surface by `metres`, x to the right and y up, on a globe of `radius`.
        public GlobeSpin Dragged(Vector2 metres, float radius)
        {
            var degrees = metres * (DragGain * Mathf.Rad2Deg / radius);
            return new GlobeSpin(Yaw - degrees.x, Tilt + degrees.y);
        }

        /// Turns the short way round.
        public static GlobeSpin Lerp(GlobeSpin from, GlobeSpin to, float t) =>
            new GlobeSpin(Mathf.LerpAngle(from.Yaw, to.Yaw, t), Mathf.Lerp(from.Tilt, to.Tilt, t));
    }

    /// The dive from the globe into a city's street map, at one moment: the globe turns the city to the eye, then
    /// swells and fades while the map grows in where it was.
    public readonly struct DivePose
    {
        public const float Seconds = 0.8f;
        const float GlobeSwell = 1.6f, MapStart = 0.6f;

        /// How far the globe has turned from where it was to facing the city, 0 to 1.
        public readonly float Turn;
        public readonly float GlobeScale, GlobeAlpha, MapScale, MapAlpha;

        DivePose(float turn, float globeScale, float globeAlpha, float mapScale, float mapAlpha)
        {
            Turn = turn;
            GlobeScale = globeScale;
            GlobeAlpha = globeAlpha;
            MapScale = mapScale;
            MapAlpha = mapAlpha;
        }

        /// `progress` is 0 at the globe and 1 in the city.
        public static DivePose At(float progress)
        {
            var map = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 1f, progress));
            return new DivePose(
                Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.5f, progress)),
                Mathf.Lerp(1f, GlobeSwell, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 1f, progress))),
                1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.4f, 0.9f, progress)),
                Mathf.Lerp(MapStart, 1f, map),
                map);
        }
    }
}
