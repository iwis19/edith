using System.Threading;
using UnityEngine;

namespace Edith.Scrapbook
{
    /// What the Places lens draws under the lens bar: the map panel at the world level, with a pin for each city, or
    /// at the city level, with a pin for each spot, and the quick zoom between them. It follows the Browse it is
    /// drawn for.
    public sealed class PlacesView
    {
        public const string Attribution = "© OpenStreetMap contributors";
        /// How long the panel takes to change from one level to the other.
        public const float ZoomSeconds = 0.35f;
        const string Failed = "Couldn't load the map";
        const float LensSeconds = 0.2f;
        // The old level grows or shrinks this much as it fades out, and the new one settles from the opposite size.
        const float ZoomScale = 0.15f;

        readonly MapPanel panel;
        readonly MapImages images;
        Atlas atlas;
        /// The city the panel shows, or null for the world level, and the one it is changing to.
        City shown, target;
        MapFrame frame;
        float lens = 1f, zoom = 1f;
        /// 1 when zooming in to a city, -1 when zooming out.
        int direction;

        /// `frame` is the deck's frame and must have unit world scale.
        public PlacesView(Transform frame, Font font, Material uiMaterial, MapImages images)
        {
            this.images = images;
            panel = new MapPanel(frame, font, uiMaterial);
        }

        public void Hide() => panel.Hide();

        /// Draws the Places lens of `browse`, faded by `fade`. Map images still missing are asked of `client`, which
        /// may be null, for as long as `session` is not cancelled.
        public void Draw(Browse browse, float fade, float deltaTime, ScrapbookClient client, CancellationTokenSource session)
        {
            if (browse.Atlas != atlas)
            {
                atlas = browse.Atlas;
                lens = browse.Lens == Lens.Places ? 1f : 0f;
                zoom = 1f;
                target = browse.City;
                Lay(target);
            }
            else if (browse.City != target)
            {
                target = browse.City;
                zoom = 0f;
                direction = target != null ? 1 : -1;
            }

            lens = Mathf.MoveTowards(lens, browse.Lens == Lens.Places ? 1f : 0f, deltaTime / LensSeconds);
            if (zoom < 1f)
            {
                zoom = Mathf.Min(1f, zoom + deltaTime / ZoomSeconds);
                if (zoom >= 0.5f && shown != target) Lay(target);
            }

            var seen = fade * lens;
            if (seen <= 0f || atlas.Cities.Count == 0)
            {
                Hide();
                return;
            }
            var (alpha, scale) = Zoomed();
            var image = client != null ? images.AreaImage(frame, client, session) : null;
            // The level being zoomed to is fetched while the old one fades out.
            if (client != null && shown != target) images.AreaImage(Framing(target), client, session);
            panel.Draw(scale, seen * alpha, image, shown != null ? browse.Spot : null,
                image != null ? Attribution : images.AreaFailed(frame) ? Failed : null);
        }

        /// The city or the spot whose pin the line from `eye` through `through` passes nearest, or null, and how many
        /// degrees off the line it is. Pins take no pinch while the panel zooms or the lens is out.
        public Place HitPin(Vector3 eye, Vector3 through, out float degrees)
        {
            degrees = float.MaxValue;
            return lens < 1f || zoom < 1f ? null : panel.HitPin(eye, through, out degrees);
        }

        /// How opaque the panel is and how large, through a zoom: the old level fades out growing or shrinking, then the
        /// new one fades in settling to its size.
        (float alpha, float scale) Zoomed()
        {
            if (zoom >= 1f) return (1f, 1f);
            if (zoom < 0.5f)
            {
                var leaving = Smooth(zoom * 2f);
                return (1f - leaving, 1f + direction * ZoomScale * leaving);
            }
            var arriving = Smooth(zoom * 2f - 1f);
            return (arriving, 1f - direction * ZoomScale * (1f - arriving));
        }

        void Lay(City city)
        {
            shown = city;
            frame = Framing(city);
            if (city != null) panel.Show(frame, city.Spots);
            else panel.Show(frame, atlas.Cities);
        }

        MapFrame Framing(City city) => city != null ? city.Frame : atlas.World;

        static float Smooth(float t) => t * t * (3f - 2f * t);
    }
}
