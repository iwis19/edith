using Edith.Look;
using Edith.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Edith.Snapshots
{
    /// A snapshot's card: the photo sits in a liquid-glass panel whose outline and halo take the rarity's colour, four
    /// cyan sight corners mark the photo, and one centred line of text under it, "Looking…" until the fact types out.
    /// The photo's rect is in millimetres. The text wraps within the photo's width. The Scrapbook draws its memories
    /// with the same card.
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
        const float ArmMm = 6f, ThickMm = 1.3f, InsetMm = 2f;
        static readonly Color Quiet = Theme.Muted;
        static readonly Color Waiting = new Color(Theme.Cyan.r, Theme.Cyan.g, Theme.Cyan.b, 0.5f);

        // Sizes above are for a card at arm's length. A card hung farther away, such as one on the object a scan pin
        // marked, grows its glass, gap and text with its distance so they look the same size from the eye.
        const float ArmsLengthMeters = 0.45f;

        Glass glass;
        Text fact;
        RectTransform column;
        CanvasGroup textGroup;
        readonly RectTransform[] corners = new RectTransform[8];
        float scale;
        Vector2 photoMm;
        float textWidthMm;
        int fittedLength = -1;

        /// `depth` is how far the card hangs from the eye that made it, in metres.
        public static CardView Build(RectTransform photo, Vector2 photoSizeMm, float depth, Font font, Material material)
        {
            var view = new CardView
            {
                scale = ScaleAt(depth),
                photoMm = photoSizeMm,
                textWidthMm = photoSizeMm.x,
            };
            view.BuildGlass(photo, material);
            view.BuildCorners(photo, material);
            view.BuildText(photo, Theme.Regular(font), material);
            view.Fit();
            return view;
        }

        /// How much a card hung `depth` metres from the eye grows its glass, gap and text, so they look the same size.
        public static float ScaleAt(float depth) => Mathf.Max(1f, depth / ArmsLengthMeters);

        /// Opacity of the text under the photo.
        public float TextAlpha
        {
            set => textGroup.alpha = value;
        }

        /// Lays the glass, the sight corners and the text out again around a photo of `photoSizeMm`.
        public void Fit(Vector2 photoSizeMm)
        {
            photoMm = photoSizeMm;
            textWidthMm = photoSizeMm.x;
            PlaceCorners();
            column.anchoredPosition = new Vector2(0f, -(photoMm.y * 0.5f + GapMm * scale));
            fact.rectTransform.sizeDelta = new Vector2(textWidthMm * TextUnitsPerMm / scale, fact.rectTransform.sizeDelta.y);
            Fit();
        }

        /// A saved card: `text` under the photo, with the glass in the rarity's colour.
        public void Show(Rarity? tier, string text)
        {
            Tint(tier);
            fact.text = text;
            fact.color = Theme.Ice;
            Fit();
        }

        void Tint(Rarity? tier)
        {
            var rated = tier is Rarity rarity ? RarityColors.For(rarity) : (Color?)null;
            if (rated is Color tint)
                glass.SetLook(Theme.PanelFill, new Color(tint.r, tint.g, tint.b, 0.16f), tint, new Color(tint.r, tint.g, tint.b, 0.4f));
            else
                glass.SetLook(Theme.PanelFill, new Color(1f, 1f, 1f, 0.12f), Waiting, Color.clear);
        }

        /// `caption`, when given, is said in place of the card's fact.
        public void Show(LookCard card, string caption)
        {
            Tint(card.Tier);
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

        static void Place(RectTransform rect, Vector2 size, Vector2 position)
        {
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        static RectTransform Bar(RectTransform photo, Material material)
        {
            var image = new GameObject("Corner", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.color = Theme.Cyan;
            image.raycastTarget = false;
            image.material = material;
            Anchor(image.rectTransform, photo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            return image.rectTransform;
        }

        void BuildText(RectTransform photo, Font font, Material material)
        {
            column = new GameObject("Text", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
            textGroup = column.GetComponent<CanvasGroup>();
            textGroup.interactable = false;
            textGroup.blocksRaycasts = false;
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
