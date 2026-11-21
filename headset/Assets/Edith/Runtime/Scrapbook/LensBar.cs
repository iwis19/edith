using System.Collections.Generic;
using Edith.Look;
using UnityEngine;
using UnityEngine.UI;

namespace Edith.Scrapbook
{
    /// The bar under the deck in two rows: the lens tabs, and under them the chips the lens in use offers. Each row
    /// leans back to face the eye.
    public sealed class LensBar
    {
        // Four canvas units per millimetre at the bar's old size, so the font is rasterised sharply enough.
        const float UnitsPerMetre = 4000f / ScrapbookPlacement.BarScale;
        // In the deck's frame, under the card's text.
        static readonly Vector3 TabsCentre = ScrapbookPlacement.BarCentre + Vector3.up * (ScrapbookPlacement.BarRowGap * 0.5f);
        static readonly Vector3 ChipsCentre = ScrapbookPlacement.BarCentre - Vector3.up * (ScrapbookPlacement.BarRowGap * 0.5f);
        // A pinch this near a chip still chooses it: a fingertip is about this much wider than the chip's gap.
        const float HitMargin = 0.002f;
        // The alpha of a chip that offers nothing, relative to a live one.
        const float DimmedAlpha = 0.4f;
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

        /// One row of pills on its own canvas. A label wider than its pill widens it, keeping `Inset` clear on each side.
        sealed class Row
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public readonly List<Pill> Pills = new List<Pill>();
            public int Shown;
            public Vector2 Size;
            public float Gap, Inset;
            public int FontSize;
        }

        readonly Row tabs, chips;
        readonly Font font;
        readonly Material material;

        /// `parent` is the deck's frame and must have unit world scale.
        public LensBar(Transform parent, Font font, Material material)
        {
            this.font = font;
            this.material = material;
            // Six tier chips must fit within the width of the deck's fan.
            tabs = NewRow(parent, "Tabs", TabsCentre, new Vector2(200f, 72f), 16f, 20f, 28);
            chips = NewRow(parent, "Chips", ChipsCentre, new Vector2(150f, 64f), 12f, 14f, 26);
        }

        public float Alpha
        {
            set => tabs.Group.alpha = chips.Group.alpha = value;
        }

        /// The labels on the bar, tabs first, then the chips left to right.
        public IEnumerable<string> Labels
        {
            get
            {
                foreach (var row in new[] { tabs, chips })
                    for (var i = 0; i < row.Shown; i++) yield return row.Pills[i].Label.text;
            }
        }

        public void Show(IReadOnlyList<Chip> tabChips, IReadOnlyList<Chip> chipChips)
        {
            Fill(tabs, tabChips);
            Fill(chips, chipChips);
        }

        public void Hide() => Show(System.Array.Empty<Chip>(), System.Array.Empty<Chip>());

        /// The enabled tab or chip the line from `eye` through `through` crosses.
        public bool Hit(Vector3 eye, Vector3 through, out Chip chip)
        {
            foreach (var row in new[] { tabs, chips })
                for (var i = 0; i < row.Shown; i++)
                {
                    var pill = row.Pills[i];
                    if (!pill.Chip.Enabled) continue;
                    var rect = pill.Backing.rectTransform;
                    if (!SightLine.Hits(eye, through, rect.position, rect.rotation, rect.sizeDelta * rect.lossyScale.x, HitMargin)) continue;
                    chip = pill.Chip;
                    return true;
                }
            chip = default;
            return false;
        }

        Row NewRow(Transform parent, string name, Vector3 centre, Vector2 size, float gap, float inset, int fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            var row = new Row { Root = (RectTransform)go.transform, Size = size, Gap = gap, Inset = inset, FontSize = fontSize };
            row.Root.SetParent(parent, false);
            row.Root.localPosition = centre;
            row.Root.localRotation = Quaternion.LookRotation(centre, Vector3.up);
            row.Root.localScale = Vector3.one / UnitsPerMetre;
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            row.Group = go.GetComponent<CanvasGroup>();
            row.Group.interactable = false;
            row.Group.blocksRaycasts = false;
            return row;
        }

        void Fill(Row row, IReadOnlyList<Chip> items)
        {
            row.Shown = 0;
            var width = -row.Gap;
            foreach (var chip in items)
            {
                if (row.Shown == row.Pills.Count) row.Pills.Add(NewPill(row));
                var pill = row.Pills[row.Shown++];
                pill.Chip = chip;
                pill.Backing.gameObject.SetActive(true);
                var live = chip.Enabled ? 1f : DimmedAlpha;
                var fill = chip.Selected ? (chip.Kind == ChipKind.Tier ? RarityColors.For(chip.Tier) : Chosen) : Unchosen;
                fill.a = (chip.Selected ? Chosen.a : Unchosen.a) * live;
                pill.Backing.color = fill;
                pill.Label.text = chip.Label;
                var ink = chip.Selected ? Dark : chip.Kind == ChipKind.Tier ? RarityColors.For(chip.Tier) : Bright;
                ink.a = live;
                pill.Label.color = ink;
                var fitted = new Vector2(Mathf.Max(row.Size.x, pill.Label.preferredWidth + 2f * row.Inset), row.Size.y);
                pill.Backing.rectTransform.sizeDelta = fitted;
                pill.Label.rectTransform.sizeDelta = fitted;
                width += fitted.x + row.Gap;
            }
            var x = -width * 0.5f;
            for (var i = 0; i < row.Shown; i++)
            {
                var rect = row.Pills[i].Backing.rectTransform;
                rect.anchoredPosition = new Vector2(x + rect.sizeDelta.x * 0.5f, 0f);
                x += rect.sizeDelta.x + row.Gap;
            }
            for (var i = row.Shown; i < row.Pills.Count; i++) row.Pills[i].Backing.gameObject.SetActive(false);
        }

        Pill NewPill(Row row)
        {
            var backing = new GameObject("Chip", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            backing.raycastTarget = false;
            backing.material = material;
            backing.rectTransform.SetParent(row.Root, false);
            var label = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            label.font = font;
            label.fontSize = row.FontSize;
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
