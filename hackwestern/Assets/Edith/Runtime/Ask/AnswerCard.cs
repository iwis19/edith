using UnityEngine;
using UnityEngine.UI;

namespace Edith.Ask
{
    /// The card an answer is written on. It rests a little below the line of sight and follows it closely, with only
    /// enough smoothing to hide head tremor.
    public sealed class AnswerCard : MonoBehaviour
    {
        // Four canvas units per millimetre, so the font is rasterised sharply enough at this size.
        const float UnitsPerMetre = 4000f;
        static readonly Vector2 Size = new Vector2(1120f, 520f);
        const float Pad = 48f;
        const int FontSize = 44;
        static readonly Color Backing = new Color(0.04f, 0.05f, 0.07f, 0.65f);
        static readonly Color Quiet = new Color(0.78f, 0.8f, 0.85f, 0.8f);
        static readonly Color Bright = new Color(0.97f, 0.97f, 0.99f, 1f);

        // Where it rests: this far along the line of sight, this far below it.
        const float Ahead = 0.55f, Below = 0.12f;
        // The rate of the exponential approach, per second: 20 closes 63% of the gap in 50 ms.
        const float FollowRate = 20f;
        const float FadeSeconds = 0.3f, FlickMetres = 0.45f;

        Transform eye;
        Text text;
        CanvasGroup group;
        bool leaving;
        float leaveProgress;
        Vector3 leaveFrom, leaveDirection;

        public static AnswerCard Create(Transform eye, Font font, Material material)
        {
            var root = new GameObject("AnswerCard", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = Size;
            rect.localScale = Vector3.one / UnitsPerMetre;
            root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;

            var backing = new GameObject("Backing", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            backing.color = Backing;
            backing.raycastTarget = false;
            backing.material = material;
            Fill(backing.rectTransform, rect, 0f);

            var label = new GameObject("Text", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            label.font = font;
            label.fontSize = FontSize;
            label.alignment = TextAnchor.UpperLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.raycastTarget = false;
            label.material = material;
            Fill(label.rectTransform, rect, Pad);

            var card = root.AddComponent<AnswerCard>();
            card.eye = eye;
            card.text = label;
            card.group = root.GetComponent<CanvasGroup>();
            card.group.interactable = false;
            card.group.blocksRaycasts = false;
            var rest = Rest(eye);
            rect.SetPositionAndRotation(rest, Facing(eye, rest));
            card.ShowListening();
            return card;
        }

        public void ShowListening() => Show("listening…", Quiet);
        public void ShowAnswer(string answer) => Show(answer, Bright);
        public void ShowProblem(string line) => Show(line, Quiet);

        void Show(string value, Color color)
        {
            text.text = value;
            text.color = color;
        }

        /// Slides out sideways while it fades, then destroys itself. -1 is left, 1 right and 0 fades in place.
        public void Leave(float direction)
        {
            if (leaving) return;
            leaving = true;
            leaveFrom = transform.position;
            leaveDirection = transform.right * direction;
        }

        void LateUpdate()
        {
            var dt = Time.deltaTime;
            if (leaving)
            {
                leaveProgress = Mathf.MoveTowards(leaveProgress, 1f, dt / FadeSeconds);
                transform.position = leaveFrom + leaveDirection * (FlickMetres * leaveProgress * leaveProgress);
                group.alpha = 1f - leaveProgress;
                if (leaveProgress >= 1f) Destroy(gameObject);
                return;
            }
            var follow = 1f - Mathf.Exp(-FollowRate * dt);
            var position = Vector3.Lerp(transform.position, Rest(eye), follow);
            transform.SetPositionAndRotation(position, Quaternion.Slerp(transform.rotation, Facing(eye, position), follow));
        }

        /// Below the line of sight rather than straight down, so looking up or down carries the card with it.
        static Vector3 Rest(Transform eye) => eye.position + eye.forward * Ahead - eye.up * Below;

        /// Faces the eye and stays upright.
        static Quaternion Facing(Transform eye, Vector3 position) => Quaternion.LookRotation(position - eye.position, Vector3.up);

        static void Fill(RectTransform child, RectTransform parent, float inset)
        {
            child.SetParent(parent, false);
            child.anchorMin = Vector2.zero;
            child.anchorMax = Vector2.one;
            child.offsetMin = new Vector2(inset, inset);
            child.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
