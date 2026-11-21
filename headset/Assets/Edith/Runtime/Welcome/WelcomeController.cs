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
    /// The first-launch welcome: EDITH on her horse at the left, and at the right three steps in glass. One picks
    /// interests and a voice, two shows the pairing code, three asks for a practice box. The steps and what has been
    /// picked live in WelcomeFlow; the pairing itself keeps running in PhonePairing, which stays hidden while this is
    /// up. The first two steps are tapped with a quick pinch, read straight from the hands because every gesture
    /// but this one stays off until a phone is paired. The practice step watches the real pinch input.
    public sealed class WelcomeController : MonoBehaviour
    {
        [SerializeField] Transform eye;
        [SerializeField] Font font;
        [SerializeField] Material material;
        [SerializeField] PhonePairing pairing;
        [SerializeField] PinchInput pinch;
        [SerializeField] RightHandSource right;
        [SerializeField] LeftHandSource left;

        /// The person tapped a voice's play button. Playing a sample is up to whoever listens.
        public event Action<VoiceStyle> PreviewRequested;

        /// The welcome ran to its end or was left at the pairing step.
        public event Action Finished;

        // The screen is laid out in 1280 by 820 design units. 2800 units to the metre makes it 46 cm wide, held 0.55 m
        // out, which fills about 45 degrees of view.
        const float UnitsPerMetre = 2800f;
        const float Ahead = 0.55f, FollowRate = 6f, FadeSeconds = 0.35f;
        const float ScreenWidth = 1280f, ScreenHeight = 820f;
        const float TapMargin = 10f;
        const float TypeCharsPerSecond = 55f;
        const string PracticeLine = "Pinch the air and drag a box around something that catches your eye.";

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
        RectTransform screen, stepOne, stepTwo, stepThree, ghost;
        Text speechText, link, status, toast, counterOne, counterTwo, counterThree;
        Glass dot, spinner;
        CanvasGroup ghostGroup;
        Font regular, semibold;
        WelcomeStep shownStep = WelcomeStep.Done;
        bool phoneKnown, lookingUpPlace, dragging, leaving;
        float ghostAlpha = 1f;

        void Start()
        {
            if (WelcomeStore.Done)
            {
                enabled = false;
                return;
            }
            Build();
            if (pairing != null) pairing.Suppressed = true;
            if (pinch != null) pinch.Stepped += OnPinch;
            Refresh();
        }

        void OnDestroy()
        {
            if (pinch != null) pinch.Stepped -= OnPinch;
        }

        void Update()
        {
            if (screen == null) return;
            WatchPhone();
            if (flow.Step == WelcomeStep.Interests || flow.Step == WelcomeStep.Pair) ReadTaps();
            if (flow.Step != shownStep) Enter(flow.Step);
            Draw();
        }

        void LateUpdate()
        {
            if (screen == null || group.alpha <= 0f) return;
            var follow = 1f - Mathf.Exp(-FollowRate * Time.deltaTime);
            var rest = eye.position + eye.forward * Ahead;
            var position = Vector3.Lerp(transform.position, rest, follow);
            transform.SetPositionAndRotation(position, Quaternion.Slerp(transform.rotation, Facing(position), follow));
        }

        // ---- flow

        /// Moves to the practice step once the backend keeps a phone position, however the person got there.
        void WatchPhone()
        {
            var state = pairing != null ? pairing.Flow : null;
            if (state == null || phoneKnown) return;
            if (state.Phase != PairPhase.Connected && state.Phase != PairPhase.Paired) return;
            phoneKnown = true;
            flow.PhoneConnected(null);
            LookUpPlace();
        }

        /// The backend resolves the place a moment after the first position arrives, so ask a few times.
        async void LookUpPlace()
        {
            if (lookingUpPlace) return;
            lookingUpPlace = true;
            for (var attempt = 0; attempt < 6 && this != null; attempt++)
            {
                var place = await pairing.PlaceAsync();
                if (this == null) return;
                if (!string.IsNullOrEmpty(place))
                {
                    flow.PhoneConnected(place);
                    return;
                }
                await System.Threading.Tasks.Task.Delay(2000);
            }
        }

        void ReadTaps()
        {
            if (right == null || left == null) return;
            var reading = taps.Step(right.ReadUngated(), left.ReadUngated());
            if (reading.Event == PinchEvent.Tap) Tap(reading.Start);
        }

        /// The practice step finishes with the first box drawn.
        void OnPinch(PinchReading reading)
        {
            if (flow.Step != WelcomeStep.Practice) return;
            dragging = reading.Phase == PinchPhase.Dragging || reading.Phase == PinchPhase.Released;
            if (reading.Event == PinchEvent.Completed) flow.PracticeDone();
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
            stepThree.gameObject.SetActive(step == WelcomeStep.Practice);
            dragging = false;
            Refresh();
            if (step == WelcomeStep.Done) Leave();
        }

        void Leave()
        {
            if (leaving) return;
            leaving = true;
            // Practice done means the welcome is complete. Leaving at the pairing step also ends it, and brings the
            // pairing panel back, since the gate stays closed until a phone is paired.
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

            switch (flow.Step)
            {
                case WelcomeStep.Pair:
                    DrawPairing();
                    break;
                case WelcomeStep.Practice:
                    DrawPractice();
                    break;
            }
            counterOne.text = counterTwo.text = counterThree.text = flow.Counter;
        }

        string SpeechLine()
        {
            switch (flow.Step)
            {
                case WelcomeStep.Interests:
                    return "Hi, I'm EDITH! Tell me what you love and I'll find the fun in whatever you look at.";
                case WelcomeStep.Pair:
                    return "One more thing: grab your phone so I know where we are. Then I can tell you about the place too.";
                case WelcomeStep.Practice:
                    var where = string.IsNullOrEmpty(flow.Place) ? "I can tell where we are" : "we're at " + flow.Place;
                    return "Got it, " + where + "! Now let's try one. " + PracticeLine;
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

        void DrawPractice()
        {
            toast.text = string.IsNullOrEmpty(flow.Place) ? "Phone paired" : "Phone paired · " + flow.Place;
            // The ghost box shows where to drag and fades once the pinch starts.
            ghostAlpha = Mathf.MoveTowards(ghostAlpha, dragging ? 0f : 1f, Time.deltaTime / 0.25f);
            ghostGroup.alpha = ghostAlpha;
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
            BuildCharacter();
            stepOne = Child("Step 1", screen);
            stepTwo = Child("Step 2", screen);
            stepThree = Child("Step 3", screen);
            BuildInterests();
            BuildPairing();
            BuildPractice();
            stepOne.gameObject.SetActive(false);
            stepTwo.gameObject.SetActive(false);
            stepThree.gameObject.SetActive(false);

            var rest = eye.position + eye.forward * Ahead;
            transform.SetPositionAndRotation(rest, Facing(rest));
        }

        void BuildCharacter()
        {
            var name = Label(screen, "Name", semibold, 15, Theme.Cyan, TextAnchor.MiddleLeft, 70f, 122f, 200f, 20f);
            name.text = "EDITH";
            speechText = Label(screen, "Speech", regular, 29, Theme.Ice, TextAnchor.UpperLeft, 70f, 148f, 390f, 130f);
            speechText.supportRichText = true;
            var rider = Theme.Pixels(screen, "Rider", Theme.Rider, material);
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
            var triangle = Label(play.Rect, "Triangle", semibold, 15, Theme.Ice, TextAnchor.MiddleCenter, 0f, 0f, 34f, 34f, centred: true);
            triangle.text = "▶";
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

        void BuildPractice()
        {
            var pill = Panel(stepThree, "Paired", 520f, 62f, 455f, 50f, 25f);
            pill.SetLook(Theme.PanelFill, new Color(1f, 1f, 1f, 0.14f), Theme.Cyan, new Color(Theme.Cyan.r, Theme.Cyan.g, Theme.Cyan.b, 0.3f));
            var check = Panel(stepThree, "Check", 526f, 66f, 42f, 42f, 21f);
            check.SetLook(Theme.Cyan, Color.clear, Theme.Cyan, Color.clear);
            Label(check.Rect, "Tick", semibold, 21, Theme.OnCyan, TextAnchor.MiddleCenter, 0f, 0f, 42f, 42f, centred: true).text = "✓";
            toast = Label(stepThree, "Toast", semibold, 19, Theme.Ice, TextAnchor.MiddleLeft, 578f, 70f, 320f, 34f);
            counterThree = Label(stepThree, "Counter", semibold, 14, Theme.Cyan, TextAnchor.MiddleRight, 880f, 74f, 80f, 24f);

            ghost = Child("Ghost", stepThree);
            ghostGroup = ghost.gameObject.AddComponent<CanvasGroup>();
            ghostGroup.interactable = false;
            ghostGroup.blocksRaycasts = false;
            const float gx = 880f, gy = 262f, gw = 322f, gh = 288f;
            var outline = Theme.Pixels(ghost, "Outline", DashedBox((int)gw, (int)gh, 38, 3, 12, 8), material);
            Place(outline.rectTransform, gx, gy, gw, gh);
            outline.color = new Color(Theme.Cyan.r, Theme.Cyan.g, Theme.Cyan.b, 0.85f);
            var length = Mathf.Sqrt(gw * gw + gh * gh) - 40f;
            var dots = Theme.Pixels(ghost, "Diagonal", DotRow(), material);
            Place(dots.rectTransform, gx + gw * 0.5f, gy + gh * 0.5f, length, 8f, centred: true);
            dots.uvRect = new Rect(0f, 0f, length / 16f, 1f);
            dots.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Atan2(gh, gw) * Mathf.Rad2Deg);
            dots.color = Theme.Ice;
            dot = Panel(ghost, "Start", gx - 11f, gy - 11f, 22f, 22f, 11f);
            dot.SetLook(new Color(Theme.Cyan.r, Theme.Cyan.g, Theme.Cyan.b, 0.9f), Color.clear, Theme.Ice, new Color(Theme.Cyan.r, Theme.Cyan.g, Theme.Cyan.b, 0.5f));
            Label(ghost, "Hint", regular, 16, Theme.Ice, TextAnchor.MiddleCenter, gx - 40f, gy + gh + 26f, gw + 80f, 26f).text = "Ghost box shows where to drag. It fades once you start.";

            var back = Label(stepThree, "Back", regular, 18, Theme.Ice, TextAnchor.MiddleCenter, 500f, 749f, 280f, 28f);
            back.text = "Back to step 1";
            var underline = new GameObject("Underline", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            underline.transform.SetParent(stepThree, false);
            underline.color = Theme.Ice;
            underline.raycastTarget = false;
            underline.material = material;
            Place(underline.rectTransform, 560f, 772f, 160f, 1.5f);
            AddTarget(back.rectTransform, WelcomeStep.Practice, flow.Back);
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

        Quaternion Facing(Vector3 position) => Quaternion.LookRotation(position - eye.position, Vector3.up);

        /// A rounded rectangle drawn as a dashed line `thickness` pixels wide. Corners are solid.
        static Texture2D DashedBox(int w, int h, int radius, int thickness, int dash, int gap)
        {
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[w * h];
            float hx = w * 0.5f, hy = h * 0.5f;
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                float px = x + 0.5f - hx, py = y + 0.5f - hy;
                float qx = Mathf.Abs(px) - (hx - radius), qy = Mathf.Abs(py) - (hy - radius);
                var d = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f))
                    + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                var band = Mathf.Clamp01(thickness * 0.5f + 0.5f - Mathf.Abs(d + thickness * 0.5f));
                var corner = qx > 0f && qy > 0f;
                var along = qx > qy ? y : x;
                var on = corner || along % (dash + gap) < dash;
                pixels[y * w + x] = new Color(1f, 1f, 1f, on ? band : 0f);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        /// One cell of the dotted diagonal: a dot in a 16 by 8 pixel tile, repeated along the line.
        static Texture2D DotRow()
        {
            var texture = new Texture2D(16, 8, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
            var pixels = new Color32[16 * 8];
            for (var y = 0; y < 8; y++)
            for (var x = 0; x < 16; x++)
            {
                var d = Mathf.Sqrt((x - 7.5f) * (x - 7.5f) + (y - 3.5f) * (y - 3.5f));
                pixels[y * 16 + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(2.6f - d));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
