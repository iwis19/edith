using System.Collections.Generic;
using Edith.Look;
using Edith.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Edith.Snapshots
{
    /// A snapshot's card: the photo sits in a liquid-glass panel whose outline and halo take the rarity's colour, with
    /// a thin frame and four white sight corners around the photo. Under it, past a dashed divider, the fact,
    /// "Looking" with one to three dots cycling until it types out. The photo's rect is in millimetres. The text wraps within the photo's width.
    /// The Scrapbook draws its memories with the same card.
    sealed class CardView
    {
        // Text is laid out in quarter millimetres and scaled down, so the font is rasterised sharply at this size.
        public const float TextUnitsPerMm = 4f;
        // The glass around the photo and its corner radius, and the gap from the photo to the text.
        const float PadMm = 7f;
        const float RadiusMm = 11f;
        const float GapMm = 5f;
        const int FactSize = 36;
        const float LineHeight = 1.25f;
        const int MinLines = 2;
        const float ArmMm = 6f, ThickMm = 1.3f, InsetMm = 3.5f;
        // The divider's distance under the photo and the gap from it to the fact, and its dashes: length, gap, thickness.
        const float DividerMm = 5f, TextGapMm = 6f, DashMm = 2.4f, DashGapMm = 1.6f, DashThickMm = 0.5f;
        // The thin frame round the photo: how far out from it and its corner radius. The photo's own corners are cut
        // to sit concentric inside it.
        const float FrameEdgeMm = 3f, FrameRadiusMm = 7f;
        static readonly Color Waiting = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.5f);
        static readonly Color DashColor = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.45f);

        // Sizes above are for a card at arm's length. A card hung farther away, such as one on the object a scan pin
        // marked, grows its glass, gap and text with its distance so they look the same size from the eye.
        const float ArmsLengthMeters = 0.45f;

        Glass glass, frame;
        Text fact;
        RectTransform column, divider;
        CanvasGroup textGroup, dividerGroup;
        readonly RectTransform[] corners = new RectTransform[8];
        readonly List<RectTransform> dashes = new List<RectTransform>();
        Material material;
        float scale;
        Vector2 photoMm;
        float textWidthMm;
        int fittedLength = -1;
        float textAlpha = 1f;
        float opened = 1f;
        Vector2 fullSize, fullCentre;

        /// The corner radius of the photo on a card hung `depth` metres from the eye, in millimetres.
        public static float PhotoRadiusMm(float depth) => (FrameRadiusMm - FrameEdgeMm) * ScaleAt(depth);

        /// `depth` is how far the card hangs from the eye that made it, in metres.
        public static CardView Build(RectTransform photo, Vector2 photoSizeMm, float depth, Font font, Material material)
        {
            var view = new CardView
            {
                scale = ScaleAt(depth),
                photoMm = photoSizeMm,
                textWidthMm = photoSizeMm.x,
                material = material,
            };
            view.BuildGlass(photo, material);
            view.BuildCorners(photo, material);
            view.BuildDivider(photo, material);
            view.BuildText(photo, Theme.Regular(font), material);
            view.Fit();
            return view;
        }

        /// How much a card hung `depth` metres from the eye grows its glass, gap and text, so they look the same size.
        public static float ScaleAt(float depth) => Mathf.Max(1f, depth / ArmsLengthMeters);

        /// Opacity of the text under the photo.
        public float TextAlpha
        {
            set
            {
                textAlpha = value;
                Unfold();
            }
        }

        /// How far the card has opened, 0 to 1. At 0 the glass hugs the photo's frame with no room for text; opening
        /// grows it down to its full height, and the divider and text fade in from 40% open.
        public float Opened
        {
            set
            {
                opened = Mathf.Clamp01(value);
                Unfold();
            }
        }

        /// Lays the glass, frame, sight corners, divider and text out again around a photo of `photoSizeMm`.
        public void Fit(Vector2 photoSizeMm)
        {
            photoMm = photoSizeMm;
            textWidthMm = photoSizeMm.x;
            PlaceCorners();
            frame.Resize(photoMm + Vector2.one * (2f * FrameEdgeMm * scale));
            PlaceDivider();
            column.anchoredPosition = new Vector2(0f, -(photoMm.y * 0.5f + (DividerMm + TextGapMm) * scale));
            fact.rectTransform.sizeDelta = new Vector2(textWidthMm * TextUnitsPerMm / scale, fact.rectTransform.sizeDelta.y);
            Fit();
        }

        /// A saved card: its fact, as a snapshot shows it, with the glass in the rarity's colour. With no fact the
        /// name stands in its place.
        public void Show(Rarity? tier, string name, string text)
        {
            Tint(tier, bold: false);
            fact.text = string.IsNullOrEmpty(text) ? name ?? "" : text;
            Fit();
        }

        /// `bold` draws a rated card's outline as a thick ring and its photo frame at full strength, so the rarity's
        /// colour reads at a glance. A card still waiting for its rarity keeps the thin outline.
        void Tint(Rarity? tier, bool bold)
        {
            var rated = tier is Rarity rarity ? RarityColors.For(rarity) : (Color?)null;
            var thick = bold && rated != null;
            glass.SetOutlineSprite(thick ? Theme.BoldEdge : Theme.Edge);
            frame.SetOutlineSprite(thick ? Theme.BoldEdge : Theme.Edge);
            if (rated is Color tint)
                glass.SetLook(Theme.PanelFill, new Color(tint.r, tint.g, tint.b, thick ? 0.22f : 0.16f), tint, new Color(tint.r, tint.g, tint.b, 0.4f));
            else
                glass.SetLook(Theme.PanelFill, new Color(1f, 1f, 1f, 0.12f), Waiting, Color.clear);
            var rimLook = rated ?? Theme.Accent;
            frame.SetLook(Color.clear, Color.clear, new Color(rimLook.r, rimLook.g, rimLook.b, thick ? 1f : 0.75f), Color.clear);
        }

        // How long each of "Looking.", "Looking.." and "Looking..." shows.
        const float DotSeconds = 0.45f;

        /// True while the card has nothing to say yet but "Looking".
        public static bool IsLooking(LookCard card, string caption) =>
            caption == null && card.Phase != LookPhase.Failed && card.Fact.Empty && !card.Finished;

        /// "Looking" and one, two or three dots, by how many `seconds` it has been looking.
        public static string LookingText(float seconds) =>
            "Looking" + new string('.', 1 + Mathf.FloorToInt(Mathf.Max(0f, seconds) / DotSeconds) % 3);

        /// `caption`, when given, is said in place of the card's fact. `lookingSeconds` picks the dots while it looks.
        public void Show(LookCard card, string caption, float lookingSeconds = 0f)
        {
            Tint(card.Tier, bold: true);
            fact.text = IsLooking(card, caption) ? LookingText(lookingSeconds)
                : caption ?? (card.Phase == LookPhase.Failed ? card.Error
                    : card.Fact.Empty ? ""
                    : card.Fact.Render());
            if (fact.text.Length != fittedLength) Fit();
        }

        /// Sizes the glass to the photo and the text under it, and keeps the photo's top and sides where they were.
        void Fit()
        {
            fittedLength = fact.text.Length;
            var textMm = Mathf.Max(fact.preferredHeight, FactSize * LineHeight * MinLines) * scale / TextUnitsPerMm;
            var pad = PadMm * scale;
            var width = textWidthMm + 2f * pad;
            var height = photoMm.y + (DividerMm + TextGapMm) * scale + textMm + 2f * pad;
            fullSize = new Vector2(width, height);
            fullCentre = new Vector2(0f, photoMm.y * 0.5f + pad - height * 0.5f);
            Unfold();
        }

        /// Places the glass between its closed and full size by `opened`, eased in and out (smootherstep) so it
        /// glides rather than snaps. The top and sides never move, so the glass only grows downward.
        void Unfold()
        {
            var grow = opened * opened * opened * (opened * (opened * 6f - 15f) + 10f);
            var closed = new Vector2(fullSize.x, photoMm.y + 2f * PadMm * scale);
            glass.Resize(Vector2.Lerp(closed, fullSize, grow));
            glass.Rect.anchoredPosition = Vector2.Lerp(Vector2.zero, fullCentre, grow);
            var reveal = Mathf.SmoothStep(0f, 1f, (opened - 0.4f) / 0.6f);
            dividerGroup.alpha = reveal;
            textGroup.alpha = textAlpha * reveal;
        }

        /// The thin frame around the photo, and the dashed line that separates photo from text.
        void BuildDivider(RectTransform photo, Material material)
        {
            frame = Glass.Build(photo, "Frame", photoMm + Vector2.one * (2f * FrameEdgeMm * scale), FrameRadiusMm * scale, material);
            frame.Rect.SetSiblingIndex(1);
            divider = new GameObject("Divider", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
            dividerGroup = divider.GetComponent<CanvasGroup>();
            dividerGroup.interactable = false;
            dividerGroup.blocksRaycasts = false;
            Anchor(divider, photo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            PlaceDivider();
        }

        /// As many whole dashes as fit the text's width, centred under it.
        void PlaceDivider()
        {
            divider.anchoredPosition = new Vector2(0f, -(photoMm.y * 0.5f + DividerMm * scale));
            float dash = DashMm * scale, step = (DashMm + DashGapMm) * scale;
            var count = Mathf.Max(1, Mathf.FloorToInt((textWidthMm + DashGapMm * scale) / step));
            var start = -((count - 1) * step) * 0.5f;
            while (dashes.Count < count) dashes.Add(Segment(material, DashColor));
            for (var i = 0; i < dashes.Count; i++)
            {
                dashes[i].gameObject.SetActive(i < count);
                Place(dashes[i], new Vector2(dash, DashThickMm * scale), new Vector2(start + i * step, 0f));
            }
        }

        static void Place(RectTransform rect, Vector2 size, Vector2 position)
        {
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        RectTransform Segment(Material material, Color color)
        {
            var image = new GameObject("Mark", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            image.material = material;
            Anchor(image.rectTransform, divider, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            return image.rectTransform;
        }

        void BuildGlass(RectTransform photo, Material material)
        {
            glass = Glass.Build(photo, "Glass", photoMm, RadiusMm * scale, material);
            // Behind the photo, which was made first.
            glass.Rect.SetAsFirstSibling();
        }

        /// Four sight corners just inside the photo's edges.
        void BuildCorners(RectTransform photo, Material material)
        {
            for (var i = 0; i < corners.Length; i++) corners[i] = Bar(photo, material);
            PlaceCorners();
        }

        void PlaceCorners()
        {
            float arm = ArmMm * scale, thick = ThickMm * scale, inset = InsetMm * scale;
            float x = photoMm.x * 0.5f - inset, y = photoMm.y * 0.5f - inset;
            for (var i = 0; i < 4; i++)
            {
                float sx = i % 2 == 0 ? -1f : 1f, sy = i < 2 ? 1f : -1f;
                // The bar along the top or bottom edge and the bar down the side, each starting at the corner.
                Place(corners[2 * i], new Vector2(arm, thick), new Vector2(sx * (x - arm * 0.5f), sy * (y - thick * 0.5f)));
                Place(corners[2 * i + 1], new Vector2(thick, arm), new Vector2(sx * (x - thick * 0.5f), sy * (y - arm * 0.5f)));
            }
        }

        static RectTransform Bar(RectTransform photo, Material material)
        {
            var image = new GameObject("Corner", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.color = Theme.Ice;
            image.raycastTarget = false;
            image.material = material;
            Anchor(image.rectTransform, photo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            return image.rectTransform;
        }

        void BuildText(RectTransform photo, Font regular, Material material)
        {
            column = new GameObject("Text", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
            textGroup = column.GetComponent<CanvasGroup>();
            textGroup.interactable = false;
            textGroup.blocksRaycasts = false;
            Anchor(column, photo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), Vector2.zero,
                new Vector2(0f, -(photoMm.y * 0.5f + (DividerMm + TextGapMm) * scale)));
            column.localScale = Vector3.one * (scale / TextUnitsPerMm);

            var lineWidth = textWidthMm * TextUnitsPerMm / scale;
            fact = Label("Fact", column, regular, material, FactSize, new Vector2(lineWidth, FactSize * LineHeight * MinLines));
            // TypedText hides the part not yet typed with a transparent colour tag.
            fact.supportRichText = true;
        }

        static Text Label(string name, RectTransform column, Font font, Material material, int fontSize, Vector2 size)
        {
            // Plain white, with no outline or shadow around the letters.
            var text = Theme.Label(column, name, font, fontSize, Color.white, TextAnchor.UpperLeft, material, shadow: false);
            Anchor(text.rectTransform, column, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), size, Vector2.zero);
            return text;
        }

        static void Anchor(RectTransform rect, RectTransform parent, Vector2 anchor, Vector2 pivot, Vector2 size, Vector2 position)
        {
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }
    }
}
