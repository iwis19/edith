using Edith.Look;
using Edith.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Edith.Snapshots
{
    /// A snapshot's card: the photo sits in a liquid-glass panel whose outline and halo take the rarity's colour, with
    /// a thin frame and four white sight corners around the photo. Under it, past a dashed divider, the fact,
    /// "Looking…" until it types out. The photo's rect is in millimetres.
    sealed class CardView
    {
        // Text is laid out in quarter millimetres and scaled down, so the font is rasterised sharply at this size.
        const float TextUnitsPerMm = 4f;
        // The glass around the photo and its corner radius, and the gap from the photo to the text. The text wraps
        // within the photo's width, which LookController keeps from being skinny.
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
        static readonly Color Quiet = Theme.Muted;
        static readonly Color Waiting = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.5f);

        // Sizes above are for a card at arm's length. A card hung farther away, such as one on the object a scan pin
        // marked, grows its glass, gap and text with its distance so they look the same size from the eye.
        const float ArmsLengthMeters = 0.45f;

        Glass glass, frame;
        Text fact;
        RectTransform column, divider;
        float scale;
        Vector2 photoMm;
        float textWidthMm;
        int fittedLength = -1;

        /// The corner radius of the photo on a card hung `depth` metres from the eye, in millimetres.
        public static float PhotoRadiusMm(float depth) => (FrameRadiusMm - FrameEdgeMm) * Scale(depth);

        static float Scale(float depth) => Mathf.Max(1f, depth / ArmsLengthMeters);

        /// `depth` is how far the card hangs from the eye that made it, in metres.
        public static CardView Build(RectTransform photo, Vector2 photoSizeMm, float depth, Font font, Material material)
        {
            var view = new CardView
            {
                scale = Scale(depth),
                photoMm = photoSizeMm,
            };
            view.textWidthMm = photoSizeMm.x;
            view.BuildGlass(photo, material);
            view.BuildCorners(photo, material);
            view.BuildDivider(photo, material);
            view.BuildText(photo, Theme.Regular(font), material);
            view.Fit();
            return view;
        }

        /// `caption`, when given, is said in place of the card's fact.
        public void Show(LookCard card, string caption)
        {
            var rated = card.Tier is Rarity tier ? RarityColors.For(tier) : (Color?)null;
            if (rated is Color tint)
                glass.SetLook(Theme.PanelFill, new Color(tint.r, tint.g, tint.b, 0.16f), tint, new Color(tint.r, tint.g, tint.b, 0.4f));
            else
                glass.SetLook(Theme.PanelFill, new Color(1f, 1f, 1f, 0.12f), Waiting, Color.clear);

            var rimLook = rated ?? Theme.Accent;
            frame.SetLook(Color.clear, Color.clear, new Color(rimLook.r, rimLook.g, rimLook.b, 0.75f), Color.clear);

            if (caption != null)
            {
                fact.text = caption;
                fact.color = Theme.Ice;
            }
            else if (card.Phase == LookPhase.Failed)
            {
                fact.text = card.Error;
                fact.color = Quiet;
            }
            else if (card.Fact.Empty)
            {
                fact.text = card.Finished ? "" : "Looking…";
                fact.color = Quiet;
            }
            else
            {
                fact.text = card.Fact.Render();
                fact.color = Theme.Ice;
            }
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
            glass.Resize(new Vector2(width, height));
            glass.Rect.anchoredPosition = new Vector2(0f, photoMm.y * 0.5f + pad - height * 0.5f);
        }

        /// The thin frame around the photo, and the dashed line that separates photo from text.
        void BuildDivider(RectTransform photo, Material material)
        {
            var edge = FrameEdgeMm * scale;
            frame = Glass.Build(photo, "Frame", photoMm + Vector2.one * (2f * edge), FrameRadiusMm * scale, material);
            frame.Rect.SetSiblingIndex(1);
            divider = new GameObject("Divider", typeof(RectTransform)).GetComponent<RectTransform>();
            Anchor(divider, photo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, -(photoMm.y * 0.5f + DividerMm * scale)));
            var line = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.45f);
            float dash = DashMm * scale, step = (DashMm + DashGapMm) * scale;
            // As many whole dashes as fit the text's width, centred under it.
            var count = Mathf.Max(1, Mathf.FloorToInt((textWidthMm + DashGapMm * scale) / step));
            var start = -((count - 1) * step) * 0.5f;
            for (var i = 0; i < count; i++)
                Segment(material, new Vector2(dash, DashThickMm * scale), new Vector2(start + i * step, 0f), line);
        }

        void Segment(Material material, Vector2 size, Vector2 position, Color color)
        {
            var image = new GameObject("Mark", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            image.material = material;
            Anchor(image.rectTransform, divider, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), size, position);
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
            float arm = ArmMm * scale, thick = ThickMm * scale, inset = InsetMm * scale;
            float x = photoMm.x * 0.5f - inset, y = photoMm.y * 0.5f - inset;
            for (var i = 0; i < 4; i++)
            {
                float sx = i % 2 == 0 ? -1f : 1f, sy = i < 2 ? 1f : -1f;
                // The bar along the top or bottom edge and the bar down the side, each starting at the corner.
                Bar(photo, material, new Vector2(arm, thick), new Vector2(sx * (x - arm * 0.5f), sy * (y - thick * 0.5f)));
                Bar(photo, material, new Vector2(thick, arm), new Vector2(sx * (x - thick * 0.5f), sy * (y - arm * 0.5f)));
            }
        }

        static void Bar(RectTransform photo, Material material, Vector2 size, Vector2 position)
        {
            var image = new GameObject("Corner", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.color = Theme.Ice;
            image.raycastTarget = false;
            image.material = material;
            Anchor(image.rectTransform, photo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), size, position);
        }

        void BuildText(RectTransform photo, Font regular, Material material)
        {
            column = new GameObject("Text", typeof(RectTransform)).GetComponent<RectTransform>();
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
            var text = Theme.Label(column, name, font, fontSize, Theme.Ice, TextAnchor.UpperLeft, material);
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
