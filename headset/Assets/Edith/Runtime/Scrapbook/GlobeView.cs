using System.Collections.Generic;
using Edith.Look;
using UnityEngine;
using UnityEngine.UI;

namespace Edith.Scrapbook
{
    /// The globe of the Places lens: the world image on a sphere under the lens bar, with a pin for each city where
    /// memories were collected, tinted by the city's best rarity. A pin on the far side is hidden.
    public sealed class GlobeView
    {
        public const float Radius = ScrapbookPlacement.GlobeRadius;
        /// In the deck's frame, under the lens bar.
        public static readonly Vector3 Centre = ScrapbookPlacement.GlobeCentre;
        /// How far above the globe's centre the eye that opened the Scrapbook is, in degrees.
        public static readonly float EyeElevation = Mathf.Atan2(-Centre.y, Centre.z) * Mathf.Rad2Deg;

        /// The Scrapbook's maps are drawn before its cards and lens bar, and their pins and captions over them.
        public const int SurfaceOrder = -1000, CaptionOrder = -999, PinOrder = -998;

        const float PinRadius = 0.006f * ScrapbookPlacement.MapDetailScale;
        // The globe before its image has arrived.
        const float BareAlpha = 0.3f;
        const float CaptionGap = 0.014f * ScrapbookPlacement.MapDetailScale;
        static readonly Color Untiered = new Color(0.97f, 0.97f, 0.99f, 1f);

        readonly Transform root;
        readonly MeshRenderer surface;
        readonly Mesh sphere;
        readonly Material material;
        readonly MapCaption caption;
        readonly List<MeshRenderer> pins = new List<MeshRenderer>();
        readonly List<Vector3> seen = new List<Vector3>();
        readonly List<City> seenCities = new List<City>();
        IReadOnlyList<City> cities = System.Array.Empty<City>();

        /// `parent` is the deck's frame and must have unit world scale.
        public GlobeView(Transform parent, Font font, Material uiMaterial, Material mapMaterial)
        {
            material = mapMaterial;
            sphere = MapMeshes.Globe();
            surface = MapMeshes.Add(parent, "Globe", sphere, mapMaterial, SurfaceOrder);
            root = surface.transform;
            root.localPosition = Centre;
            var under = Centre + Vector3.down * (Radius + CaptionGap);
            caption = new MapCaption(parent, font, uiMaterial, under, Quaternion.LookRotation(under, Vector3.up));
            Hide();
        }

        /// The globe's centre in the room.
        public Vector3 Position => root.position;

        public bool Shown => root.gameObject.activeSelf;

        /// Puts a pin on each of `cities`.
        public void Show(IReadOnlyList<City> cities)
        {
            this.cities = cities;
            while (pins.Count < cities.Count)
            {
                var pin = MapMeshes.Add(root, "Pin", sphere, material, PinOrder);
                pin.transform.localScale = Vector3.one * (PinRadius / Radius);
                pins.Add(pin);
            }
            for (var i = 0; i < pins.Count; i++)
            {
                pins[i].gameObject.SetActive(i < cities.Count);
                if (i < cities.Count) pins[i].transform.localPosition = MercatorMath.Direction(cities[i].Centre);
            }
        }

        /// Draws the globe turned by `spin` for an eye at `eye`, `scale` times its size. `world` is the world image,
        /// or null while there is none, and `note` the line under the globe.
        public void Draw(GlobeSpin spin, float scale, float alpha, Texture world, string note, Vector3 eye)
        {
            root.gameObject.SetActive(alpha > 0f);
            caption.Show(note, alpha);
            seen.Clear();
            seenCities.Clear();
            if (alpha <= 0f) return;
            root.localRotation = spin.Rotation;
            root.localScale = Vector3.one * (Radius * scale);
            MapMeshes.Paint(surface, world, new Color(1f, 1f, 1f, world != null ? alpha : alpha * BareAlpha));
            for (var i = 0; i < cities.Count; i++)
            {
                var at = pins[i].transform.position;
                var faces = PinchRoute.FacesEye(root.position, at, eye);
                pins[i].enabled = faces;
                if (!faces) continue;
                var tint = cities[i].Best is Rarity tier ? RarityColors.For(tier) : Untiered;
                MapMeshes.Paint(pins[i], null, new Color(tint.r, tint.g, tint.b, alpha));
                seen.Add(at);
                seenCities.Add(cities[i]);
            }
        }

        public void Hide()
        {
            root.gameObject.SetActive(false);
            caption.Show(null, 0f);
            seen.Clear();
            seenCities.Clear();
        }

        /// The city whose pin, on the side last drawn toward the eye, the line from `eye` through `through` passes
        /// nearest, within PinchRoute.PinDegrees, and how far off the line it is.
        public City HitPin(Vector3 eye, Vector3 through, out float degrees)
        {
            var at = PinchRoute.NearestPin(eye, through, seen, out degrees);
            return at < 0 ? null : seenCities[at];
        }

        public void Release() => Object.Destroy(sphere);
    }

    /// One line of small text beside a map: who the map is from, or that it could not be loaded.
    public sealed class MapCaption
    {
        // Four canvas units per millimetre at the captions' old size, so the font is rasterised sharply enough.
        const float UnitsPerMetre = 4000f / ScrapbookPlacement.MapDetailScale;
        const int FontSize = 20;
        static readonly Color Quiet = new Color(0.9f, 0.91f, 0.94f, 0.9f);
        static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.85f);

        readonly Text text;
        readonly CanvasGroup group;

        /// `position` and `rotation` are in the frame of `parent`.
        public MapCaption(Transform parent, Font font, Material material, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject("Caption", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = Vector3.one / UnitsPerMetre;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = GlobeView.CaptionOrder;
            group = go.GetComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            text = new GameObject("Text", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.rectTransform.SetParent(go.transform, false);
            text.font = font;
            text.fontSize = FontSize;
            text.color = Quiet;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.material = material;
            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = ShadowColor;
            shadow.effectDistance = new Vector2(1f, -1f);
        }

        /// Null or empty shows nothing.
        public void Show(string line, float alpha)
        {
            text.gameObject.SetActive(alpha > 0f && !string.IsNullOrEmpty(line));
            text.text = line ?? "";
            group.alpha = alpha;
        }
    }
}
