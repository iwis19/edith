using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Edith.Ask
{
    /// The card an answer is written on, under one quoted line of what was heard. It rests a little below the line of
    /// sight and follows it closely, with only enough smoothing to hide head tremor.
    public sealed class AnswerCard : MonoBehaviour
    {
        // Four canvas units per millimetre, so the font is rasterised sharply enough at this size.
        const float UnitsPerMetre = 4000f;
        static readonly Vector2 Size = new Vector2(1120f, 520f);
        const float Pad = 48f;
        const int FontSize = 44;
        const int HeardFontSize = 34;
        const float HeardHeight = 56f;
        const int SourcesFontSize = 30;
        const float SourcesHeight = 48f;
        const int MaxSources = 3;
        /// The longest heard line, about what fits on one line of the card.
        public const int MaxHeardChars = 50;
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
        Text heard, sources;
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

            var heardLabel = new GameObject("Heard", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            heardLabel.font = font;
            heardLabel.fontSize = HeardFontSize;
            heardLabel.color = Quiet;
            heardLabel.alignment = TextAnchor.UpperLeft;
            heardLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            heardLabel.verticalOverflow = VerticalWrapMode.Truncate;
            heardLabel.raycastTarget = false;
            heardLabel.material = material;
            var heardRect = heardLabel.rectTransform;
            heardRect.SetParent(rect, false);
            heardRect.anchorMin = new Vector2(0f, 1f);
            heardRect.anchorMax = Vector2.one;
            heardRect.offsetMin = new Vector2(Pad, -(Pad + HeardHeight));
            heardRect.offsetMax = new Vector2(-Pad, -Pad);

            var sourcesLabel = new GameObject("Sources", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            sourcesLabel.font = font;
            sourcesLabel.fontSize = SourcesFontSize;
            sourcesLabel.color = Quiet;
            sourcesLabel.alignment = TextAnchor.LowerLeft;
            sourcesLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            sourcesLabel.verticalOverflow = VerticalWrapMode.Truncate;
            sourcesLabel.raycastTarget = false;
            sourcesLabel.material = material;
            var sourcesRect = sourcesLabel.rectTransform;
            sourcesRect.SetParent(rect, false);
            sourcesRect.anchorMin = Vector2.zero;
            sourcesRect.anchorMax = new Vector2(1f, 0f);
            sourcesRect.offsetMin = new Vector2(Pad, Pad);
            sourcesRect.offsetMax = new Vector2(-Pad, Pad + SourcesHeight);

            var card = root.AddComponent<AnswerCard>();
            card.sources = sourcesLabel;
            card.eye = eye;
            card.text = label;
            card.heard = heardLabel;
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

        /// Shows `words`, what was heard so far, as HeardLine puts it, with the answer below it.
        public void ShowHeard(string words)
        {
            heard.text = HeardLine(words);
            text.rectTransform.offsetMax = new Vector2(-Pad, heard.text.Length == 0 ? -Pad : -(Pad + HeardHeight));
        }

        /// Shows up to three of `names` as one quiet line under the answer; nothing when there are none.
        public void ShowSources(IReadOnlyList<string> names)
        {
            sources.text = SourcesLine(names);
            text.rectTransform.offsetMin = new Vector2(Pad, sources.text.Length == 0 ? Pad : Pad + SourcesHeight);
        }

        /// The first three of `names` joined by a middle dot, led by "Sources:". Empty when there are none.
        public static string SourcesLine(IReadOnlyList<string> names)
        {
            if (names == null || names.Count == 0) return "";
            return "Sources: " + string.Join(" · ", names.Take(MaxSources));
        }

        /// `words` with its spacing tidied, in quotes. Past MaxHeardChars it keeps the latest words, from a word break.
        /// Empty when nothing was heard.
        public static string HeardLine(string words)
        {
            var tidy = string.Join(" ", (words ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
            if (tidy.Length == 0) return "";
            if (tidy.Length > MaxHeardChars)
            {
                var cut = tidy.Length - MaxHeardChars;
                var space = tidy.IndexOf(' ', cut);
                tidy = "…" + (space >= 0 ? tidy.Substring(space + 1) : tidy.Substring(cut));
            }
            return "“" + tidy + "”";
        }

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
