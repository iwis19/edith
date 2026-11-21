using Edith.Look;
using UnityEngine;
using UnityEngine.UI;

namespace Edith.Snapshots
{
    /// A snapshot's card drawn around its photo: a thin border just outside it that takes the rarity's colour, and
    /// one line of text under it, "Looking…" until the fact types out in the rarity's colour. The photo's rect is in
    /// millimetres.
    sealed class CardView
    {
        // Text is laid out in quarter millimetres and scaled down, so the font is rasterised sharply at this size.
        const float TextUnitsPerMm = 4f;
        const float BorderMm = 3f;
        const float GapMm = 6f;
        const float MinTextWidthMm = 90f;
        const float FactHeight = 360f;
        const int FactSize = 36;
        static readonly Color Bright = new Color(0.97f, 0.97f, 0.99f, 1f);
        static readonly Color Quiet = new Color(0.78f, 0.8f, 0.85f, 0.7f);
        static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.85f);
        static readonly Color BorderWaiting = new Color(0.95f, 0.95f, 0.97f, 0.35f);

        readonly Image[] border = new Image[4];
        Text fact;

        // Sizes above are for a card at arm's length. A card hung farther away, such as one on the object a scan pin
        // marked, grows its border, gap and text with its distance so they look the same size from the eye.
        const float ArmsLengthMeters = 0.45f;

        /// `depth` is how far the card hangs from the eye that made it, in metres.
        public static CardView Build(RectTransform photo, Vector2 photoSizeMm, float depth, Font font, Material material)
        {
            var view = new CardView();
            var scale = Mathf.Max(1f, depth / ArmsLengthMeters);
            view.BuildBorder(photo, photoSizeMm, scale, material);
            view.BuildText(photo, photoSizeMm, scale, font, material);
            return view;
        }

        public void Show(LookCard card)
        {
            var rated = card.Tier is Rarity tier ? RarityColors.For(tier) : (Color?)null;
            foreach (var edge in border) edge.color = rated ?? BorderWaiting;
            if (card.Phase == LookPhase.Failed)
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
                fact.color = rated ?? Bright;
            }
        }

        /// Four strips just outside the photo, so the border never covers any of it.
        void BuildBorder(RectTransform photo, Vector2 size, float scale, Material material)
        {
            float w = BorderMm * scale, x = (size.x + w) * 0.5f, y = (size.y + w) * 0.5f;
            var edges = new[]
            {
                (new Vector2(size.x + 2f * w, w), new Vector2(0f, y)),
                (new Vector2(size.x + 2f * w, w), new Vector2(0f, -y)),
                (new Vector2(w, size.y), new Vector2(-x, 0f)),
                (new Vector2(w, size.y), new Vector2(x, 0f)),
            };
            for (var i = 0; i < edges.Length; i++)
            {
                var image = new GameObject("Border", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                image.raycastTarget = false;
                image.material = material;
                Anchor(image.rectTransform, photo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), edges[i].Item1, edges[i].Item2);
                border[i] = image;
            }
        }

        void BuildText(RectTransform photo, Vector2 size, float scale, Font font, Material material)
        {
            var width = Mathf.Max(size.x, MinTextWidthMm * scale);
            var column = new GameObject("Text", typeof(RectTransform)).GetComponent<RectTransform>();
            Anchor(column, photo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, -(size.y * 0.5f + GapMm * scale)));
            column.localScale = Vector3.one * (scale / TextUnitsPerMm);

            var lineWidth = width * TextUnitsPerMm / scale;
            fact = Label("Fact", column, font, material, FactSize, FontStyle.Normal, new Vector2(lineWidth, FactHeight), 0f);
            // TypedText hides the part not yet typed with a transparent colour tag.
            fact.supportRichText = true;
        }

        static Text Label(string name, RectTransform column, Font font, Material material, int fontSize, FontStyle style,
            Vector2 size, float top)
        {
            var text = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(UnityEngine.UI.Shadow)).GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = TextAnchor.UpperCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.material = material;
            var shadow = text.GetComponent<UnityEngine.UI.Shadow>();
            shadow.effectColor = ShadowColor;
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
            Anchor(text.rectTransform, column, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), size, new Vector2(0f, top));
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
