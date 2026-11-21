using System.Collections.Generic;
using Edith.Look;
using UnityEngine;
using UnityEngine.UI;

namespace Edith.Scrapbook
{
    /// The flat map of the Places lens: a landscape panel under the lens bar, facing the eye and leaned back, drawn as a
    /// world-space canvas like the cards. It shows the middle of a square map image, and puts a dot and a label on
    /// each place, the dot tinted by the place's best rarity.
    public sealed class MapPanel
    {
        public const float Width = ScrapbookPlacement.MapWidth, Height = ScrapbookPlacement.MapHeight;

        // The canvas is laid out at four units per millimetre, so the font is rasterised sharply enough.
        const float UnitsPerMetre = 4000f;
        const float DotSize = 44f, DotFill = 30f, LabelHeight = 60f, LabelInset = 16f, LabelGap = 6f, NoteHeight = 40f;
        const int LabelFontSize = 28, NoteFontSize = 22;
        // A dot is kept this far inside the panel's edges, and clear of the note along the bottom one.
        const float EdgeMetres = 0.012f, NoteMetres = NoteHeight / UnitsPerMetre + 0.006f;
        // The map before its image has arrived.
        static readonly Color Bare = new Color(0.12f, 0.14f, 0.18f, 0.8f);
        static readonly Color Unchosen = new Color(0.04f, 0.05f, 0.07f, 0.8f);
        static readonly Color Chosen = new Color(0.97f, 0.97f, 0.99f, 0.95f);
        static readonly Color Bright = new Color(0.97f, 0.97f, 0.99f, 1f);
        static readonly Color Dark = new Color(0.04f, 0.05f, 0.07f, 1f);
        static readonly Color Quiet = new Color(0.9f, 0.91f, 0.94f, 0.9f);
        static readonly Color Strip = new Color(0.04f, 0.05f, 0.07f, 0.55f);
        static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.85f);

        sealed class Pin
        {
            public Image Dot, Fill, Backing;
            public Text Label;
        }

        readonly RectTransform root;
        readonly CanvasGroup group;
        readonly RawImage map;
        readonly GameObject strip;
        readonly Text note;
        readonly Font font;
        readonly Material material;
        readonly List<Pin> pins = new List<Pin>();
        readonly List<Vector3> dots = new List<Vector3>();
        IReadOnlyList<Place> places = System.Array.Empty<Place>();

        /// `parent` is the deck's frame and must have unit world scale.
        public MapPanel(Transform parent, Font font, Material material)
        {
            this.font = font;
            this.material = material;
            var go = new GameObject("Map", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            root = (RectTransform)go.transform;
            root.SetParent(parent, false);
            root.sizeDelta = new Vector2(Width, Height) * UnitsPerMetre;
            root.localPosition = ScrapbookPlacement.MapCentre;
            root.localRotation = ScrapbookPlacement.MapRotation;
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            group = go.GetComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            map = new GameObject("Image", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            map.rectTransform.SetParent(root, false);
            Stretch(map.rectTransform);
            map.raycastTarget = false;
            map.material = material;
            var share = ScrapbookPlacement.MapVisibleShare;
            map.uvRect = new Rect(0f, (1f - share) * 0.5f, 1f, share);

            strip = new GameObject("Note", typeof(RectTransform), typeof(Image));
            var backing = strip.GetComponent<Image>();
            backing.raycastTarget = false;
            backing.material = material;
            backing.color = Strip;
            var rect = backing.rectTransform;
            rect.SetParent(root, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(0f, NoteHeight);
            rect.anchoredPosition = Vector2.zero;
            note = NewText(rect, NoteFontSize, Quiet);
            Stretch(note.rectTransform);
            var shadow = note.gameObject.AddComponent<Shadow>();
            shadow.effectColor = ShadowColor;
            shadow.effectDistance = new Vector2(1f, -1f);
            Hide();
        }

        /// Where a pixel of a map image is on the panel, in metres from its middle, x east and y north.
        public static Vector2 OnPanel(Vector2 pixel)
        {
            const float half = MercatorMath.ImagePixels * 0.5f;
            return new Vector2(pixel.x - half, half - pixel.y) * (Width / MercatorMath.ImagePixels);
        }

        /// Lays out a dot and a label for each of `places`, positioned on the image `frame` describes. A place beyond
        /// the visible crop is pinned at its edge.
        public void Show(MapFrame frame, IReadOnlyList<Place> places)
        {
            this.places = places;
            while (pins.Count < places.Count) pins.Add(NewPin());
            const float halfWidth = Width * 0.5f * UnitsPerMetre, halfHeight = Height * 0.5f * UnitsPerMetre;
            for (var i = 0; i < pins.Count; i++)
            {
                var used = i < places.Count;
                var pin = pins[i];
                pin.Dot.gameObject.SetActive(used);
                pin.Backing.gameObject.SetActive(used);
                if (!used) continue;
                var at = OnPanel(frame.OnImage(places[i].Centre));
                at.x = Mathf.Clamp(at.x, -Width * 0.5f + EdgeMetres, Width * 0.5f - EdgeMetres);
                at.y = Mathf.Clamp(at.y, -Height * 0.5f + NoteMetres, Height * 0.5f - EdgeMetres);
                var dot = at * UnitsPerMetre;
                pin.Dot.rectTransform.anchoredPosition = dot;

                pin.Label.text = $"{places[i].Label} {places[i].Count}";
                var size = new Vector2(pin.Label.preferredWidth + 2f * LabelInset, LabelHeight);
                pin.Backing.rectTransform.sizeDelta = size;
                var rise = DotSize * 0.5f + LabelGap + LabelHeight * 0.5f;
                // A label that would pass the top edge goes under its dot instead.
                var y = dot.y + rise + LabelHeight * 0.5f > halfHeight ? dot.y - rise : dot.y + rise;
                pin.Backing.rectTransform.anchoredPosition = new Vector2(Mathf.Clamp(dot.x, -halfWidth + size.x * 0.5f, halfWidth - size.x * 0.5f), y);
            }
        }

        /// Draws the panel `scale` times its size, `alpha` opaque. `image` is the map image, or null while there is
        /// none, `selected` the place whose label is lit, and `line` the note along the bottom edge.
        public void Draw(float scale, float alpha, Texture image, Place selected, string line)
        {
            var shown = alpha > 0f && places.Count > 0;
            root.gameObject.SetActive(shown);
            dots.Clear();
            if (!shown) return;
            root.localScale = Vector3.one * (scale / UnitsPerMetre);
            group.alpha = alpha;
            map.texture = image;
            map.color = image != null ? Color.white : Bare;
            strip.SetActive(!string.IsNullOrEmpty(line));
            note.text = line ?? "";
            for (var i = 0; i < places.Count; i++)
            {
                var pin = pins[i];
                pin.Fill.color = places[i].Best is Rarity tier ? RarityColors.For(tier) : Bright;
                var chosen = places[i] == selected;
                pin.Backing.color = chosen ? Chosen : Unchosen;
                pin.Label.color = chosen ? Dark : Bright;
                dots.Add(pin.Dot.rectTransform.position);
            }
        }

        public void Hide()
        {
            root.gameObject.SetActive(false);
            dots.Clear();
        }

        /// The place whose dot, as last drawn, the line from `eye` through `through` passes nearest, within
        /// PinchRoute.PinDegrees, and how far off the line it is.
        public Place HitPin(Vector3 eye, Vector3 through, out float degrees)
        {
            var at = PinchRoute.NearestPin(eye, through, dots, out degrees);
            return at < 0 ? null : places[at];
        }

        Pin NewPin()
        {
            var pin = new Pin { Dot = NewImage("Dot", root, Bright), Backing = NewImage("Label", root, Unchosen) };
            pin.Dot.rectTransform.sizeDelta = new Vector2(DotSize, DotSize);
            pin.Fill = NewImage("Fill", pin.Dot.rectTransform, Bright);
            pin.Fill.rectTransform.sizeDelta = new Vector2(DotFill, DotFill);
            pin.Label = NewText(pin.Backing.rectTransform, LabelFontSize, Bright);
            Stretch(pin.Label.rectTransform);
            return pin;
        }

        Image NewImage(string name, Transform parent, Color color)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.rectTransform.SetParent(parent, false);
            image.raycastTarget = false;
            image.material = material;
            image.color = color;
            return image;
        }

        Text NewText(Transform parent, int size, Color color)
        {
            var text = new GameObject("Text", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.rectTransform.SetParent(parent, false);
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.material = material;
            return text;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
