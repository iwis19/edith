using System.Threading;
using UnityEngine;

namespace Edith.Scrapbook
{
    /// What the Places lens draws under the lens bar: the globe, the street map of the city dived into, and the dive
    /// between them. It follows the Browse it is drawn for.
    public sealed class PlacesView
    {
        public const string Attribution = "© OpenStreetMap contributors";
        const string Failed = "Couldn't load the map";
        const float LensSeconds = 0.2f;

        readonly GlobeView globe;
        readonly CityMapView map;
        readonly MapImages images;
        Atlas atlas;
        /// The city the street map shows: the one dived into, or the one being left.
        City city;
        GlobeSpin spin, diveFrom;
        float lens, dive;
        bool surfacing;

        /// `frame` is the deck's frame and must have unit world scale.
        public PlacesView(Transform frame, Font font, Material uiMaterial, Material mapMaterial, MapImages images)
        {
            this.images = images;
            globe = new GlobeView(frame, font, uiMaterial, mapMaterial);
            map = new CityMapView(frame, font, uiMaterial, mapMaterial);
        }

        /// The globe's centre in the room.
        public Vector3 GlobePosition => globe.Position;

        /// True while a pinch-drag on the globe would turn it: the Places lens is at the globe, not diving.
        public bool GlobeSpins => globe.Shown && lens == 1f && dive == 0f;

        public GlobeSpin Spin => spin;

        public void Hide()
        {
            globe.Hide();
            map.Hide();
        }

        /// Draws the Places lens of `browse`, faded by `fade`, for an eye at `eye`. Map images still missing are
        /// asked of `client`, which may be null, for as long as `session` is not cancelled.
        public void Draw(Browse browse, float fade, Vector3 eye, float deltaTime, ScrapbookClient client, CancellationTokenSource session)
        {
            if (browse.Atlas != atlas)
            {
                atlas = browse.Atlas;
                globe.Show(atlas.Cities);
                city = null;
                dive = 0f;
                surfacing = false;
                lens = browse.Lens == Lens.Places ? 1f : 0f;
                // The newest city faces the eye.
                spin = atlas.Cities.Count > 0 ? GlobeSpin.Facing(atlas.Cities[0].Centre, GlobeView.EyeElevation) : default;
            }

            if (browse.City != null && (browse.City != city || surfacing))
            {
                if (browse.City != city) map.Show(browse.City);
                city = browse.City;
                diveFrom = dive == 0f ? spin : GlobeSpin.Facing(city.Centre, GlobeView.EyeElevation);
                surfacing = false;
            }
            else if (browse.City == null && dive > 0f && !surfacing)
            {
                surfacing = true;
                // The globe comes back with the city it dived into still facing the eye.
                diveFrom = GlobeSpin.Facing(city.Centre, GlobeView.EyeElevation);
            }

            lens = Mathf.MoveTowards(lens, browse.Lens == Lens.Places ? 1f : 0f, deltaTime / LensSeconds);
            dive = Mathf.MoveTowards(dive, browse.City != null ? 1f : 0f, deltaTime / DivePose.Seconds);
            if (dive == 0f && surfacing)
            {
                surfacing = false;
                spin = diveFrom;
                city = null;
            }

            var shown = fade * lens;
            if (shown <= 0f)
            {
                Hide();
                return;
            }
            var pose = DivePose.At(dive);
            var world = client != null ? images.WorldImage(client, session) : null;
            globe.Draw(
                dive > 0f ? GlobeSpin.Lerp(diveFrom, GlobeSpin.Facing(city.Centre, GlobeView.EyeElevation), pose.Turn) : spin,
                pose.GlobeScale, shown * pose.GlobeAlpha, world, Note(world != null, images.WorldFailed), eye);
            if (dive == 0f)
            {
                map.Hide();
                return;
            }
            var area = client != null ? images.AreaImage(city, client, session) : null;
            map.Draw(pose.MapScale, shown * pose.MapAlpha, area, browse.Spot, Note(area != null, images.AreaFailed(city)));
        }

        /// Turns the globe with a hand that has moved by `metres` in the deck's frame.
        public void Drag(Vector2 metres) => spin = spin.Dragged(metres, GlobeView.Radius);

        /// The city or the spot whose pin the line from `eye` through `through` passes, or null. Pins take no pinch
        /// while the dive is under way.
        public Place HitPin(Vector3 eye, Vector3 through)
        {
            if (lens < 1f) return null;
            if (dive == 0f) return globe.HitPin(eye, through);
            return dive == 1f ? map.HitPin(eye, through) : null;
        }

        public void Release()
        {
            globe.Release();
            map.Release();
        }

        static string Note(bool loaded, bool failed) => loaded ? Attribution : failed ? Failed : null;
    }
}
