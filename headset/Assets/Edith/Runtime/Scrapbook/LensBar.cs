using System.Collections.Generic;
using Edith.Look;
using UnityEngine;
using UnityEngine.UI;

namespace Edith.Scrapbook
{
    /// The bar under the deck: a row of lens tabs and, under it, the row of chips the lens in use offers. It sits
    /// below the cards and leans back to face the eye.
    public sealed class LensBar
    {
        // Four canvas units per millimetre, so the font is rasterised sharply enough at this size.
        const float UnitsPerMetre = 4000f;
        // In the deck's frame: under the card's text, and a little nearer than the cards so a hand reaches it first.
        static readonly Vector3 Centre = new Vector3(0f, -0.205f, 0.42f);
        static readonly Vector2 TabSize = new Vector2(200f, 72f), ChipSize = new Vector2(176f, 72f);
        const float Gap = 16f;
        // A label wider than its chip widens the chip, keeping this much clear on each side.
        const float LabelInset = 20f;
        const int FontSize = 28;
        // A pinch this near a chip still chooses it: a fingertip is about this much wider than the chip's gap.
        const float HitMargin = 0.002f;
        static readonly Color Unchosen = new Color(0.04f, 0.05f, 0.07f, 0.8f);
        static readonly Color Chosen = new Color(0.97f, 0.97f, 0.99f, 0.95f);
        static readonly Color Bright = new Color(0.97f, 0.97f, 0.99f, 1f);
        static readonly Color Dark = new Color(0.04f, 0.05f, 0.07f, 1f);

        sealed class Pill
        {
            public Image Backing;
            public Text Label;
            public Chip Chip;
        }

        readonly RectTransform root;
        readonly CanvasGroup group;
        readonly Font font;
        readonly Material material;
        readonly List<Pill> pills = new List<Pill>();
        int shown;

        /// `parent` is the deck's frame and must have unit world scale.
        public LensBar(Transform parent, Font font, Material material)
        {
            this.font = font;
            this.material = material;
            var go = new GameObject("LensBar", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            root = (RectTransform)go.transform;
            root.SetParent(parent, false);
            root.localPosition = Centre;
            root.localRotation = Quaternion.LookRotation(Centre, Vector3.up);
            root.localScale = Vector3.one / UnitsPerMetre;
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            group = go.GetComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
        }

        public float Alpha
        {
            set => group.alpha = value;
        }

        /// The labels on the bar, tabs first, then the chips left to right.
        public IEnumerable<string> Labels
        {
            get
            {
                for (var i = 0; i < shown; i++) yield return pills[i].Label.text;
            }
        }

        public void Show(IReadOnlyList<Chip> tabs, IReadOnlyList<Chip> chips)
        {
            shown = 0;
            Row(tabs, TabSize, (TabSize.y + Gap) * 0.5f);
            Row(chips, ChipSize, -(ChipSize.y + Gap) * 0.5f);
            for (var i = shown; i < pills.Count; i++) pills[i].Backing.gameObject.SetActive(false);
        }

        public void Hide() => Show(System.Array.Empty<Chip>(), System.Array.Empty<Chip>());

        /// The tab or chip the line from `eye` through `through` crosses.
        public bool Hit(Vector3 eye, Vector3 through, out Chip chip)
        {
            for (var i = 0; i < shown; i++)
            {
                var rect = pills[i].Backing.rectTransform;
                if (!SightLine.Hits(eye, through, rect.position, rect.rotation, rect.sizeDelta * rect.lossyScale.x, HitMargin)) continue;
                chip = pills[i].Chip;
                return true;
            }
            chip = default;
            return false;
        }

        void Row(IReadOnlyList<Chip> chips, Vector2 size, float y)
        {
            var first = shown;
            var width = -Gap;
            foreach (var chip in chips)
            {
                if (shown == pills.Count) pills.Add(NewPill());
                var pill = pills[shown++];
                pill.Chip = chip;
                pill.Backing.gameObject.SetActive(true);
                pill.Backing.color = chip.Selected ? Chosen : Unchosen;
                pill.Label.text = chip.Label;
                pill.Label.color = chip.Selected ? Dark : chip.Kind == ChipKind.Tier ? RarityColors.For(chip.Tier) : Bright;
                var fitted = new Vector2(Mathf.Max(size.x, pill.Label.preferredWidth + 2f * LabelInset), size.y);
                pill.Backing.rectTransform.sizeDelta = fitted;
                pill.Label.rectTransform.sizeDelta = fitted;
                width += fitted.x + Gap;
            }
            var x = -width * 0.5f;
            for (var i = first; i < shown; i++)
            {
                var rect = pills[i].Backing.rectTransform;
                rect.anchoredPosition = new Vector2(x + rect.sizeDelta.x * 0.5f, y);
                x += rect.sizeDelta.x + Gap;
            }
        }

        Pill NewPill()
        {
            var backing = new GameObject("Chip", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            backing.raycastTarget = false;
            backing.material = material;
            backing.rectTransform.SetParent(root, false);
            var label = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            label.font = font;
            label.fontSize = FontSize;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            label.material = material;
            label.rectTransform.SetParent(backing.rectTransform, false);
            return new Pill { Backing = backing, Label = label };
        }
    }
}
