using System;
using System.Collections.Generic;
using Edith.Hands;
using Edith.Look;
using Edith.Pairing;
using Edith.Pinch;
using Edith.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Edith.Welcome
{
    /// The first-launch welcome in two steps. One is EDITH on her horse at the left and, at the right in glass, the
    /// interests and voice to pick. Two is the pairing code alone in glass, held where PhonePairing holds its panel
    /// and as large. The steps and what has been picked live in WelcomeFlow; the pairing itself keeps running in
    /// PhonePairing, which stays hidden while this is up. Both steps are tapped with a quick pinch, read straight from
    /// the hands because every gesture but this one stays off until a phone is paired.
    public sealed class WelcomeController : MonoBehaviour
    {
        [SerializeField] Transform eye;
        [SerializeField] Font font;
        [SerializeField] Material material;
        [SerializeField] PhonePairing pairing;
        [SerializeField] RightHandSource right;
        [SerializeField] LeftHandSource left;

        /// The person tapped a voice's play button. Playing a sample is up to whoever listens.
        public event Action<VoiceStyle> PreviewRequested;

        /// The phone paired or the person skipped pairing.
        public event Action Finished;

        // The screen is laid out in 1280 by 820 design units. 2800 units to the metre makes it 46 cm wide, held 0.55 m
        // out, which fills about 45 degrees of view.
        const float UnitsPerMetre = 2800f;
        const float Ahead = 0.55f, FollowRate = 6f, FadeSeconds = 0.35f;
        const float ScreenWidth = 1280f, ScreenHeight = 820f;
        // The pairing pane, in design units, and where it is held: 15 cm wide, 0.45 m out and 6 cm below the line of
        // sight, as PhonePairing holds its panel.
        static readonly Rect PairPane = new Rect(500f, 93f, 720f, 455f);
        const float PairWidthMetres = 0.15f, PairAhead = 0.45f, PairBelow = 0.06f;
        const float TapMargin = 10f;
        const float TypeCharsPerSecond = 55f;

        sealed class Target
        {
            public RectTransform Rect;
            public Action Tap;
            public WelcomeStep Step;
        }

        sealed class Chip
        {
            public string Name;
            public Glass Glass;
            public Text Label;
        }

        sealed class VoiceCard
        {
            public VoiceStyle Style;
            public Glass Glass, Play;
        }

        readonly WelcomeFlow flow = new WelcomeFlow();
        readonly PinchPair taps = new PinchPair(new PinchTuning());
        readonly List<Target> targets = new List<Target>();
        readonly List<Chip> chips = new List<Chip>();
        readonly List<VoiceCard> voices = new List<VoiceCard>();
        readonly TypedText speech = new TypedText(TypeCharsPerSecond);
        readonly Text[] digits = new Text[4];
        readonly Glass[] boxes = new Glass[4];

        Canvas canvas;
        CanvasGroup group;
        RectTransform screen, character, stepOne, stepTwo;
        Text speechText, link, status, counterOne, counterTwo;
        Glass spinner;
        Font regular, semibold;
        WelcomeStep shownStep = WelcomeStep.Done;
        bool phoneKnown, leaving;

        void Start()
        {
            if (WelcomeStore.Done)
            {
                enabled = false;
                return;
            }
            Build();
            if (pairing != null) pairing.Suppressed = true;
            Refresh();
        }

        void Update()
        {
            if (screen == null) return;
            WatchPhone();
            ReadTaps();
            if (flow.Step != shownStep) Enter(flow.Step);
            Draw();
        }

        void LateUpdate()
        {
            if (screen == null || group.alpha <= 0f) return;
            var follow = 1f - Mathf.Exp(-FollowRate * Time.deltaTime);
            var rest = Rest();
            var position = Vector3.Lerp(transform.position, rest, follow);
            transform.SetPositionAndRotation(position, Quaternion.Slerp(transform.rotation, Facing(position), follow));
        }

        // ---- flow

        /// Ends the pairing step once the backend keeps a phone position, however the person got there.
        void WatchPhone()
        {
            var state = pairing != null ? pairing.Flow : null;
            if (state == null || phoneKnown) return;
            if (state.Phase != PairPhase.Connected && state.Phase != PairPhase.Paired) return;
            phoneKnown = true;
            flow.PhoneConnected();
        }

        void ReadTaps()
        {
            if (right == null || left == null) return;
            var reading = taps.Step(right.ReadUngated(), left.ReadUngated());
            if (reading.Event == PinchEvent.Tap) Tap(reading.Start);
        }

        void Tap(Vector3 pinchPoint)
        {
            var toPanel = pinchPoint - eye.position;
            var normal = transform.forward;
            var along = Vector3.Dot(toPanel, normal);
            if (Mathf.Abs(along) < 1e-4f) return;
            var t = Vector3.Dot(transform.position - eye.position, normal) / along;
            if (t <= 0f) return;
            var hit = eye.position + toPanel * t;
            for (var i = targets.Count - 1; i >= 0; i--)
            {
                var target = targets[i];
                if (target.Step != flow.Step || !target.Rect.gameObject.activeInHierarchy) continue;
                var local = target.Rect.InverseTransformPoint(hit);
                var rect = target.Rect.rect;
                if (local.x < rect.xMin - TapMargin || local.x > rect.xMax + TapMargin
                    || local.y < rect.yMin - TapMargin || local.y > rect.yMax + TapMargin) continue;
                target.Tap();
                Refresh();
                return;
            }
        }

        void NextTapped()
        {
            WelcomeStore.Save(flow);
            flow.Next();
        }

        void SkipTapped()
        {
            WelcomeStore.Save(flow);
            flow.Skip();
        }

        // ---- drawing

        void Enter(WelcomeStep step)
        {
            shownStep = step;
            stepOne.gameObject.SetActive(step == WelcomeStep.Interests);
            stepTwo.gameObject.SetActive(step == WelcomeStep.Pair);
            // Done fades out in the layout it was shown in.
            if (step != WelcomeStep.Done) Arrange(step == WelcomeStep.Pair);
            Refresh();
            if (step == WelcomeStep.Done) Leave();
        }

        /// Step one fills the screen with EDITH beside it. Step two shows only its pane, scaled and held like
        /// PhonePairing's panel, with the screen shifted so the pane is centred on the canvas.
        void Arrange(bool pairOnly)
        {
            character.gameObject.SetActive(!pairOnly);
            var root = (RectTransform)transform;
            root.localScale = Vector3.one / (pairOnly ? PairPane.width / PairWidthMetres : UnitsPerMetre);
            screen.anchoredPosition = pairOnly
                ? new Vector2(ScreenWidth * 0.5f - PairPane.center.x, PairPane.center.y - ScreenHeight * 0.5f)
                : Vector2.zero;
            var rest = Rest();
            transform.SetPositionAndRotation(rest, Facing(rest));
        }

        Vector3 Rest() => shownStep == WelcomeStep.Pair
            ? eye.position + eye.forward * PairAhead - eye.up * PairBelow
            : eye.position + eye.forward * Ahead;

        void Leave()
        {
            if (leaving) return;
            leaving = true;
            // A paired phone completes the welcome. Skipping pairing also ends it, and brings the pairing panel back,
            // since the gate stays closed until a phone is paired.
            WelcomeStore.Done = true;
            if (pairing != null) pairing.Suppressed = false;
            Finished?.Invoke();
        }

        void Draw()
        {
            var goal = leaving ? 0f : 1f;
            group.alpha = Mathf.MoveTowards(group.alpha, goal, Time.deltaTime / FadeSeconds);
            canvas.enabled = group.alpha > 0f;
            if (leaving && group.alpha <= 0f)
            {
                enabled = false;
                return;
            }

            speech.Set(SpeechLine());
            speech.Advance(Time.deltaTime);
            speechText.text = speech.Render();

            if (flow.Step == WelcomeStep.Pair) DrawPairing();
            counterOne.text = counterTwo.text = flow.Counter;
        }

        string SpeechLine()
        {
            switch (flow.Step)
            {
                case WelcomeStep.Interests:
                    return "Hi, I'm EDITH! Tell me what you love and I'll find the fun in whatever you look at.";
                default:
                    return "";
            }
        }

        void DrawPairing()
        {
            var state = pairing != null ? pairing.Flow : null;
            var code = state != null && state.Phase == PairPhase.Showing ? state.Code : null;
            for (var i = 0; i < 4; i++)
            {
                var has = code != null && i < code.Length;
                digits[i].text = has ? code[i].ToString() : "";
                boxes[i].SetLook(has ? Theme.ChipFill : new Color(0.04f, 0.2f, 0.22f, 0.2f), new Color(1f, 1f, 1f, 0.1f),
                    has ? Theme.Cyan : new Color(Theme.Cyan.r, Theme.Cyan.g, Theme.Cyan.b, 0.4f), Color.clear);
            }
            link.text = state != null && state.PhoneUrl != null ? PairingText.TypedAddress(state.PhoneUrl) : "...";
            if (state == null) status.text = "This build has no EDITH server.";
            else if (code == null) status.text = state.Failures > 0 ? "Can't reach EDITH. Trying again..." : "Getting a code...";
            else
            {
                var left = Mathf.Max(0f, state.CodeExpiresAt - Time.realtimeSinceStartup);
                status.text = "Waiting for your phone... code good for " + Mathf.FloorToInt(left / 60f) + ":" + Mathf.FloorToInt(left % 60f).ToString("00");
            }
            var beat = 0.5f + 0.5f * Mathf.Sin(Time.time * 4f);
            spinner.SetLook(new Color(Theme.Cyan.r, Theme.Cyan.g, Theme.Cyan.b, Mathf.Lerp(0.25f, 0.9f, beat)), Color.clear, Theme.Ice, Color.clear);
        }

        /// Redraws the picks: which chips and voice are chosen.
        void Refresh()
        {
            if (screen == null) return;
            foreach (var chip in chips)
            {
                var on = flow.IsPicked(chip.Name);
                chip.Glass.SetLook(on ? Theme.Cyan : Theme.ChipFill, on ? Color.clear : new Color(1f, 1f, 1f, 0.1f),
                    on ? Theme.Cyan : new Color(1f, 1f, 1f, 0.45f), on ? new Color(Theme.Cyan.r, Theme.Cyan.g, Theme.Cyan.b, 0.4f) : Color.clear);
                chip.Label.color = on ? Theme.OnCyan : Theme.Ice;
            }
            foreach (var voice in voices)
            {
                var on = flow.Voice == voice.Style;
                voice.Glass.SetLook(on ? new Color(0.05f, 0.3f, 0.3f, 0.6f) : Theme.ChipFill, new Color(1f, 1f, 1f, on ? 0.18f : 0.08f),
                    on ? Theme.Cyan : new Color(Theme.Cyan.r, Theme.Cyan.g, Theme.Cyan.b, 0.4f),
                    on ? new Color(Theme.Cyan.r, Theme.Cyan.g, Theme.Cyan.b, 0.4f) : Color.clear);
            }
        }

        // ---- building

        void Build()
        {
            regular = Theme.Regular(font);
            semibold = Theme.SemiBold(font);

            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            group = gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            var root = (RectTransform)transform;
            root.sizeDelta = new Vector2(ScreenWidth, ScreenHeight);
            root.localScale = Vector3.one / UnitsPerMetre;

            screen = Child("Screen", root);
            screen.sizeDelta = root.sizeDelta;
            character = Child("Character", screen);
            BuildCharacter();
            stepOne = Child("Step 1", screen);
            stepTwo = Child("Step 2", screen);
            BuildInterests();
            BuildPairing();
            stepOne.gameObject.SetActive(false);
            stepTwo.gameObject.SetActive(false);
            Arrange(false);
        }

        void BuildCharacter()
        {
            var name = Label(character, "Name", semibold, 15, Theme.Cyan, TextAnchor.MiddleLeft, 70f, 122f, 200f, 20f);
            name.text = "EDITH";
            speechText = Label(character, "Speech", regular, 29, Theme.Ice, TextAnchor.UpperLeft, 70f, 148f, 390f, 130f);
            speechText.supportRichText = true;
            var rider = Theme.Pixels(character, "Rider", Theme.Rider, material);
            Place(rider.rectTransform, 50f, 300f, 400f, 325f);
        }

        void BuildInterests()
        {
            var card = Panel(stepOne, "Interests", 500f, 60f, 720f, 505f, 44f);
            card.SetLook(Theme.PanelFill, new Color(1f, 1f, 1f, 0.14f), Theme.Cyan, new Color(Theme.Cyan.r, Theme.Cyan.g, Theme.Cyan.b, 0.3f));
            var first = Panel(stepOne, "Pick", 525f, 83f, 670f, 187f, 28f);
            first.SetLook(new Color(0.03f, 0.15f, 0.15f, 0.3f), new Color(1f, 1f, 1f, 0.06f), new Color(Theme.Cyan.r, Theme.Cyan.g, Theme.Cyan.b, 0.5f), Color.clear);
            Label(stepOne, "Title", semibold, 29, Theme.Ice, TextAnchor.MiddleLeft, 546f, 104f, 400f, 40f).text = "What are you into?";
            counterOne = Label(stepOne, "Counter", semibold, 14, Theme.Cyan, TextAnchor.MiddleRight, 1000f, 108f, 175f, 24f);

            var measure = Label(stepOne, "Measure", regular, 21, Theme.Ice, TextAnchor.MiddleCenter, 0f, 0f, 10f, 10f);
            measure.horizontalOverflow = HorizontalWrapMode.Overflow;
            float x = 546f, y = 160f;
            foreach (var name in WelcomeFlow.AllInterests)
            {
                measure.text = name;
                var width = Mathf.Ceil(measure.preferredWidth) + 38f;
                if (x + width > 1176f)
                {
                    x = 546f;
                    y += 54f;
                }
                var chip = new Chip { Name = name, Glass = Panel(stepOne, "Chip", x, y, width, 43f, 21.5f) };
                chip.Label = Label(chip.Glass.Rect, "Label", regular, 21, Theme.Ice, TextAnchor.MiddleCenter, 0f, 0f, width, 43f, centred: true);
                chip.Label.text = name;
                chip.Label.horizontalOverflow = HorizontalWrapMode.Overflow;
                chips.Add(chip);
                var picked = name;
                AddTarget(chip.Glass.Rect, WelcomeStep.Interests, () => flow.Toggle(picked));
                x += width + 12f;
            }
            Destroy(measure.gameObject);

            var second = Panel(stepOne, "Voice", 525f, 287f, 670f, 190f, 28f);
            second.SetLook(new Color(0.03f, 0.15f, 0.15f, 0.3f), new Color(1f, 1f, 1f, 0.06f), new Color(Theme.Cyan.r, Theme.Cyan.g, Theme.Cyan.b, 0.5f), Color.clear);
            Label(stepOne, "VoiceTitle", semibold, 27, Theme.Ice, TextAnchor.MiddleLeft, 546f, 306f, 400f, 38f).text = "How should I sound?";
            AddVoice(VoiceStyle.Enthusiastic, "Enthusiastic", "Big energy. Every find is the best find.", 545f);
            AddVoice(VoiceStyle.Calm, "Calm", "Slow, warm, museum-guide quiet.", 759f);
            AddVoice(VoiceStyle.Light, "Light", "Easygoing and a little cheeky.", 973f);

            Label(stepOne, "Hint", regular, 16, Theme.Ice, TextAnchor.MiddleLeft, 530f, 506f, 420f, 24f).text = "Pinch to pick, or say them out loud";
            var next = Panel(stepOne, "Next", 1100f, 494f, 96f, 52f, 26f);
            next.SetLook(Theme.Cyan, Color.clear, Theme.Cyan, new Color(Theme.Cyan.r, Theme.Cyan.g, Theme.Cyan.b, 0.45f));
            var nextLabel = Label(next.Rect, "Label", semibold, 21, Theme.OnCyan, TextAnchor.MiddleCenter, 0f, 0f, 96f, 52f, centred: true);
            nextLabel.text = "Next";
            AddTarget(next.Rect, WelcomeStep.Interests, NextTapped);
        }

        void AddVoice(VoiceStyle style, string title, string description, float x)
        {
            const float y = 354f, w = 202f, h = 108f;
            var glass = Panel(stepOne, "Voice " + title, x, y, w, h, 22f);
            Label(glass.Rect, "Title", semibold, 21, Theme.Ice, TextAnchor.MiddleLeft, 16f, 12f, 120f, 28f, centred: false, within: new Vector2(w, h)).text = title;
            var play = Glass.Build(glass.Rect, "Play", new Vector2(34f, 34f), 17f, material);
            play.SetLook(new Color(0.04f, 0.2f, 0.22f, 0.4f), Color.clear, Theme.Ice, Color.clear);
            play.Rect.anchoredPosition = new Vector2(w * 0.5f - 34f, h * 0.5f - 29f);
            // Jost has no play glyph, so the triangle is drawn. Nudged right so it looks centred in the circle.
            var triangle = Theme.Pixels(play.Rect, "Triangle", PlayTriangle, material);
            triangle.color = Theme.Ice;
            triangle.rectTransform.sizeDelta = new Vector2(11f, 13f);
            triangle.rectTransform.anchoredPosition = new Vector2(1.5f, 0f);
            var body = Label(glass.Rect, "Description", regular, 16, Theme.Muted, TextAnchor.UpperLeft, 16f, 50f, w - 32f, 50f, centred: false, within: new Vector2(w, h));
            body.text = description;
            voices.Add(new VoiceCard { Style = style, Glass = glass, Play = play });
            AddTarget(play.Rect, WelcomeStep.Interests, () =>
            {
                flow.ChooseVoice(style);
                PreviewRequested?.Invoke(style);
                Debug.Log($"[EDITH welcome] voice preview asked for {style}");
            });
            // Added after the play button, so the button wins where the two meet.
            targets.Insert(targets.Count - 1, new Target { Rect = glass.Rect, Step = WelcomeStep.Interests, Tap = () => flow.ChooseVoice(style) });
        }

        void BuildPairing()
        {
            var card = Panel(stepTwo, "Pairing", 500f, 93f, 720f, 455f, 44f);
            card.SetLook(Theme.PanelFill, new Color(1f, 1f, 1f, 0.14f), Theme.Cyan, new Color(Theme.Cyan.r, Theme.Cyan.g, Theme.Cyan.b, 0.3f));
            var inner = Panel(stepTwo, "Code", 525f, 115f, 670f, 350f, 28f);
            inner.SetLook(new Color(0.03f, 0.15f, 0.15f, 0.3f), new Color(1f, 1f, 1f, 0.06f), new Color(Theme.Cyan.r, Theme.Cyan.g, Theme.Cyan.b, 0.5f), Color.clear);
            Label(stepTwo, "Title", semibold, 27, Theme.Ice, TextAnchor.MiddleLeft, 551f, 138f, 400f, 38f).text = "Pair your phone";
            counterTwo = Label(stepTwo, "Counter", semibold, 14, Theme.Cyan, TextAnchor.MiddleRight, 1000f, 146f, 175f, 24f);
            Label(stepTwo, "Step A", regular, 16, Theme.Muted, TextAnchor.MiddleLeft, 551f, 200f, 600f, 24f).text = "1 · On the phone running your hotspot, open";
            link = Label(stepTwo, "Link", semibold, 21, Theme.Cyan, TextAnchor.MiddleLeft, 575f, 234f, 600f, 30f);
            Label(stepTwo, "Step B", regular, 16, Theme.Muted, TextAnchor.MiddleLeft, 551f, 280f, 600f, 24f).text = "2 · Type this code and tap Share location";
            for (var i = 0; i < 4; i++)
            {
                boxes[i] = Panel(stepTwo, "Digit", 642f + i * 115f, 316f, 98f, 122f, 22f);
                digits[i] = Label(boxes[i].Rect, "Digit", semibold, 60, Theme.Ice, TextAnchor.MiddleCenter, 0f, 0f, 98f, 122f, centred: true);
            }
            spinner = Panel(stepTwo, "Spinner", 524f, 498f, 22f, 22f, 11f);
            status = Label(stepTwo, "Status", regular, 17, Theme.Ice, TextAnchor.MiddleLeft, 556f, 496f, 480f, 26f);
            var skip = Panel(stepTwo, "Skip", 1062f, 483f, 134f, 50f, 25f);
            skip.SetLook(new Color(0.04f, 0.2f, 0.22f, 0.3f), Color.clear, Theme.Cyan, Color.clear);
            Label(skip.Rect, "Label", semibold, 17, Theme.Ice, TextAnchor.MiddleCenter, 0f, 0f, 134f, 50f, centred: true).text = "Skip for now";
            AddTarget(skip.Rect, WelcomeStep.Pair, SkipTapped);
        }

        // ---- helpers

        static RectTransform Child(string name, RectTransform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(ScreenWidth, ScreenHeight);
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        /// Puts `rect` at design position (x, y) from the screen's top left, `w` by `h`, whatever its parent is. A
        /// `centred` rect is placed by its centre.
        static void Place(RectTransform rect, float x, float y, float w, float h, bool centred = false)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(w, h);
            rect.anchoredPosition = centred
                ? new Vector2(x - ScreenWidth * 0.5f, ScreenHeight * 0.5f - y)
                : new Vector2(x + w * 0.5f - ScreenWidth * 0.5f, ScreenHeight * 0.5f - (y + h * 0.5f));
        }

        Glass Panel(Transform parent, string name, float x, float y, float w, float h, float radius)
        {
            var glass = Glass.Build(parent, name, new Vector2(w, h), radius, material);
            Place(glass.Rect, x, y, w, h);
            return glass;
        }

        /// A label. Inside the screen its position is in design units from the screen's top left. Inside a glass
        /// shape (`centred`) it fills the shape from the centre, or when `within` gives the shape's size it sits at
        /// (x, y) from that shape's top left.
        Text Label(Transform parent, string name, Font face, int size, Color color, TextAnchor anchor,
            float x, float y, float w, float h, bool centred = false, Vector2? within = null)
        {
            var text = Theme.Label(parent, name, face, size, color, anchor, material);
            var rect = text.rectTransform;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(w, h);
            if (within is Vector2 box) rect.anchoredPosition = new Vector2(x + w * 0.5f - box.x * 0.5f, box.y * 0.5f - (y + h * 0.5f));
            else if (centred) rect.anchoredPosition = Vector2.zero;
            else Place(rect, x, y, w, h);
            return text;
        }

        void AddTarget(RectTransform rect, WelcomeStep step, Action tap) =>
            targets.Add(new Target { Rect = rect, Step = step, Tap = tap });

        static Texture2D playTriangle;

        /// A white play triangle pointing right, its edges smoothed, 22 by 26 pixels.
        static Texture2D PlayTriangle
        {
            get
            {
                if (playTriangle != null) return playTriangle;
                const int w = 22, h = 26;
                playTriangle = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var pixels = new Color32[w * h];
                for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    // Inside the left edge and under both slanted edges, each softened over a pixel.
                    float px = x + 0.5f, py = y + 0.5f - h * 0.5f;
                    var slant = (w - px) * (h * 0.5f) / w - Mathf.Abs(py);
                    var edge = Mathf.Min(px, slant * w / Mathf.Sqrt(w * w + h * h * 0.25f));
                    pixels[y * w + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(edge + 0.5f));
                }
                playTriangle.SetPixels32(pixels);
                playTriangle.Apply(false, true);
                return playTriangle;
            }
        }

        Quaternion Facing(Vector3 position) => Quaternion.LookRotation(position - eye.position, Vector3.up);
    }
}
