using UnityEngine;
using UnityEngine.UI;

namespace Edith.Scrapbook
{
    /// Draws the wrist button: a disc facing the eye with its name under it. It brightens while the Scrapbook is
    /// open and swells for a moment when pressed.
    public sealed class WristButtonView
    {
        // Four canvas units per millimetre, so the font is rasterised sharply enough at this size.
        const float UnitsPerMetre = 4000f;
        const int DiscPixels = 64;
        const float FadeSeconds = 0.15f, PulseSeconds = 0.25f, PulseSwell = 0.35f;
        static readonly Color Idle = new Color(0.97f, 0.97f, 0.99f, 0.55f);
        static readonly Color Open = new Color(1.00f, 0.80f, 0.20f, 0.95f);
        static readonly Color Bright = new Color(0.97f, 0.97f, 0.99f, 1f);

        readonly RectTransform root;
        readonly CanvasGroup group;
        readonly RawImage disc;
        readonly Texture2D circle;
        float pulse;

        /// `parent` must have unit world scale.
        public WristButtonView(Transform parent, Font font, Material material, float radiusMeters)
        {
            var go = new GameObject("WristButton", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            root = (RectTransform)go.transform;
            root.SetParent(parent, false);
            root.localScale = Vector3.one / UnitsPerMetre;
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            group = go.GetComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            group.alpha = 0f;

            var size = 2f * radiusMeters * UnitsPerMetre;
            circle = Circle();
            disc = new GameObject("Disc", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            disc.texture = circle;
            disc.raycastTarget = false;
            disc.material = material;
            disc.rectTransform.SetParent(root, false);
            disc.rectTransform.sizeDelta = new Vector2(size, size);

            var label = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            label.font = font;
            label.fontSize = 22;
            label.text = "Scrapbook";
            label.color = Bright;
            label.alignment = TextAnchor.UpperCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            label.material = material;
            label.rectTransform.SetParent(root, false);
            label.rectTransform.pivot = new Vector2(0.5f, 1f);
            label.rectTransform.anchoredPosition = new Vector2(0f, -size * 0.5f - 8f);
        }

        public void Show(in WristReading reading, Vector3 eye, bool open, float deltaTime)
        {
            group.alpha = Mathf.MoveTowards(group.alpha, reading.Shown ? 1f : 0f, deltaTime / FadeSeconds);
            if (reading.Pressed) pulse = 1f;
            pulse = Mathf.MoveTowards(pulse, 0f, deltaTime / PulseSeconds);
            disc.color = open ? Open : Idle;
            disc.rectTransform.localScale = Vector3.one * (1f + PulseSwell * pulse);
            if (!reading.Shown || reading.Position == eye) return;
            root.SetPositionAndRotation(reading.Position, Quaternion.LookRotation(reading.Position - eye, Vector3.up));
        }

        /// Frees the disc's texture. The button itself goes with its parent.
        public void Release() => Object.Destroy(circle);

        static Texture2D Circle()
        {
            var texture = new Texture2D(DiscPixels, DiscPixels, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[DiscPixels * DiscPixels];
            var centre = (DiscPixels - 1) * 0.5f;
            for (var y = 0; y < DiscPixels; y++)
                for (var x = 0; x < DiscPixels; x++)
                {
                    var away = Mathf.Sqrt((x - centre) * (x - centre) + (y - centre) * (y - centre));
                    // One pixel of falloff at the rim keeps the edge smooth.
                    var alpha = Mathf.Clamp01(DiscPixels * 0.5f - away);
                    pixels[y * DiscPixels + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
