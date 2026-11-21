using Edith.Snapshots;
using UnityEngine;
using UnityEngine.UI;

namespace Edith.Scrapbook
{
    /// One card of the deck, drawn like a snapshot's card: the photo at its own shape in glass outlined in the rarity's
    /// colour, and the fun fact under it. The name stands in when there is no fact. A memory's
    /// text is already typed out. Also draws the Scrapbook's notices, which are text with no photo.
    public sealed class MemoryCard
    {
        // The canvas is laid out in millimetres, as a snapshot's is.
        const float UnitsPerMetre = 1000f;
        // The photo is fitted into this room, in millimetres.
        static readonly Vector2 Room = new Vector2(DeckLayout.CardWidth, DeckLayout.CardHeight) * UnitsPerMetre;
        // A tall photo is widened to at least this, growing taller up to MaxPhotoMm, so its fact wraps into a few
        // readable lines rather than a thin column. A photo too tall for both is cropped top and bottom, never shown
        // narrower. Millimetres.
        public const float MinPhotoMm = CardWidth.MinPhotoTangent * ScrapbookPlacement.CardAhead * UnitsPerMetre, MaxPhotoMm = 260f;
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
            view.Show(memory.Tier, memory.Name, memory.Fact);
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
            var size = ShownSize(texture);
            photo.rectTransform.sizeDelta = size;
            photo.uvRect = Crop(texture, size);
            // The bottom edge stays where the room's is, so a taller photo grows upward and its fact never moves down
            // onto the rarity chips.
            photo.rectTransform.anchoredPosition = new Vector2(0f, (size.y - Room.y) * 0.5f);
            view.Fit(size);
        }

        /// A copy of `texture` with its corners cut round to sit inside the card's frame, as a snapshot's photo is,
        /// or null when it can't be read.
        public static Texture2D Rounded(Texture2D texture)
        {
            if (texture == null || !texture.isReadable) return null;
            var size = ShownSize(texture);
            var radius = CardView.PhotoRadiusMm(ScrapbookPlacement.CardAhead) / size.x;
            var crop = Crop(texture, size);
            if (crop.height >= 1f) return Snapshot.Rounded(texture, radius);
            // Cut the rows the card will not show first, so the rounded corners are the ones in view.
            int rows = Mathf.Max(1, Mathf.RoundToInt(texture.height * crop.height)), from = (texture.height - rows) / 2;
            var cut = new Texture2D(texture.width, rows, TextureFormat.RGBA32, false);
            cut.SetPixels(texture.GetPixels(0, from, texture.width, rows));
            cut.Apply(false, false);
            var rounded = Snapshot.Rounded(cut, radius);
            if (Application.isPlaying) Object.Destroy(cut); else Object.DestroyImmediate(cut);
            return rounded;
        }

        /// The part of `texture` a card `size` millimetres shows: all of it, or a band through its middle when the
        /// photo is too tall for the card.
        static Rect Crop(Texture texture, Vector2 size)
        {
            if (texture == null || texture.width <= 0 || texture.height <= 0) return new Rect(0f, 0f, 1f, 1f);
            var shown = (texture.width / (float)texture.height) / (size.x / size.y);
            return shown >= 0.999f ? new Rect(0f, 0f, 1f, 1f) : new Rect(0f, (1f - shown) * 0.5f, 1f, shown);
        }

        /// The photo's size on the card, in millimetres: `texture` at its own shape, as large as fits the room.
        static Vector2 ShownSize(Texture texture)
        {
            if (texture == null || texture.width <= 0 || texture.height <= 0) return Room;
            var shape = texture.width / (float)texture.height;
            var size = shape > Room.x / Room.y ? new Vector2(Room.x, Room.x / shape) : new Vector2(Room.y * shape, Room.y);
            if (size.x < MinPhotoMm) size = new Vector2(MinPhotoMm, MinPhotoMm / shape);
            if (size.y > MaxPhotoMm) size = new Vector2(Mathf.Max(MaxPhotoMm * shape, MinPhotoMm), MaxPhotoMm);
            return size;
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
