using System.Collections.Generic;
using Edith.Look;
using UnityEngine;
using UnityEngine.UI;

namespace Edith.Scrapbook
{
    /// The row of rarity chips under the deck. It leans back to face the eye.
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
        const int FontSize = 26;
        static readonly Color Unchosen = new Color(0.04f, 0.05f, 0.07f, 0.8f);
        static readonly Color Dark = new Color(0.04f, 0.05f, 0.07f, 1f);

        sealed class Pill
        {
            public Image Backing;
            public Text Label;
            public Chip Chip;
        }

        readonly RectTransform root;
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
                pill.Backing.gameObject.SetActive(true);
                var live = chip.Enabled ? 1f : DimmedAlpha;
                var fill = chip.Selected ? RarityColors.For(chip.Tier) : Unchosen;
                fill.a = (chip.Selected ? 0.95f : Unchosen.a) * live;
                pill.Backing.color = fill;
                pill.Label.text = chip.Label;
                var ink = chip.Selected ? Dark : RarityColors.For(chip.Tier);
                ink.a = live;
                pill.Label.color = ink;
                var fitted = new Vector2(Mathf.Max(ChipSize.x, pill.Label.preferredWidth + 2f * Inset), ChipSize.y);
                pill.Backing.rectTransform.sizeDelta = fitted;
                pill.Label.rectTransform.sizeDelta = fitted;
                width += fitted.x + Gap;
            }
            var x = -width * 0.5f;
            for (var i = 0; i < shown; i++)
            {
                var rect = pills[i].Backing.rectTransform;
                rect.anchoredPosition = new Vector2(x + rect.sizeDelta.x * 0.5f, 0f);
                x += rect.sizeDelta.x + Gap;
            }
            for (var i = shown; i < pills.Count; i++) pills[i].Backing.gameObject.SetActive(false);
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
                var rect = pills[i].Backing.rectTransform;
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
                var rect = pills[i].Backing.rectTransform;
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
