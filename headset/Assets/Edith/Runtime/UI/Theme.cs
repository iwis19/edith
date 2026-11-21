using System;
using UnityEngine;
using UnityEngine.UI;

namespace Edith.UI
{
    /// The look of EDITH's headset UI: deep-teal liquid glass with a cyan accent, Jost type and the pixel-art EDITH.
    /// Everything is drawn from a handful of baked sprites, so panels of any size share them.
    public static class Theme
    {
        public static readonly Color Cyan = new Color32(110, 225, 255, 255);
        /// Body text: a white with a trace of ice.
        public static readonly Color Ice = new Color32(229, 250, 254, 255);
        public static readonly Color Muted = new Color32(178, 214, 224, 255);
        public static readonly Color PanelFill = new Color(0.03f, 0.15f, 0.15f, 0.62f);
        public static readonly Color ChipFill = new Color(0.04f, 0.2f, 0.22f, 0.42f);
        public static readonly Color DiscFill = new Color32(5, 16, 22, 240);
        public static readonly Color TextShadow = new Color(0f, 0.06f, 0.09f, 0.8f);
        public static readonly Color TextGlow = new Color(0.43f, 0.88f, 1f, 0.28f);
        /// Dark text on a cyan fill.
        public static readonly Color OnCyan = new Color32(4, 28, 40, 255);

        /// Corner radius and glow spread, in pixels, of the baked rounded shapes. A shape drawn at radius r units
        /// scales every pixel by r / ShapeRadius.
        public const int ShapeRadius = 48;
        public const int ShapePad = 40;

        const string FontFolder = "EdithUI/";

        static Font regular, semiBold;
        static Texture2D head, rider;
        static Sprite fill, edge, glow, inner;

        /// Jost Regular, or `fallback` (else Unity's built-in font) when the font is not in the build.
        public static Font Regular(Font fallback = null) => Pick(ref regular, "Jost-Regular", fallback);

        /// Jost SemiBold, used where the old UI used bold.
        public static Font SemiBold(Font fallback = null) => Pick(ref semiBold, "Jost-SemiBold", fallback);

        static Font Pick(ref Font cache, string file, Font fallback)
        {
            if (cache == null) cache = Resources.Load<Font>(FontFolder + file);
            if (cache != null) return cache;
            return fallback != null ? fallback : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        /// EDITH's head in pixel art, 21 by 17 pixels, or null when the texture is not in the build.
        public static Texture2D Head
        {
            get
            {
                if (head == null) head = LoadPixelArt("EdithHead");
                return head;
            }
        }

        /// EDITH on her horse, 64 by 52 pixels, or null when the texture is not in the build.
        public static Texture2D Rider
        {
            get
            {
                if (rider == null) rider = LoadPixelArt("EdithRider");
                return rider;
            }
        }

        static Texture2D LoadPixelArt(string file)
        {
            var texture = Resources.Load<Texture2D>(FontFolder + file);
            if (texture == null) return null;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
        }

        public static Sprite Fill
        {
            get
            {
                if (fill == null) fill = Bake("EDITH glass fill", 0, d => Mathf.Clamp01(0.5f - d));
                return fill;
            }
        }

        /// A thin ring on the shape's outline, three pixels wide.
        public static Sprite Edge
        {
            get
            {
                if (edge == null) edge = Bake("EDITH glass edge", 0, d => Mathf.Clamp01(0.5f - d) * Mathf.Clamp01(d + 3.5f));
                return edge;
            }
        }

        /// A soft halo outside the shape, empty inside it.
        public static Sprite Glow
        {
            get
            {
                if (glow == null)
                    glow = Bake("EDITH glass glow", ShapePad, d =>
                    {
                        if (d <= -0.5f) return 0f;
                        var x = Mathf.Max(d, 0f) / (ShapePad * 0.42f);
                        return Mathf.Clamp01(d + 0.5f) * Mathf.Exp(-x * x);
                    });
                return glow;
            }
        }

        /// A faint light that fades inward from the outline, which gives the glass its lit rim.
        public static Sprite Inner
        {
            get
            {
                if (inner == null)
                    inner = Bake("EDITH glass inner", 0, d =>
                    {
                        var t = Mathf.Clamp01(1f + d / 22f);
                        return t * t * Mathf.Clamp01(0.5f - d);
                    });
                return inner;
            }
        }

        /// A white rounded square, `pad` pixels of margin around it, as a nine-slice sprite whose alpha is
        /// `alpha(signed distance to the outline in pixels)`, negative inside.
        static Sprite Bake(string name, int pad, Func<float, float> alpha)
        {
            var size = (ShapeRadius + pad) * 2 + 4;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color32[size * size];
            var centre = size * 0.5f;
            var core = centre - pad - 2 - ShapeRadius;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var qx = Mathf.Abs(x + 0.5f - centre) - core;
                var qy = Mathf.Abs(y + 0.5f - centre) - core;
                var outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
                var d = outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - ShapeRadius;
                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha(d)));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            var border = ShapeRadius + pad + 2;
            // 100 pixels per unit matches the canvas's reference, so a sliced border is drawn at `1 / multiplier` units a pixel.
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        /// How many times its size text is rasterised at on a world-space canvas, so it stays sharp in the headset.
        public const float TextSharpness = 3f;

        /// Makes the world-space canvas on `canvas` rasterise its text at TextSharpness times its size. Layout and
        /// sprites are unchanged.
        public static void Sharpen(GameObject canvas)
        {
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = canvas.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = TextSharpness;
        }

        /// A text label with a soft shadow and a faint cyan glow so it stays readable over any room.
        public static Text Label(Transform parent, string name, Font font, int size, Color color, TextAnchor anchor,
            Material material, bool shadow = true)
        {
            var text = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(parent, false);
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.material = material;
            if (shadow) AddShadows(text, size);
            return text;
        }

        /// The glow first and the dark drop shadow over it.
        public static void AddShadows(Text text, int size)
        {
            var spread = Mathf.Max(1f, size * 0.035f);
            var halo = text.gameObject.AddComponent<UnityEngine.UI.Outline>();
            halo.effectColor = TextGlow;
            halo.effectDistance = new Vector2(spread * 1.8f, spread * 1.8f);
            var drop = text.gameObject.AddComponent<UnityEngine.UI.Shadow>();
            drop.effectColor = TextShadow;
            drop.effectDistance = new Vector2(spread, -spread);
        }

        /// A pixel-art picture that stays crisp: Point filtering, nothing smoothed. Hidden when the texture is missing.
        public static RawImage Pixels(Transform parent, string name, Texture2D texture, Material material)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            image.transform.SetParent(parent, false);
            image.texture = texture;
            image.enabled = texture != null;
            image.raycastTarget = false;
            image.material = material;
            return image;
        }
    }

    /// A liquid-glass shape: a halo, a translucent teal body, a lit inner rim and a thin cyan outline. Its sprites
    /// are shared, so a panel, a chip and a circle (a square with radius half its side) are all the same four images.
    public sealed class Glass
    {
        public readonly RectTransform Rect;
        readonly Image glow, body, rim, outline;
        readonly float haloUnits;

        Glass(RectTransform rect, float haloUnits, Image glow, Image body, Image rim, Image outline)
        {
            Rect = rect;
            this.haloUnits = haloUnits;
            this.glow = glow;
            this.body = body;
            this.rim = rim;
            this.outline = outline;
        }

        /// A shape `size` canvas units across with corners of `radius` units, centred on its own position. A radius of
        /// half the shorter side makes a pill, or a circle for a square.
        public static Glass Build(Transform parent, string name, Vector2 size, float radius, Material material)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = size;

            var unitsPerPixel = radius / Theme.ShapeRadius;
            var halo = Theme.ShapePad * unitsPerPixel;
            var glow = Layer(rect, "Glow", Theme.Glow, size + Vector2.one * (2f * halo), unitsPerPixel, material);
            var body = Layer(rect, "Body", Theme.Fill, size, unitsPerPixel, material);
            var rim = Layer(rect, "Rim", Theme.Inner, size, unitsPerPixel, material);
            var outline = Layer(rect, "Border", Theme.Edge, size, unitsPerPixel, material);
            var glass = new Glass(rect, halo, glow, body, rim, outline);
            glass.SetLook(Theme.PanelFill, new Color(1f, 1f, 1f, 0.16f), Theme.Cyan, new Color(Theme.Cyan.r, Theme.Cyan.g, Theme.Cyan.b, 0.32f));
            return glass;
        }

        static Image Layer(RectTransform parent, string name, Sprite sprite, Vector2 size, float unitsPerPixel, Material material)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1f / unitsPerPixel;
            image.raycastTarget = false;
            image.material = material;
            var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
            return image;
        }

        /// Recolours every layer. A fully transparent glow turns the halo off.
        public void SetLook(Color bodyColor, Color rimColor, Color outlineColor, Color glowColor)
        {
            body.color = bodyColor;
            rim.color = rimColor;
            outline.color = outlineColor;
            glow.color = glowColor;
            glow.enabled = glowColor.a > 0f;
        }

        /// Changes the shape's size, keeping its corners.
        public void Resize(Vector2 size)
        {
            Rect.sizeDelta = size;
            glow.rectTransform.sizeDelta = size + Vector2.one * (2f * haloUnits);
            body.rectTransform.sizeDelta = size;
            rim.rectTransform.sizeDelta = size;
            outline.rectTransform.sizeDelta = size;
        }

        public void SetOutline(Color color) => outline.color = color;
        public void SetGlow(Color color)
        {
            glow.color = color;
            glow.enabled = color.a > 0f;
        }

        public void SetBody(Color color) => body.color = color;
    }
}
