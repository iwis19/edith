using Edith.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Edith.Navigation
{
    /// The card under the arrow, in the glass look of the other panels: the place's name, its street address, and one
    /// outlined line with the next turn or the time and distance left. An empty line is left out, the card fits its
    /// text, and its top edge sits at the canvas centre.
    public sealed class NavCard
    {
        const float Pad = 16f, Gap = 4f, MinInner = 150f, MaxInner = 300f;
        const float BoxHeight = 38f, BoxPad = 18f;
        const int TitleSize = 27, AddressSize = 19, LineSize = 20;
        const int AddressMaxChars = 38;

        readonly Glass card, box;
        readonly Text title, address, line;
        string shown;

        /// The card's glass, as laid out for the text shown last.
        public RectTransform Rect => card.Rect;

        public NavCard(RectTransform root, Font font, Material material)
        {
            var accent = Theme.Accent;
            card = Glass.Build(root, "Card", new Vector2(MinInner + 2f * Pad, 100f), 24f, material);
            card.SetLook(Theme.PanelFill, new Color(1f, 1f, 1f, 0.14f), accent, new Color(accent.r, accent.g, accent.b, 0.3f));
            box = Glass.Build(card.Rect, "Line", new Vector2(MinInner, BoxHeight), BoxHeight * 0.5f, material);
            box.SetLook(Theme.InnerFill, new Color(1f, 1f, 1f, 0.06f), new Color(accent.r, accent.g, accent.b, 0.5f), Color.clear);
            title = Theme.Label(card.Rect, "Title", Theme.SemiBold(font), TitleSize, Theme.Ice, TextAnchor.MiddleCenter, material);
            address = Theme.Label(card.Rect, "Address", Theme.Regular(font), AddressSize, Theme.Muted, TextAnchor.MiddleCenter, material);
            line = Theme.Label(box.Rect, "Text", Theme.Regular(font), LineSize, Theme.Ice, TextAnchor.MiddleCenter, material, shadow: false);
        }

        /// Shows `name`, `street` and `guide`, any of which may be empty.
        public void Show(string name, string street, string guide)
        {
            name ??= "";
            street = Shorten(street ?? "");
            guide ??= "";
            var key = name + "\n" + street + "\n" + guide;
            if (key == shown) return;
            shown = key;

            title.text = name;
            address.text = street;
            line.text = guide;
            title.horizontalOverflow = address.horizontalOverflow = line.horizontalOverflow = HorizontalWrapMode.Overflow;
            var inner = Mathf.Clamp(Mathf.Max(Width(title), Width(address), guide.Length > 0 ? Width(line) + 2f * BoxPad : 0f),
                MinInner, MaxInner);

            // A long name wraps; the address is cut short instead, so the card stays three lines at most.
            title.horizontalOverflow = HorizontalWrapMode.Wrap;
            title.rectTransform.sizeDelta = new Vector2(inner, 400f);
            var titleHeight = name.Length > 0 ? Mathf.Max(title.preferredHeight, TitleSize * 1.3f) : 0f;
            var addressHeight = street.Length > 0 ? AddressSize * 1.45f : 0f;
            var boxHeight = guide.Length > 0 ? BoxHeight : 0f;
            var height = Pad + titleHeight + (addressHeight > 0f ? Gap + addressHeight : 0f)
                + (boxHeight > 0f ? 3f * Gap + boxHeight : 0f) + Pad;

            card.Resize(new Vector2(inner + 2f * Pad, height));
            card.Rect.anchoredPosition = new Vector2(0f, -height * 0.5f);
            var y = height * 0.5f - Pad;
            Place(title, name.Length > 0, inner, titleHeight, ref y, 0f);
            Place(address, street.Length > 0, inner, addressHeight, ref y, Gap);
            box.Rect.gameObject.SetActive(guide.Length > 0);
            if (guide.Length > 0)
            {
                y -= 3f * Gap;
                box.Resize(new Vector2(inner, boxHeight));
                box.Rect.anchoredPosition = new Vector2(0f, y - boxHeight * 0.5f);
                line.rectTransform.sizeDelta = new Vector2(inner, boxHeight);
                line.rectTransform.anchoredPosition = Vector2.zero;
            }
        }

        static float Width(Text text) => text.text.Length > 0 ? text.preferredWidth : 0f;

        /// Lays `text` out `height` tall under `y`, after a `gap`, and moves `y` past it; hides it when not `shown`.
        static void Place(Text text, bool shown, float width, float height, ref float y, float gap)
        {
            text.gameObject.SetActive(shown);
            if (!shown) return;
            y -= gap;
            text.rectTransform.sizeDelta = new Vector2(width, height);
            text.rectTransform.anchoredPosition = new Vector2(0f, y - height * 0.5f);
            y -= height;
        }

        static string Shorten(string street) =>
            street.Length <= AddressMaxChars ? street : street.Substring(0, AddressMaxChars - 1).TrimEnd() + "…";
    }
}
