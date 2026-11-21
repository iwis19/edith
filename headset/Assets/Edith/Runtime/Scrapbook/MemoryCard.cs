using Edith.Look;
using UnityEngine;
using UnityEngine.UI;

namespace Edith.Scrapbook
{
    /// One card of the deck: the photo at its own shape inside a thin border of the rarity's colour, with the rarity's
    /// name in a box on the photo's top-right corner, and under it the fun fact and a line with the place on the left
    /// and the day on the right, like a snapshot's card. The name shows only when there is no fact. Also draws the
    /// Scrapbook's notices, which are cards with no photo.
    public sealed class MemoryCard
    {
        // Four canvas units per millimetre, so the font is rasterised sharply enough at this size.
        const float UnitsPerMetre = 4000f;
        const float Width = DeckLayout.CardWidth * UnitsPerMetre;
        const float FrameHeight = 600f, TextHeight = 240f;
        const float FrameInset = 8f, Gap = 12f;
        const int NameSize = 44, FactSize = 30, CaptionSize = 28, BadgeSize = 22;
        const float NameHeight = 52f, FactLineHeight = 35f, FactMaxHeight = 4f * FactLineHeight, CaptionHeight = 36f;
        // The day, "11/21", takes this much of the last line, and the place the rest.
        const float DayWidth = 110f;
        // The badge sits 4 mm in from the photo's corner.
        const float BadgeInset = 16f, BadgeHeight = 36f, BadgePad = 12f;
        static readonly Color BadgeInk = new Color(0.04f, 0.05f, 0.07f, 1f);
        static readonly Color Bright = new Color(0.97f, 0.97f, 0.99f, 1f);
        static readonly Color Quiet = new Color(0.78f, 0.8f, 0.85f, 0.8f);
        static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.85f);
        /// The frame of a memory that has no rarity, and of a notice.
        public static readonly Color Untiered = new Color(0.95f, 0.95f, 0.97f, 0.35f);

        RectTransform root;
        Canvas canvas;
        CanvasGroup group, textGroup;
        Image frame;
        RawImage photo;
        Image badge;
        Text name, fact, place, day, badgeLabel;
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
            card.photo = New<RawImage>("Photo", card.frame.rectTransform, material);
            card.badge = New<Image>("BadgeBox", card.photo.rectTransform, material);
            var corner = new Vector2(1f, 1f);
            card.badge.rectTransform.anchorMin = card.badge.rectTransform.anchorMax = card.badge.rectTransform.pivot = corner;
            card.badge.rectTransform.anchoredPosition = new Vector2(-BadgeInset, -BadgeInset);
            card.badgeLabel = New<Text>("Badge", card.badge.rectTransform, material);
            card.badgeLabel.font = font;
            card.badgeLabel.fontSize = BadgeSize;
            card.badgeLabel.fontStyle = FontStyle.Bold;
            card.badgeLabel.color = BadgeInk;
            card.badgeLabel.alignment = TextAnchor.MiddleCenter;
            card.badgeLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            card.badgeLabel.verticalOverflow = VerticalWrapMode.Overflow;

            var text = new GameObject("Text", typeof(RectTransform), typeof(CanvasGroup));
            var column = (RectTransform)text.transform;
            column.SetParent(card.root, false);
            column.pivot = new Vector2(0.5f, 1f);
            Anchor(column, new Vector2(Width, TextHeight), new Vector2(0f, TextHeight * 0.5f - FrameHeight * 0.5f - Gap));
            card.textGroup = Passive(text.GetComponent<CanvasGroup>());
            card.name = Label("Name", column, font, material, NameSize, FontStyle.Bold, NameHeight, TextAnchor.UpperLeft);
            card.name.horizontalOverflow = HorizontalWrapMode.Overflow;
            card.fact = Label("Fact", column, font, material, FactSize, FontStyle.Normal, FactMaxHeight, TextAnchor.UpperLeft);
            card.fact.verticalOverflow = VerticalWrapMode.Truncate;
            card.place = Label("Place", column, font, material, CaptionSize, FontStyle.Normal, CaptionHeight, TextAnchor.UpperLeft);
            card.place.rectTransform.sizeDelta = new Vector2(Width - DayWidth, CaptionHeight);
            card.place.verticalOverflow = VerticalWrapMode.Truncate;
            card.place.color = Quiet;
            card.day = Label("Day", column, font, material, CaptionSize, FontStyle.Normal, CaptionHeight, TextAnchor.UpperRight);
            card.day.rectTransform.sizeDelta = new Vector2(DayWidth, CaptionHeight);
            card.day.color = Quiet;
            card.SetPhoto(null);
            card.Hide();
            return card;
        }

        public void Show(Memory memory) => Show(string.IsNullOrEmpty(memory.Fact) ? memory.Name : null, memory.Fact, memory.PlaceLine,
            Memory.Day(memory.Date), memory.Tier is Rarity tier ? RarityColors.For(tier) : Untiered, memory.Tier);

        /// `where` and `when` share the card's last line, and a null `tier` shows no badge.
        public void Show(string title, string body, string where, string when, Color frameColor, Rarity? tier = null)
        {
            frame.color = frameColor;
            name.text = title ?? "";
            fact.text = body ?? "";
            place.text = where ?? "";
            day.text = when ?? "";
            badge.gameObject.SetActive(tier != null);
            if (tier is Rarity badged)
            {
                badge.color = RarityColors.For(badged);
                badgeLabel.text = badged.ToString().ToUpperInvariant();
                var size = new Vector2(badgeLabel.preferredWidth + 2f * BadgePad, BadgeHeight);
                badge.rectTransform.sizeDelta = size;
                badgeLabel.rectTransform.sizeDelta = size;
            }
            // The fact sits under the title when there is one, and the last line follows the fact, however many lines
            // it wraps to.
            var titleHeight = string.IsNullOrEmpty(title) ? 0f : NameHeight;
            fact.rectTransform.anchoredPosition = new Vector2(0f, -titleHeight);
            var factHeight = string.IsNullOrEmpty(body) ? 0f : Mathf.Clamp(fact.preferredHeight, FactLineHeight, FactMaxHeight);
            var line = new Vector2(0f, -(titleHeight + factHeight + (factHeight > 0f ? Gap : 0f)));
            place.rectTransform.anchoredPosition = day.rectTransform.anchoredPosition = line;
        }

        /// Shows `texture` at its own shape, as large as fits the card, with the border hugging it. Null hides the
        /// photo and its border until there is one.
        public void SetPhoto(Texture texture)
        {
            if (fitted && photo.texture == texture) return;
            fitted = true;
            photo.texture = texture;
            frame.gameObject.SetActive(texture != null);
            var room = new Vector2(Width, FrameHeight) - 2f * FrameInset * Vector2.one;
            var size = room;
            if (texture != null && texture.width > 0 && texture.height > 0)
            {
                var shape = texture.width / (float)texture.height;
                size = shape > room.x / room.y ? new Vector2(room.x, room.x / shape) : new Vector2(room.y * shape, room.y);
            }
            Anchor(photo.rectTransform, size, Vector2.zero);
            var rect = frame.rectTransform;
            rect.sizeDelta = size + 2f * FrameInset * Vector2.one;
            // The frame grows up from a fixed bottom edge, so the text under it stays put whatever the photo's shape.
            rect.anchoredPosition = new Vector2(0f, TextHeight * 0.5f - FrameHeight * 0.5f + rect.sizeDelta.y * 0.5f);
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

        static Text Label(string name, RectTransform column, Font font, Material material, int fontSize, FontStyle style, float height, TextAnchor alignment)
        {
            var text = New<Text>(name, column, material);
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = Bright;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            var shadow = text.gameObject.AddComponent<UnityEngine.UI.Shadow>();
            shadow.effectColor = ShadowColor;
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
            var rect = text.rectTransform;
            var right = alignment == TextAnchor.UpperRight;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(right ? 1f : 0f, 1f);
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
