using Edith.Snapshots;
using UnityEngine;
using UnityEngine.UI;

namespace Edith.Scrapbook
{
    /// One card of the deck, drawn like a snapshot's card: the photo at its own shape, a thin border in the rarity's
    /// colour, and the fun fact under it in the same colour. The name stands in when there is no fact. A memory's
    /// text is already typed out. Also draws the Scrapbook's notices, which are text with no photo.
    public sealed class MemoryCard
    {
        // The canvas is laid out in millimetres, as a snapshot's is.
        const float UnitsPerMetre = 1000f;
        // The photo is fitted into this room, in millimetres.
        static readonly Vector2 Room = new Vector2(DeckLayout.CardWidth, DeckLayout.CardHeight) * UnitsPerMetre;
        // A tall photo is widened to at least this, growing taller up to MaxPhotoMm, so its fact wraps into a few
        // readable lines rather than a thin column. Millimetres.
        public const float MinPhotoMm = CardView.MinPhotoTangent * ScrapbookPlacement.CardAhead * UnitsPerMetre, MaxPhotoMm = 260f;
        const int NoticeTitleSize = 44, NoticeBodySize = 30;
        static readonly Color EmptyPhoto = new Color(1f, 1f, 1f, 0.35f);
        static readonly Color Bright = new Color(0.97f, 0.97f, 0.99f, 1f);
        static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.85f);

        RectTransform root;
        Canvas canvas;
        CanvasGroup group;
        RawImage photo;
        CardView view;
        Text notice;
        bool fitted;

        public static MemoryCard Build(Transform parent, Font font, Material material)
        {
            var card = new MemoryCard();
            var go = new GameObject("Card", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            card.root = (RectTransform)go.transform;
            card.root.SetParent(parent, false);
            card.root.sizeDelta = Room;
            card.canvas = go.GetComponent<Canvas>();
            card.canvas.renderMode = RenderMode.WorldSpace;
            card.group = go.GetComponent<CanvasGroup>();
            card.group.interactable = false;
            card.group.blocksRaycasts = false;

            var image = new GameObject("Photo", typeof(RectTransform), typeof(RawImage));
            var rect = (RectTransform)image.transform;
            rect.SetParent(card.root, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            card.photo = image.GetComponent<RawImage>();
            card.photo.raycastTarget = false;
            card.photo.material = material;
            card.view = CardView.Build(rect, Room, ScrapbookPlacement.CardAhead, font, material);
            card.notice = NewNotice(card.root, font, material);
            card.SetPhoto(null);
            card.Hide();
            return card;
        }

        /// Shows `memory`: its fact, or its name when it has none.
        public void Show(Memory memory)
        {
            photo.gameObject.SetActive(true);
            notice.gameObject.SetActive(false);
            view.Show(memory.Tier, string.IsNullOrEmpty(memory.Fact) ? memory.Name : memory.Fact);
        }

        /// Shows `title` and `body` as plain text with no photo.
        public void Show(string title, string body)
        {
            photo.gameObject.SetActive(false);
            notice.gameObject.SetActive(true);
            notice.text = $"<size={NoticeTitleSize}><b>{title}</b></size>" + (string.IsNullOrEmpty(body) ? "" : $"\n<size={NoticeBodySize}>{body}</size>");
        }

        /// Shows `texture` at its own shape, as large as fits the card. Null shows a plain panel until there is one.
        public void SetPhoto(Texture texture)
        {
            if (fitted && photo.texture == texture) return;
            fitted = true;
            photo.texture = texture;
            photo.color = texture == null ? EmptyPhoto : Color.white;
            var size = Room;
            if (texture != null && texture.width > 0 && texture.height > 0)
            {
                var shape = texture.width / (float)texture.height;
                size = shape > Room.x / Room.y ? new Vector2(Room.x, Room.x / shape) : new Vector2(Room.y * shape, Room.y);
                if (size.x < MinPhotoMm) size = new Vector2(MinPhotoMm, MinPhotoMm / shape);
                if (size.y > MaxPhotoMm) size = new Vector2(MaxPhotoMm * shape, MaxPhotoMm);
            }
            photo.rectTransform.sizeDelta = size;
            // The bottom edge stays where the room's is, so a taller photo grows upward and its fact never moves down
            // onto the rarity chips.
            photo.rectTransform.anchoredPosition = new Vector2(0f, (size.y - Room.y) * 0.5f);
            view.Fit(size);
        }

        /// `pose` is in the frame of the card's parent. `fade` scales the whole card's opacity.
        public void Place(in CardPose pose, float fade = 1f)
        {
            root.gameObject.SetActive(pose.Alpha * fade > 0f);
            root.localPosition = pose.Position;
            root.localRotation = pose.Rotation;
            root.localScale = Vector3.one * (pose.Scale / UnitsPerMetre);
            group.alpha = pose.Alpha * fade;
            view.TextAlpha = pose.TextAlpha;
            // Cards overlap as they fold, and the one nearer the middle is drawn over its neighbour.
            canvas.sortingOrder = -Mathf.RoundToInt(Mathf.Abs(pose.Position.x) * 1000f);
        }

        public void Hide() => root.gameObject.SetActive(false);

        public bool Shown => root.gameObject.activeSelf;

        public Texture Photo => photo.texture;

        /// True when the line from `eye` through `through` crosses the card's photo, or passes within `margin` metres of it.
        public bool InLine(Vector3 eye, Vector3 through, float margin = 0f)
        {
            var rect = photo.rectTransform;
            return SightLine.Hits(eye, through, rect.position, rect.rotation, rect.sizeDelta * rect.lossyScale.x, margin);
        }

        /// The photo's centre in the room.
        public Vector3 Centre => photo.rectTransform.position;

        static Text NewNotice(RectTransform parent, Font font, Material material)
        {
            var column = new GameObject("Notice", typeof(RectTransform)).GetComponent<RectTransform>();
            column.SetParent(parent, false);
            column.localScale = Vector3.one * (CardView.ScaleAt(ScrapbookPlacement.CardAhead) / CardView.TextUnitsPerMm);
            var text = new GameObject("Message", typeof(RectTransform), typeof(Text), typeof(UnityEngine.UI.Shadow)).GetComponent<Text>();
            text.rectTransform.SetParent(column, false);
            text.rectTransform.sizeDelta = new Vector2(Room.x * CardView.TextUnitsPerMm / CardView.ScaleAt(ScrapbookPlacement.CardAhead), 400f);
            text.font = font;
            text.supportRichText = true;
            text.color = Bright;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.material = material;
            var shadow = text.GetComponent<UnityEngine.UI.Shadow>();
            shadow.effectColor = ShadowColor;
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
            return text;
        }
    }
}
