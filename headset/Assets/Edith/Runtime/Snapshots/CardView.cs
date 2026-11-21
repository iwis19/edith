using Edith.Look;
using Edith.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Edith.Snapshots
{
    /// A snapshot's card: the photo sits in a liquid-glass panel whose outline and halo take the rarity's colour, four
    /// cyan sight corners mark the photo, and one centred line of text under it, "Looking…" until the fact types out.
    /// The photo's rect is in millimetres.
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
        const float LineHeight = 1.25f;
        const int MinLines = 2;
        const float ArmMm = 6f, ThickMm = 1.3f, InsetMm = 2f;
        static readonly Color Quiet = Theme.Muted;
        static readonly Color Waiting = new Color(Theme.Cyan.r, Theme.Cyan.g, Theme.Cyan.b, 0.5f);
        static readonly Color HuntColor = new Color(0.5f, 0.95f, 0.6f, 1f);
        static readonly Color HuntGlow = new Color(HuntColor.r, HuntColor.g, HuntColor.b, 0.6f);

        // Sizes above are for a card at arm's length. A card hung farther away, such as one on the object a scan pin
        // marked, grows its glass, gap and text with its distance so they look the same size from the eye.
        const float ArmsLengthMeters = 0.45f;

        Glass glass;
        Text fact;
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
            view.BuildText(photo, Theme.Regular(font), material);
            view.Fit();
            return view;
        }

        /// `caption`, when given, is said in place of the card's fact. `hunt` is how strongly the Look's hunt tick
        /// shows, from 1 as it lands to 0: the outline and halo flash green and the card says the target ticked.
        public void Show(LookCard card, string caption, float hunt = 0f)
        {
            var rated = card.Tier is Rarity tier ? RarityColors.For(tier) : (Color?)null;
            var outline = rated ?? Waiting;
            var rim = rated is Color tint ? new Color(tint.r, tint.g, tint.b, 0.16f) : new Color(1f, 1f, 1f, 0.12f);
            var halo = rated is Color lit ? new Color(lit.r, lit.g, lit.b, 0.4f) : Color.clear;
            if (hunt > 0f)
            {
                outline = Color.Lerp(outline, HuntColor, hunt);
                halo = Color.Lerp(halo, HuntGlow, hunt);
            }
            glass.SetLook(Theme.PanelFill, rim, outline, halo);

            if (hunt > 0f && card.HuntText != null)
            {
                fact.text = "✓ " + card.HuntText;
                fact.color = HuntColor;
            }
            else if (caption != null)
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
            var height = photoMm.y + GapMm * scale + textMm + 2f * pad;
            glass.Resize(new Vector2(width, height));
            glass.Rect.anchoredPosition = new Vector2(0f, photoMm.y * 0.5f + pad - height * 0.5f);
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
            image.color = Theme.Cyan;
            image.raycastTarget = false;
            image.material = material;
            Anchor(image.rectTransform, photo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), size, position);
        }

        void BuildText(RectTransform photo, Font font, Material material)
        {
            var column = new GameObject("Text", typeof(RectTransform)).GetComponent<RectTransform>();
            Anchor(column, photo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, -(photoMm.y * 0.5f + GapMm * scale)));
            column.localScale = Vector3.one * (scale / TextUnitsPerMm);

            var lineWidth = textWidthMm * TextUnitsPerMm / scale;
            fact = Label("Fact", column, font, material, FactSize, new Vector2(lineWidth, FactSize * LineHeight * MinLines));
            // TypedText hides the part not yet typed with a transparent colour tag.
            fact.supportRichText = true;
        }

        static Text Label(string name, RectTransform column, Font font, Material material, int fontSize, Vector2 size)
        {
            var text = Theme.Label(column, name, font, fontSize, Theme.Ice, TextAnchor.UpperCenter, material);
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
