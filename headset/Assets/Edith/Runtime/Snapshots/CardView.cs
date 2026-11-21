using Edith.Look;
using Edith.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Edith.Snapshots
{
    /// A snapshot's card: the photo sits in a liquid-glass panel whose outline and halo take the rarity's colour, with
    /// a thin frame and four white sight corners around the photo. Under it, past a divider with a dot, the name in
    /// bold and the fact below it, "Looking…" until the fact types out. The photo's rect is in millimetres.
    sealed class CardView
    {
        // Text is laid out in quarter millimetres and scaled down, so the font is rasterised sharply at this size.
        const float TextUnitsPerMm = 4f;
        // The glass around the photo and its corner radius, the gap from the photo to the text, and the narrowest the
        // text column goes.
        const float PadMm = 7f;
        const float RadiusMm = 11f;
        const float GapMm = 5f;
        const float MinTextWidthMm = 90f;
        const int FactSize = 36;
        const int TitleSize = 44;
        const float LineHeight = 1.25f;
        const int MinLines = 2;
        const float ArmMm = 6f, ThickMm = 1.3f, InsetMm = 3.5f;
        // The divider's distance under the photo, the gap from it to the name, and from the name to the fact.
        const float DividerMm = 5f, TitleGapMm = 6f, FactGapMm = 1.5f, DotMm = 2.6f;
        static readonly Color Quiet = Theme.Muted;
        static readonly Color Waiting = new Color(Theme.Cyan.r, Theme.Cyan.g, Theme.Cyan.b, 0.5f);

        // Sizes above are for a card at arm's length. A card hung farther away, such as one on the object a scan pin
        // marked, grows its glass, gap and text with its distance so they look the same size from the eye.
        const float ArmsLengthMeters = 0.45f;

        Glass glass, frame;
        Text title, fact;
        RectTransform column, divider;
        float scale;
        Vector2 photoMm;
        float textWidthMm;
        int fittedLength = -1;

        /// `depth` is how far the card hangs from the eye that made it, in metres.
        public static CardView Build(RectTransform photo, Vector2 photoSizeMm, float depth, Font font, Material material)
        {
            var view = new CardView
            {
                scale = Mathf.Max(1f, depth / ArmsLengthMeters),
                photoMm = photoSizeMm,
            };
            view.textWidthMm = Mathf.Max(photoSizeMm.x, MinTextWidthMm * view.scale);
            view.BuildGlass(photo, material);
            view.BuildCorners(photo, material);
            view.BuildDivider(photo, material);
            view.BuildText(photo, Theme.Regular(font), Theme.SemiBold(font), material);
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

            var rimLook = rated ?? Theme.Cyan;
            frame.SetLook(Color.clear, Color.clear, new Color(rimLook.r, rimLook.g, rimLook.b, 0.75f), Color.clear);

            // The name leads once it arrives, unless the card has nothing else to say, when the name is the fact.
            title.text = card.Phase == LookPhase.Failed || card.Name == card.Fact.Visible ? "" : card.Name;
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
            var key = fact.text.Length * 64 + Mathf.Min(title.text.Length, 63);
            if (key != fittedLength) Fit(key);
        }

        /// Sizes the glass to the photo and the text under it, and keeps the photo's top and sides where they were.
        void Fit(int key = 0)
        {
            fittedLength = key;
            var titleUnits = title.text.Length > 0 ? TitleSize * LineHeight : 0f;
            fact.rectTransform.anchoredPosition = new Vector2(0f, -(titleUnits > 0f ? titleUnits + FactGapMm * TextUnitsPerMm : 0f));
            var textUnits = titleUnits + (titleUnits > 0f ? FactGapMm * TextUnitsPerMm : 0f)
                + Mathf.Max(fact.preferredHeight, FactSize * LineHeight * MinLines);
            var textMm = textUnits * scale / TextUnitsPerMm;
            var pad = PadMm * scale;
            var width = textWidthMm + 2f * pad;
            var height = photoMm.y + (DividerMm + TitleGapMm) * scale + textMm + 2f * pad;
            glass.Resize(new Vector2(width, height));
            glass.Rect.anchoredPosition = new Vector2(0f, photoMm.y * 0.5f + pad - height * 0.5f);
        }

        /// The thin frame around the photo, and the line with a dot in it that separates photo from text.
        void BuildDivider(RectTransform photo, Material material)
        {
            var edge = 3f * scale;
            frame = Glass.Build(photo, "Frame", photoMm + Vector2.one * (2f * edge), 7f * scale, material);
            frame.Rect.SetSiblingIndex(1);
            divider = new GameObject("Divider", typeof(RectTransform)).GetComponent<RectTransform>();
            Anchor(divider, photo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, -(photoMm.y * 0.5f + DividerMm * scale)));
            var half = textWidthMm * 0.5f - DotMm * scale * 3f;
            var line = new Color(Theme.Cyan.r, Theme.Cyan.g, Theme.Cyan.b, 0.45f);
            Segment(material, new Vector2(half, 0.5f * scale), new Vector2(-(half * 0.5f + DotMm * scale * 1.5f), 0f), line);
            Segment(material, new Vector2(half, 0.5f * scale), new Vector2(half * 0.5f + DotMm * scale * 1.5f, 0f), line);
            Segment(material, Vector2.one * (DotMm * scale), Vector2.zero, Theme.Cyan, round: true);
        }

        void Segment(Material material, Vector2 size, Vector2 position, Color color, bool round = false)
        {
            var image = new GameObject("Mark", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            image.material = material;
            if (round)
            {
                image.sprite = Theme.Fill;
                image.type = Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = Theme.ShapeRadius / (size.x * 0.5f);
            }
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

        void BuildText(RectTransform photo, Font regular, Font semibold, Material material)
        {
            column = new GameObject("Text", typeof(RectTransform)).GetComponent<RectTransform>();
            Anchor(column, photo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), Vector2.zero,
                new Vector2(0f, -(photoMm.y * 0.5f + (DividerMm + TitleGapMm) * scale)));
            column.localScale = Vector3.one * (scale / TextUnitsPerMm);

            var lineWidth = textWidthMm * TextUnitsPerMm / scale;
            title = Label("Title", column, semibold, material, TitleSize, new Vector2(lineWidth, TitleSize * LineHeight));
            title.color = Theme.Ice;
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
