using Edith.Look;
using UnityEngine;
using UnityEngine.UI;

namespace Edith.Scrapbook
{
    /// One card of the deck: the photo, fitted whole into a frame of the rarity's colour, and under it the fun fact
    /// and the caption, like a snapshot's card. The name shows only when there is no fact. Also draws the Scrapbook's notices, which are cards with no photo.
    public sealed class MemoryCard
    {
        // Four canvas units per millimetre, so the font is rasterised sharply enough at this size.
        const float UnitsPerMetre = 4000f;
        const float Width = DeckLayout.CardWidth * UnitsPerMetre;
        const float FrameHeight = 600f, TextHeight = 240f;
        const float FrameInset = 16f, Gap = 12f;
        const int NameSize = 44, FactSize = 30, CaptionSize = 28;
        const float NameHeight = 52f, FactLineHeight = 35f, FactMaxHeight = 4f * FactLineHeight, CaptionHeight = 36f;
        static readonly Color Bright = new Color(0.97f, 0.97f, 0.99f, 1f);
        static readonly Color Quiet = new Color(0.78f, 0.8f, 0.85f, 0.8f);
        static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.85f);
        static readonly Color NoPhoto = new Color(0.04f, 0.05f, 0.07f, 0.8f);
        /// The frame of a memory that has no rarity, and of a notice.
        public static readonly Color Untiered = new Color(0.95f, 0.95f, 0.97f, 0.35f);

        RectTransform root;
        Canvas canvas;
        CanvasGroup group, textGroup;
        Image frame;
        RawImage photo;
        Text name, fact, caption;
        bool fitted;

        public static MemoryCard Build(Transform parent, Font font, Material material)
        {
            var card = new MemoryCard();
            var go = new GameObject("Card", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            card.root = (RectTransform)go.transform;
            card.root.SetParent(parent, false);
            card.root.sizeDelta = new Vector2(Width, FrameHeight + TextHeight);
            card.canvas = go.GetComponent<Canvas>();
            card.canvas.renderMode = RenderMode.WorldSpace;
            card.group = Passive(go.GetComponent<CanvasGroup>());

            card.frame = New<Image>("Frame", card.root, material);
            Anchor(card.frame.rectTransform, new Vector2(Width, FrameHeight), new Vector2(0f, TextHeight * 0.5f));
            card.photo = New<RawImage>("Photo", card.frame.rectTransform, material);

            var text = new GameObject("Text", typeof(RectTransform), typeof(CanvasGroup));
            var column = (RectTransform)text.transform;
            column.SetParent(card.root, false);
            column.pivot = new Vector2(0.5f, 1f);
            Anchor(column, new Vector2(Width, TextHeight), new Vector2(0f, TextHeight * 0.5f - FrameHeight * 0.5f - Gap));
            card.textGroup = Passive(text.GetComponent<CanvasGroup>());
            card.name = Label("Name", column, font, material, NameSize, FontStyle.Bold, NameHeight);
            card.name.horizontalOverflow = HorizontalWrapMode.Overflow;
            card.fact = Label("Fact", column, font, material, FactSize, FontStyle.Normal, FactMaxHeight);
            card.fact.verticalOverflow = VerticalWrapMode.Truncate;
            card.caption = Label("Caption", column, font, material, CaptionSize, FontStyle.Normal, CaptionHeight);
            card.caption.color = Quiet;
            card.SetPhoto(null);
            card.Hide();
            return card;
        }

        public void Show(Memory memory) =>
            Show(string.IsNullOrEmpty(memory.Fact) ? memory.Name : null, memory.Fact, memory.Caption, memory.Tier is Rarity tier ? RarityColors.For(tier) : Untiered);

        public void Show(string title, string body, string footer, Color frameColor)
        {
            frame.color = frameColor;
            name.text = title ?? "";
            fact.text = body ?? "";
            caption.text = footer ?? "";
            // The fact sits under the title when there is one, and the caption follows the fact, however many lines it
            // wraps to.
            var titleHeight = string.IsNullOrEmpty(title) ? 0f : NameHeight;
            fact.rectTransform.anchoredPosition = new Vector2(0f, -titleHeight);
            var factHeight = string.IsNullOrEmpty(body) ? 0f : Mathf.Clamp(fact.preferredHeight, FactLineHeight, FactMaxHeight);
            caption.rectTransform.anchoredPosition = new Vector2(0f, -(titleHeight + factHeight + (factHeight > 0f ? Gap : 0f)));
        }

        /// Shows `texture` whole inside the frame, at its own shape. Null shows an empty frame.
        public void SetPhoto(Texture texture)
        {
            if (fitted && photo.texture == texture) return;
            fitted = true;
            photo.texture = texture;
            photo.color = texture == null ? NoPhoto : Color.white;
            var room = new Vector2(Width, FrameHeight) - 2f * FrameInset * Vector2.one;
            var size = room;
            if (texture != null && texture.width > 0 && texture.height > 0)
            {
                var shape = texture.width / (float)texture.height;
                size = shape > room.x / room.y ? new Vector2(room.x, room.x / shape) : new Vector2(room.y * shape, room.y);
            }
            Anchor(photo.rectTransform, size, Vector2.zero);
        }

        /// `pose` is in the frame of the card's parent. `fade` scales the whole card's opacity.
        public void Place(in CardPose pose, float fade = 1f)
        {
            root.gameObject.SetActive(pose.Alpha * fade > 0f);
            root.localPosition = pose.Position;
            root.localRotation = pose.Rotation;
            root.localScale = Vector3.one * (pose.Scale / UnitsPerMetre);
            group.alpha = pose.Alpha * fade;
            textGroup.alpha = pose.TextAlpha;
            // Cards overlap as they fold, and the one nearer the middle is drawn over its neighbour.
            canvas.sortingOrder = -Mathf.RoundToInt(Mathf.Abs(pose.Position.x) * 1000f);
        }

        public void Hide() => root.gameObject.SetActive(false);

        public bool Shown => root.gameObject.activeSelf;

        public Texture Photo => photo.texture;

        /// True when the line from `eye` through `through` crosses the card's photo frame.
        public bool InLine(Vector3 eye, Vector3 through)
        {
            var rect = frame.rectTransform;
            return SightLine.Hits(eye, through, rect.position, rect.rotation, rect.sizeDelta * rect.lossyScale.x);
        }

        static CanvasGroup Passive(CanvasGroup canvasGroup)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            return canvasGroup;
        }

        static T New<T>(string name, RectTransform parent, Material material) where T : MaskableGraphic
        {
            var graphic = new GameObject(name, typeof(RectTransform), typeof(T)).GetComponent<T>();
            graphic.raycastTarget = false;
            graphic.material = material;
            graphic.rectTransform.SetParent(parent, false);
            return graphic;
        }

        static Text Label(string name, RectTransform column, Font font, Material material, int fontSize, FontStyle style, float height)
        {
            var text = New<Text>(name, column, material);
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = Bright;
            text.alignment = TextAnchor.UpperCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            var shadow = text.gameObject.AddComponent<UnityEngine.UI.Shadow>();
            shadow.effectColor = ShadowColor;
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(Width, height);
            rect.anchoredPosition = Vector2.zero;
            return text;
        }

        static void Anchor(RectTransform rect, Vector2 size, Vector2 position)
        {
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }
    }
}
