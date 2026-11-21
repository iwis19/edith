using System.Collections.Generic;
using Edith.Look;
using Edith.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Edith.Scrapbook
{
    /// The row of rarity chips under the deck: frost-lettered pills outlined in their tier's colour, the chosen one
    /// filled with it, on one dark glass bar. It leans back to face the eye.
    public sealed class TierBar
    {
        // Four canvas units per millimetre at the bar's old size, so the font is rasterised sharply enough.
        const float UnitsPerMetre = 4000f / ScrapbookPlacement.BarScale;
        // A pinch this near a chip's box still chooses it. Chips are nearer each other than this, so the one nearest
        // the line of sight wins.
        const float HitMargin = 0.015f;
        // The alpha of a chip that offers nothing, relative to a live one.
        const float DimmedAlpha = 0.4f;
        // Six chips must fit within the width of the deck's fan.
        static readonly Vector2 ChipSize = new Vector2(150f, 64f);
        const float Gap = 12f, Inset = 14f;
        // The bar's margin round the chips.
        const float BarPad = 12f;
        const int FontSize = 26;
        static readonly Color Unchosen = Theme.Tint(0.8f);
        static readonly Color BarFill = Theme.Tint(0.7f);
        // The chosen chip's fill: its tier's colour, soft enough for the frost text on it to read.
        const float ChosenAlpha = 0.6f;

        sealed class Pill
        {
            public Glass Glass;
            public Text Label;
            public Chip Chip;
        }

        readonly RectTransform root;
        readonly Glass bar;
        readonly CanvasGroup group;
        readonly List<Pill> pills = new List<Pill>();
        readonly Font font;
        readonly Material material;
        int shown;

        /// `parent` is the deck's frame and must have unit world scale.
        public TierBar(Transform parent, Font font, Material material)
        {
            this.font = font;
            this.material = material;
            var go = new GameObject("Chips", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            root = (RectTransform)go.transform;
            root.SetParent(parent, false);
            root.localPosition = ScrapbookPlacement.ChipsCentre;
            root.localRotation = Quaternion.LookRotation(ScrapbookPlacement.ChipsCentre, Vector3.up);
            root.localScale = Vector3.one / UnitsPerMetre;
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            group = go.GetComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            bar = Glass.Build(root, "Bar", ChipSize, (ChipSize.y + 2f * BarPad) * 0.5f, material);
            bar.SetLook(BarFill, new Color(1f, 1f, 1f, 0.1f), new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.3f), Color.clear);
        }

        public float Alpha
        {
            set => group.alpha = value;
        }

        /// The labels on the bar, left to right.
        public IEnumerable<string> Labels
        {
            get
            {
                for (var i = 0; i < shown; i++) yield return pills[i].Label.text;
            }
        }

        /// A label wider than its chip widens it, keeping `Inset` clear on each side.
        public void Show(IReadOnlyList<Chip> chips)
        {
            shown = 0;
            var width = -Gap;
            foreach (var chip in chips)
            {
                if (shown == pills.Count) pills.Add(NewPill());
                var pill = pills[shown++];
                pill.Chip = chip;
                pill.Glass.Rect.gameObject.SetActive(true);
                var live = chip.Enabled ? 1f : DimmedAlpha;
                var tier = RarityColors.For(chip.Tier);
                var fill = chip.Selected ? tier : Unchosen;
                fill.a = (chip.Selected ? ChosenAlpha : Unchosen.a) * live;
                pill.Glass.SetLook(fill, new Color(1f, 1f, 1f, 0.1f * live), new Color(tier.r, tier.g, tier.b, live),
                    chip.Selected ? new Color(tier.r, tier.g, tier.b, 0.4f * live) : Color.clear);
                pill.Label.text = chip.Label;
                var ink = Theme.Ice;
                ink.a = live;
                pill.Label.color = ink;
                var fitted = new Vector2(Mathf.Max(ChipSize.x, pill.Label.preferredWidth + 2f * Inset), ChipSize.y);
                pill.Glass.Resize(fitted);
                pill.Label.rectTransform.sizeDelta = fitted;
                width += fitted.x + Gap;
            }
            var x = -width * 0.5f;
            for (var i = 0; i < shown; i++)
            {
                var rect = pills[i].Glass.Rect;
                rect.anchoredPosition = new Vector2(x + rect.sizeDelta.x * 0.5f, 0f);
                x += rect.sizeDelta.x + Gap;
            }
            for (var i = shown; i < pills.Count; i++) pills[i].Glass.Rect.gameObject.SetActive(false);
            bar.Rect.gameObject.SetActive(shown > 0);
            bar.Resize(new Vector2(width + 2f * BarPad, ChipSize.y + 2f * BarPad));
        }

        public void Hide() => Show(System.Array.Empty<Chip>());

        /// The enabled chip nearest the line from `eye` through `through`, among those it crosses or passes within
        /// HitMargin of, and how many degrees off the line its centre is.
        public bool Hit(Vector3 eye, Vector3 through, out Chip chip, out float degrees)
        {
            chip = default;
            degrees = float.MaxValue;
            // A pinch squarely on a chip belongs to it, a dimmed one included, so the slack around a neighbour never
            // takes a pinch aimed at a tier with nothing in it.
            for (var i = 0; i < shown; i++)
            {
                var rect = pills[i].Glass.Rect;
                if (!SightLine.Hits(eye, through, rect.position, rect.rotation, rect.sizeDelta * rect.lossyScale.x, 0f)) continue;
                if (!pills[i].Chip.Enabled) return false;
                chip = pills[i].Chip;
                degrees = SightLine.Offset(eye, through, rect.position);
                return true;
            }
            var found = false;
            for (var i = 0; i < shown; i++)
            {
                if (!pills[i].Chip.Enabled) continue;
                var rect = pills[i].Glass.Rect;
                if (!SightLine.Hits(eye, through, rect.position, rect.rotation, rect.sizeDelta * rect.lossyScale.x, HitMargin)) continue;
                var off = SightLine.Offset(eye, through, rect.position);
                if (off >= degrees) continue;
                degrees = off;
                chip = pills[i].Chip;
                found = true;
            }
            return found;
        }

        Pill NewPill()
        {
            var glass = Glass.Build(root, "Chip", ChipSize, ChipSize.y * 0.5f, material);
            var label = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            label.font = font;
            label.fontSize = FontSize;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            label.material = material;
            label.rectTransform.SetParent(glass.Rect, false);
            return new Pill { Glass = glass, Label = label };
        }
    }
}
