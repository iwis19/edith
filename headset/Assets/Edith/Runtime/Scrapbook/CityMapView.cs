using System.Collections.Generic;
using Edith.Look;
using UnityEngine;
using UnityEngine.UI;

namespace Edith.Scrapbook
{
    /// The street map of the city the Places lens has dived into: a round map lying like a tabletop tipped toward the
    /// eye, with a pin standing on each spot where memories were collected, labelled with the spot and its count.
    public sealed class CityMapView
    {
        /// The map is this far across.
        public const float Width = ScrapbookPlacement.MapWidth;
        /// In the deck's frame, where the globe was.
        public static readonly Vector3 Centre = ScrapbookPlacement.MapCentre;
        /// The map leans back this far from upright.
        public const float Lean = ScrapbookPlacement.MapLean;

        const float Detail = ScrapbookPlacement.MapDetailScale;
        // Four canvas units per millimetre at the labels' old size, so the font is rasterised sharply enough.
        const float UnitsPerMetre = 4000f / Detail;
        const float HeadRadius = 0.006f * Detail, HeadHeight = 0.016f * Detail, StalkRadius = 0.0012f * Detail;
        // A spot beyond the map's edge is pinned on the edge.
        const float PinReach = 0.47f * Width;
        const float LabelLift = 0.002f * Detail, LabelAbove = 0.014f * Detail;
        const float CaptionBelow = 0.42f * Width;
        const float LabelHeight = 56f, LabelInset = 16f;
        const int FontSize = 24;
        // The map before its image has arrived.
        const float BareAlpha = 0.3f;
        static readonly Color Unchosen = new Color(0.04f, 0.05f, 0.07f, 0.8f);
        static readonly Color Chosen = new Color(0.97f, 0.97f, 0.99f, 0.95f);
        static readonly Color Bright = new Color(0.97f, 0.97f, 0.99f, 1f);
        static readonly Color Dark = new Color(0.04f, 0.05f, 0.07f, 1f);

        sealed class Pin
        {
            public MeshRenderer Head, Stalk;
            public Image Backing;
            public Text Label;
        }

        readonly Transform root;
        readonly MeshRenderer surface;
        readonly Mesh disc, ball;
        readonly Material material, uiMaterial;
        readonly Font font;
        readonly RectTransform labels;
        readonly CanvasGroup labelGroup;
        readonly MapCaption caption;
        readonly List<Pin> pins = new List<Pin>();
        readonly List<Vector3> heads = new List<Vector3>();
        City city;

        /// `parent` is the deck's frame and must have unit world scale.
        public CityMapView(Transform parent, Font font, Material uiMaterial, Material mapMaterial)
        {
            this.font = font;
            this.uiMaterial = uiMaterial;
            material = mapMaterial;
            disc = MapMeshes.Disc();
            ball = MapMeshes.Globe(16, 8);
            root = new GameObject("CityMap").transform;
            root.SetParent(parent, false);
            root.localPosition = Centre;
            root.localRotation = Quaternion.Euler(Lean, 0f, 0f);
            surface = MapMeshes.Add(root, "Map", disc, mapMaterial, GlobeView.SurfaceOrder);
            surface.transform.localScale = Vector3.one * Width;

            var canvas = new GameObject("Labels", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            labels = (RectTransform)canvas.transform;
            labels.SetParent(root, false);
            labels.localPosition = Vector3.back * LabelLift;
            labels.localScale = Vector3.one / UnitsPerMetre;
            canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            canvas.GetComponent<Canvas>().sortingOrder = GlobeView.CaptionOrder;
            labelGroup = canvas.GetComponent<CanvasGroup>();
            labelGroup.interactable = false;
            labelGroup.blocksRaycasts = false;
            caption = new MapCaption(root, font, uiMaterial, new Vector3(0f, -CaptionBelow, -LabelLift), Quaternion.identity);
            Hide();
        }

        public bool Shown => root.gameObject.activeSelf;

        /// Lays out the pins of `city`.
        public void Show(City city)
        {
            this.city = city;
            while (pins.Count < city.Spots.Count) pins.Add(NewPin());
            for (var i = 0; i < pins.Count; i++)
            {
                var used = i < city.Spots.Count;
                var pin = pins[i];
                pin.Head.gameObject.SetActive(used);
                pin.Stalk.gameObject.SetActive(used);
                pin.Backing.gameObject.SetActive(used);
                if (!used) continue;
                var spot = city.Spots[i];
                var at = Vector2.ClampMagnitude(OnDisc(city.OnMap(spot)), PinReach);
                pin.Head.transform.localPosition = new Vector3(at.x, at.y, -HeadHeight);
                pin.Stalk.transform.localPosition = new Vector3(at.x, at.y, -HeadHeight * 0.5f);
                pin.Label.text = $"{spot.Label} {spot.Count}";
                var size = new Vector2(pin.Label.preferredWidth + 2f * LabelInset, LabelHeight);
                pin.Backing.rectTransform.sizeDelta = size;
                pin.Label.rectTransform.sizeDelta = size;
                pin.Backing.rectTransform.anchoredPosition = (at + Vector2.up * LabelAbove) * UnitsPerMetre + Vector2.up * LabelHeight * 0.5f;
            }
        }

        /// Where a pixel of the map image is on the map, in metres from its centre, x east and y north.
        public static Vector2 OnDisc(Vector2 pixel)
        {
            const float half = MercatorMath.ImagePixels * 0.5f;
            return new Vector2(pixel.x - half, half - pixel.y) * (Width / MercatorMath.ImagePixels);
        }

        /// Draws the map `scale` times its size. `area` is the city's street map image, or null while there is none,
        /// `selected` the spot the deck is narrowed to, and `note` the line along the map's lower edge.
        public void Draw(float scale, float alpha, Texture area, Spot selected, string note)
        {
            var shown = alpha > 0f && city != null;
            root.gameObject.SetActive(shown);
            heads.Clear();
            if (!shown) return;
            root.localScale = Vector3.one * scale;
            MapMeshes.Paint(surface, area, new Color(1f, 1f, 1f, area != null ? alpha : alpha * BareAlpha));
            labelGroup.alpha = alpha;
            caption.Show(note, alpha);
            for (var i = 0; i < city.Spots.Count; i++)
            {
                var spot = city.Spots[i];
                var pin = pins[i];
                var tint = spot.Best is Rarity tier ? RarityColors.For(tier) : Bright;
                tint.a = alpha;
                MapMeshes.Paint(pin.Head, null, tint);
                MapMeshes.Paint(pin.Stalk, null, new Color(Bright.r, Bright.g, Bright.b, alpha));
                pin.Backing.color = spot == selected ? Chosen : Unchosen;
                pin.Label.color = spot == selected ? Dark : Bright;
                heads.Add(pin.Head.transform.position);
            }
        }

        public void Hide()
        {
            root.gameObject.SetActive(false);
            heads.Clear();
        }

        /// The spot whose pin, as last drawn, the line from `eye` through `through` passes.
        public Spot HitPin(Vector3 eye, Vector3 through)
        {
            var at = PinchRoute.NearestPin(eye, through, heads);
            return at < 0 ? null : city.Spots[at];
        }

        public void Release()
        {
            Object.Destroy(disc);
            Object.Destroy(ball);
        }

        Pin NewPin()
        {
            var pin = new Pin
            {
                Head = MapMeshes.Add(root, "Pin", ball, material, GlobeView.PinOrder),
                Stalk = MapMeshes.Add(root, "Stalk", ball, material, GlobeView.PinOrder),
                Backing = new GameObject("Label", typeof(RectTransform), typeof(Image)).GetComponent<Image>(),
                Label = new GameObject("Text", typeof(RectTransform), typeof(Text)).GetComponent<Text>(),
            };
            pin.Head.transform.localScale = Vector3.one * HeadRadius;
            pin.Stalk.transform.localScale = new Vector3(StalkRadius, StalkRadius, HeadHeight * 0.5f);
            pin.Backing.raycastTarget = false;
            pin.Backing.material = uiMaterial;
            pin.Backing.rectTransform.SetParent(labels, false);
            pin.Label.font = font;
            pin.Label.fontSize = FontSize;
            pin.Label.alignment = TextAnchor.MiddleCenter;
            pin.Label.horizontalOverflow = HorizontalWrapMode.Overflow;
            pin.Label.verticalOverflow = VerticalWrapMode.Overflow;
            pin.Label.raycastTarget = false;
            pin.Label.material = uiMaterial;
            pin.Label.rectTransform.SetParent(pin.Backing.rectTransform, false);
            return pin;
        }
    }
}
