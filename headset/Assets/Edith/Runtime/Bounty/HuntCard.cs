using System.Globalization;
using System.Text;
using Edith.Ask;
using UnityEngine;
using UnityEngine.UI;

namespace Edith.Bounty
{
    /// A small list of the hunt's targets, high and to the right of the view, that trails the head lazily so it
    /// never sits where things are framed. A found target is ticked. It shows the count found and the time left, and
    /// once the hunt ends, the result.
    public sealed class HuntCard : MonoBehaviour
    {
        // Four canvas units per millimetre, so the font is rasterised sharply at this size.
        const float UnitsPerMetre = 4000f;
        const float WidthMetres = 0.24f, HeightMetres = 0.135f, PaddingMetres = 0.008f;
        const int FontSize = 46, LineHeight = 62;
        /// Where the card's centre rests: tangents of the angle from the line of sight, up and to the right.
        static readonly Vector2 Spot = new Vector2(0.42f, 0.32f);
        const float Depth = 0.7f;
        const float FollowRate = 3f, TurnRate = 8f, FadeSeconds = 0.3f;
        static readonly Color Bright = new Color(0.97f, 0.97f, 0.99f, 1f);
        static readonly Color Done = new Color(0.5f, 0.88f, 0.55f, 1f);
        static readonly Color Urgent = new Color(1f, 0.45f, 0.4f, 1f);
        static readonly Color Back = new Color(0.04f, 0.05f, 0.08f, 0.72f);
        static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.85f);
        const string DoneTag = "<color=#80e08c>";

        Transform eye;
        CanvasGroup group;
        Text header, clock, body;
        float fade;
        bool shown;
        string lastBody = "";

        public static HuntCard Create(Transform eye, Font font, Material material)
        {
            var root = new GameObject("HuntCard", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(WidthMetres, HeightMetres) * UnitsPerMetre;
            rect.localScale = Vector3.one / UnitsPerMetre;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 1;

            var card = root.AddComponent<HuntCard>();
            card.eye = eye;
            card.group = root.GetComponent<CanvasGroup>();
            card.group.alpha = 0f;
            card.group.interactable = false;
            card.group.blocksRaycasts = false;

            var back = new GameObject("Back", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            back.color = Back;
            back.raycastTarget = false;
            back.material = material;
            Stretch(back.rectTransform, rect, 0f);

            var pad = PaddingMetres * UnitsPerMetre;
            card.header = Label("Header", rect, font, material, TextAnchor.UpperLeft, pad);
            card.clock = Label("Clock", rect, font, material, TextAnchor.UpperRight, pad);
            card.body = Label("Targets", rect, font, material, TextAnchor.UpperLeft, pad);
            card.body.supportRichText = true;
            card.body.rectTransform.offsetMax = new Vector2(-pad, -pad - LineHeight);
            card.transform.SetPositionAndRotation(card.Goal(), card.Facing(card.Goal()));
            return card;
        }

        /// Fades in while `visible` and out otherwise.
        public void SetVisible(bool visible) => shown = visible;

        public bool Visible => shown;

        /// Writes the tracker's hunt onto the card.
        public void Show(HuntTracker hunt)
        {
            var ended = hunt.Phase == HuntPhase.Ended;
            header.text = Header(hunt);
            header.color = ended && hunt.Ending == HuntEnd.FoundAll ? Done : Bright;
            clock.text = hunt.Phase == HuntPhase.Active ? Clock(hunt.Remaining) : "";
            clock.color = hunt.Phase == HuntPhase.Active && hunt.Remaining <= HuntTracker.TickSeconds[0] ? Urgent : Bright;
            var lines = Lines(hunt);
            if (lines == lastBody) return;
            lastBody = lines;
            body.text = lines;
        }

        /// "m:ss", rounding a part second up so the clock reads 0:01 until time is up.
        public static string Clock(float seconds)
        {
            var whole = Mathf.CeilToInt(Mathf.Max(0f, seconds));
            return (whole / 60).ToString(CultureInfo.InvariantCulture) + ":" + (whole % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        public static string Header(HuntTracker hunt)
        {
            var found = hunt.Found + " of " + hunt.Targets.Count;
            if (hunt.Phase != HuntPhase.Ended) return "BOUNTY  " + found;
            switch (hunt.Ending)
            {
                case HuntEnd.FoundAll: return "All " + hunt.Targets.Count + " found!";
                case HuntEnd.Timeout: return "Time's up, found " + found;
                default: return "Hunt stopped, found " + found;
            }
        }

        /// One line per target, in order; a found one is ticked and tinted.
        public static string Lines(HuntTracker hunt)
        {
            var lines = new StringBuilder();
            foreach (var target in hunt.Targets)
            {
                if (lines.Length > 0) lines.Append('\n');
                if (target.Found) lines.Append(DoneTag).Append("✓ ").Append(target.Text).Append("</color>");
                else lines.Append("- ").Append(target.Text);
            }
            return lines.ToString();
        }

        Vector3 Goal() => eye.position + eye.rotation * new Vector3(Spot.x * Depth, Spot.y * Depth, Depth);

        Quaternion Facing(Vector3 position) => OrbPlacement.FacingEye(new Pose(eye.position, eye.rotation), position);

        void LateUpdate()
        {
            var dt = Time.deltaTime;
            fade = Mathf.MoveTowards(fade, shown ? 1f : 0f, dt / FadeSeconds);
            group.alpha = fade * fade * (3f - 2f * fade);
            if (fade <= 0f) return;
            var goal = Goal();
            var position = Vector3.Lerp(transform.position, goal, 1f - Mathf.Exp(-FollowRate * dt));
            var rotation = Quaternion.Slerp(transform.rotation, Facing(position), 1f - Mathf.Exp(-TurnRate * dt));
            transform.SetPositionAndRotation(position, rotation);
        }

        /// Snaps to the resting spot, as when the card first appears, so it does not slide in from where it was last.
        public void Snap() => transform.SetPositionAndRotation(Goal(), Facing(Goal()));

        static Text Label(string name, RectTransform parent, Font font, Material material, TextAnchor anchor, float pad)
        {
            var text = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Shadow)).GetComponent<Text>();
            text.font = font;
            text.fontSize = FontSize;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.material = material;
            text.lineSpacing = 1.05f;
            var shadow = text.GetComponent<Shadow>();
            shadow.effectColor = ShadowColor;
            shadow.effectDistance = new Vector2(2f, -2f);
            Stretch(text.rectTransform, parent, pad);
            return text;
        }

        static void Stretch(RectTransform rect, RectTransform parent, float pad)
        {
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(pad, pad);
            rect.offsetMax = new Vector2(-pad, -pad);
        }
    }
}
