using Edith.Snapshots;
using UnityEngine;
using UnityEngine.UI;

namespace Edith.Ask
{
    /// The card an answer is written on. It rests a little below the line of sight and follows it closely, with only
    /// enough smoothing to hide head tremor. A fist carries it; let go, it is pinned there and stops following.
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
        // Rates of the exponential approach, per second: 20 closes 63% of the gap in 50 ms.
        const float FollowRate = 20f, HoldRate = 25f;
        const float FadeSeconds = 0.3f, FlickMetres = 0.45f;
        // The grab and tap tests treat the card as a box seen face-on from this far in front of it.
        const float BoxDepth = 0.4f;

        Transform eye;
        Text text;
        CanvasGroup group;
        bool held;
        Vector3 holdOffset;
        Quaternion holdRotation;
        Vector3 holdPosition;
        Quaternion holdTarget;
        bool leaving;
        float leaveProgress;
        Vector3 leaveFrom, leaveDirection;

        public int Id { get; private set; }
        public bool Pinned { get; private set; }
        public bool Leaving => leaving;

        /// As a snapshot's box reads to the grab and tap tests.
        public SightBox Box
        {
            get
            {
                var half = Size * (0.5f / UnitsPerMetre / BoxDepth);
                var from = new Pose(transform.position - transform.forward * BoxDepth, transform.rotation);
                return new SightBox(from, Rect.MinMaxRect(-half.x, -half.y, half.x, half.y), BoxDepth);
            }
        }

        public static AnswerCard Create(int id, Transform eye, Font font, Material material)
        {
            var root = new GameObject($"AnswerCard {id}", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
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
            card.Id = id;
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

        public void BeginHold(Vector3 grip, Quaternion hand)
        {
            if (leaving) return;
            var toHand = Quaternion.Inverse(hand);
            holdOffset = toHand * (transform.position - grip);
            holdRotation = toHand * transform.rotation;
            holdPosition = transform.position;
            holdTarget = transform.rotation;
            held = true;
            enabled = true;
        }

        public void HoldAt(Vector3 grip, Quaternion hand)
        {
            if (!held) return;
            holdPosition = grip + hand * holdOffset;
            holdTarget = hand * holdRotation;
        }

        /// Stops following and stays where it is now.
        public void Pin()
        {
            held = false;
            Pinned = true;
            enabled = false;
        }

        /// Slides out sideways while it fades, then destroys itself. -1 is left, 1 right and 0 fades in place.
        public void Leave(float direction)
        {
            if (leaving) return;
            leaving = true;
            held = false;
            leaveFrom = transform.position;
            leaveDirection = transform.right * direction;
            enabled = true;
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
            if (held)
            {
                var toHand = 1f - Mathf.Exp(-HoldRate * dt);
                transform.SetPositionAndRotation(Vector3.Lerp(transform.position, holdPosition, toHand),
                    Quaternion.Slerp(transform.rotation, holdTarget, toHand));
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
