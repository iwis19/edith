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
        [Tooltip("Show the welcome on every launch, even once it has been finished. On while the welcome is being tried out.")]
        [SerializeField] bool everyLaunch = true;

        // EDITH's welcome line recorded in each voice, named after the VoiceStyle, from voice_samples.py.
        const string VoiceFolder = "EdithUI/Voices/";

        /// The phone paired or the person skipped pairing.
        public event Action Finished;

        // The screen is laid out in 1280 by 820 design units. 2500 units to the metre makes it 51 cm wide, held 0.55 m
        // out, which fills about 50 degrees of view.
        const float UnitsPerMetre = 2500f;
        const float Ahead = 0.55f, FollowRate = 20f, FadeSeconds = 0.35f;
        const float ScreenWidth = 1280f, ScreenHeight = 820f;
        // The pairing pane, in design units, and where it is held: 0.45 m out and 6 cm below the line of sight, as
        // PhonePairing holds its panel, and a tenth wider than that panel's 15 cm.
        static readonly Rect PairPane = new Rect(500f, 93f, 720f, 455f);
        const float PairWidthMetres = 0.165f, PairAhead = 0.45f, PairBelow = 0.06f;
        const float TapMargin = 10f;
        const float TypeCharsPerSecond = 55f;
        const float ResumeOverlapSeconds = 0.15f;

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

        readonly PinchPair taps = new PinchPair(new PinchTuning());
        readonly List<Target> targets = new List<Target>();
        readonly List<Chip> chips = new List<Chip>();
        readonly List<VoiceCard> voices = new List<VoiceCard>();
        readonly TypedText speech = new TypedText(TypeCharsPerSecond);
        readonly Text[] digits = new Text[4];

        Canvas canvas;
        CanvasGroup group;
        RectTransform screen, character, stepOne, stepTwo;
        Text speechText, link;
        Font regular, semibold;
        WelcomeStep shownStep = WelcomeStep.Done;
        readonly PeaceSignsTracker summon = new PeaceSignsTracker();
        WelcomeFlow flow = new WelcomeFlow();
        bool open, phoneKnown, leaving, greeted;
        AudioSource speaker;
        // What EDITH was last told to say and when, so a line cut off by an audio reset can start again.
        VoiceStyle? saying;
        float sayingFrom, sayingUntil;
        bool resaying;

        void Start()
        {
            // Ask resets the audio output shortly after launch and after every pause, which stops whatever is playing.
            AudioSettings.OnAudioConfigurationChanged += OnAudioReset;
            Build();
            if (!WelcomeStore.Done || everyLaunch) Open();
        }

        void Update()
        {
            if (right == null || left == null) return;
            // Ungated, so the gesture and the taps work before a phone is paired.
            HandFrame rightHand = right.ReadUngated(), leftHand = left.ReadUngated();
            if (summon.Step(leftHand, rightHand) && (!open || leaving)) Open();
            if (!open) return;
            KeepSaying();
            WatchPhone();
            if (!leaving) ReadTaps(rightHand, leftHand);
            if (flow.Step != shownStep) Enter(flow.Step);
            Draw();
        }

        /// Starts the welcome over from the first step, with nothing picked. Two peace signs held for 1.5 seconds call
        /// it at any time, so it can be shown again wherever the person is.
        void Open()
        {
            flow = new WelcomeFlow();
            open = true;
            leaving = phoneKnown = greeted = false;
            speech.Set("");
            shownStep = WelcomeStep.Done;
            if (pairing != null) pairing.Suppressed = true;
            Refresh();
            Debug.Log("[EDITH welcome] opened");
        }

        void LateUpdate()
        {
            if (!open || group.alpha <= 0f) return;
            // Close behind the head, with only enough smoothing to hide head tremor: 20 a second covers 63% of the gap
            // in 50 ms.
            var follow = 1f - Mathf.Exp(-FollowRate * Time.deltaTime);
            var position = Vector3.Lerp(transform.position, Rest(), follow);
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

        void ReadTaps(in HandFrame rightHand, in HandFrame leftHand)
        {
            var reading = taps.Step(rightHand, leftHand);
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

        // ---- drawing

        void Enter(WelcomeStep step)
        {
            shownStep = step;
            stepOne.gameObject.SetActive(step == WelcomeStep.Interests);
            stepTwo.gameObject.SetActive(step == WelcomeStep.Pair);
            // Done fades out in the layout it was shown in.
            if (step != WelcomeStep.Done) Arrange(step == WelcomeStep.Pair);
            if (step == WelcomeStep.Interests && !greeted)
            {
                greeted = true;
                Say(flow.Voice);
            }
            Refresh();
            if (step == WelcomeStep.Done) Leave();
        }

        void OnDestroy() => AudioSettings.OnAudioConfigurationChanged -= OnAudioReset;

        /// Starts the line again on the next frame when an audio reset cut it off, once the reset is over.
        void OnAudioReset(bool deviceWasChanged)
        {
            if (saying != null && !leaving && Time.realtimeSinceStartup < sayingUntil) resaying = true;
        }

        /// Keeps the current line playing to its end: says it again after an audio reset, or when it stopped early.
        void KeepSaying()
        {
            if (saying is not VoiceStyle style || leaving || speaker == null) return;
            var now = Time.realtimeSinceStartup;
            if (now >= sayingUntil)
            {
                saying = null;
                return;
            }
            if (!resaying && (speaker.isPlaying || now >= sayingUntil - 0.3f)) return;
            // Picks up where the line was cut, a moment early so no word is lost, instead of greeting twice.
            var at = Mathf.Max(0f, now - sayingFrom - ResumeOverlapSeconds);
            Debug.Log($"[EDITH welcome] the {style} line {(resaying ? "was cut by an audio reset" : "stopped early")}, going on from {at:F1} s");
            resaying = false;
            Say(style, at);
        }

        /// Says EDITH's welcome line in `style` from `at` seconds in, cutting off whatever she was saying. Silent when
        /// the clip is missing.
        void Say(VoiceStyle style, float at = 0f)
        {
            var clip = Resources.Load<AudioClip>(VoiceFolder + style);
            if (clip == null)
            {
                Debug.LogWarning($"[EDITH welcome] no voice sample for {style}");
                return;
            }
            if (speaker == null)
            {
                speaker = gameObject.AddComponent<AudioSource>();
                speaker.spatialBlend = 0f;
                speaker.playOnAwake = false;
            }
            speaker.Stop();
            speaker.clip = clip;
            speaker.time = Mathf.Min(at, clip.length - 0.01f);
            speaker.Play();
            saying = style;
            sayingFrom = Time.realtimeSinceStartup - speaker.time;
            sayingUntil = sayingFrom + clip.length;
            Debug.Log($"[EDITH welcome] saying the {style} line, {clip.length:F1} s, {clip.frequency} Hz, {clip.loadType} {clip.loadState}, " +
                $"output {AudioSettings.outputSampleRate} Hz, volume {speaker.volume:F2}, listener {AudioListener.volume:F2}{(AudioListener.pause ? " paused" : "")}");
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
            if (speaker != null) speaker.Stop();
            saying = null;
            // A paired phone completes the welcome.
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
                open = false;
                return;
            }

            speech.Set(SpeechLine());
            speech.Advance(Time.deltaTime);
            speechText.text = speech.Render();

            if (flow.Step == WelcomeStep.Pair) DrawPairing();
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
            for (var i = 0; i < 4; i++) digits[i].text = code != null && i < code.Length ? code[i].ToString() : "";
            link.text = state == null ? "This build has no EDITH server."
                : state.PhoneUrl != null ? PairingText.TypedAddress(state.PhoneUrl) : "...";
        }

        /// Redraws the picks: which chips and voice are chosen.
        void Refresh()
        {
            if (screen == null) return;
            foreach (var chip in chips)
            {
                var on = flow.IsPicked(chip.Name);
                chip.Glass.SetLook(on ? Theme.Accent : Theme.ChipFill, on ? Color.clear : new Color(1f, 1f, 1f, 0.1f),
                    on ? Theme.Accent : new Color(1f, 1f, 1f, 0.45f), on ? new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.4f) : Color.clear);
                chip.Label.color = on ? Theme.OnAccent : Theme.Ice;
            }
            foreach (var voice in voices)
            {
                var on = flow.Voice == voice.Style;
                voice.Glass.SetLook(on ? Theme.Tint(0.55f) : Theme.ChipFill, new Color(1f, 1f, 1f, on ? 0.18f : 0.08f),
                    on ? Theme.Accent : new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.4f),
                    on ? new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.4f) : Color.clear);
            }
        }

        // ---- building

        void Build()
        {
            regular = Theme.Regular(font);
            semibold = Theme.SemiBold(font);

            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            Theme.Sharpen(gameObject);
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
            var name = Label(character, "Name", semibold, 15, Theme.Accent, TextAnchor.MiddleLeft, 70f, 122f, 200f, 20f);
            name.text = "EDITH";
            speechText = Label(character, "Speech", regular, 29, Theme.Ice, TextAnchor.UpperLeft, 70f, 148f, 390f, 130f);
            speechText.supportRichText = true;
            var rider = Theme.Pixels(character, "Rider", Theme.Rider, material);
            Place(rider.rectTransform, 50f, 300f, 400f, 325f);
        }

        void BuildInterests()
        {
            const float left = 500f, top = 60f, width = 720f, innerLeft = 525f, innerWidth = 670f;
            const float chipHeight = 43f, rowStep = 54f, chipsLeft = 546f, chipsRight = 1176f;
            var card = Panel(stepOne, "Interests", left, top, width, 505f, 44f);
            card.SetLook(Theme.PanelFill, new Color(1f, 1f, 1f, 0.14f), Theme.Accent, new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.3f));
            var first = Panel(stepOne, "Pick", innerLeft, 83f, innerWidth, 187f, 28f);
            first.SetLook(Theme.InnerFill, new Color(1f, 1f, 1f, 0.06f), new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.5f), Color.clear);
            Label(stepOne, "Title", semibold, 29, Theme.Ice, TextAnchor.MiddleLeft, 546f, 104f, 400f, 40f).text = "What are you into?";

            var measure = Label(stepOne, "Measure", regular, 21, Theme.Ice, TextAnchor.MiddleCenter, 0f, 0f, 10f, 10f);
            measure.horizontalOverflow = HorizontalWrapMode.Overflow;
            float x = chipsLeft, y = 160f;
            foreach (var name in WelcomeFlow.AllInterests)
            {
                measure.text = name;
                var chipWidth = Mathf.Ceil(measure.preferredWidth) + 38f;
                if (x + chipWidth > chipsRight)
                {
                    x = chipsLeft;
                    y += rowStep;
                }
                var chip = new Chip { Name = name, Glass = Panel(stepOne, "Chip", x, y, chipWidth, chipHeight, chipHeight * 0.5f) };
                chip.Label = Label(chip.Glass.Rect, "Label", regular, 21, Theme.Ice, TextAnchor.MiddleCenter, 0f, 0f, chipWidth, chipHeight, centred: true);
                chip.Label.text = name;
                chip.Label.horizontalOverflow = HorizontalWrapMode.Overflow;
                chips.Add(chip);
                var picked = name;
                AddTarget(chip.Glass.Rect, WelcomeStep.Interests, () => flow.Toggle(picked));
                x += chipWidth + 12f;
            }
            Destroy(measure.gameObject);
            var pickBottom = y + chipHeight + 24f;
            Reframe(first, innerLeft, 83f, innerWidth, pickBottom - 83f);

            var voiceTop = pickBottom + 17f;
            var voiceCards = voiceTop + 64f;
            var voiceBottom = voiceCards + VoiceHeight + 20f;
            var second = Panel(stepOne, "Voice", innerLeft, voiceTop, innerWidth, voiceBottom - voiceTop, 28f);
            second.SetLook(Theme.InnerFill, new Color(1f, 1f, 1f, 0.06f), new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.5f), Color.clear);
            Label(stepOne, "VoiceTitle", semibold, 27, Theme.Ice, TextAnchor.MiddleLeft, 546f, voiceTop + 19f, 400f, 38f).text = "How should I sound?";
            AddVoice(VoiceStyle.Enthusiastic, "Enthusiastic", 545f, voiceCards);
            AddVoice(VoiceStyle.Calm, "Calm", 759f, voiceCards);
            AddVoice(VoiceStyle.Light, "Light", 973f, voiceCards);

            var nextTop = voiceBottom + 17f;
            Label(stepOne, "Hint", regular, 16, Theme.Ice, TextAnchor.MiddleLeft, 530f, nextTop + 14f, 420f, 24f).text = "Pinch to pick, or say them out loud";
            var next = Panel(stepOne, "Next", 1100f, nextTop, 96f, 52f, 26f);
            next.SetLook(Theme.Accent, Color.clear, Theme.Accent, new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.45f));
            var nextLabel = Label(next.Rect, "Label", semibold, 21, Theme.OnAccent, TextAnchor.MiddleCenter, 0f, 0f, 96f, 52f, centred: true);
            nextLabel.text = "Next";
            AddTarget(next.Rect, WelcomeStep.Interests, NextTapped);
            Reframe(card, left, top, width, nextTop + 52f + 19f - top);
        }

        const float VoiceWidth = 202f, VoiceHeight = 64f;

        /// A voice card at design position (x, y): its name and a play button.
        void AddVoice(VoiceStyle style, string title, float x, float y)
        {
            const float w = VoiceWidth, h = VoiceHeight;
            var glass = Panel(stepOne, "Voice " + title, x, y, w, h, 22f);
            Label(glass.Rect, "Title", semibold, 21, Theme.Ice, TextAnchor.MiddleLeft, 16f, 18f, 130f, 28f, centred: false, within: new Vector2(w, h)).text = title;
            var play = Glass.Build(glass.Rect, "Play", new Vector2(34f, 34f), 17f, material);
            play.SetLook(Theme.Tint(0.4f), Color.clear, Theme.Ice, Color.clear);
            play.Rect.anchoredPosition = new Vector2(w * 0.5f - 32f, 0f);
            // Jost has no play glyph, so the triangle is drawn. Nudged right so it looks centred in the circle.
            var triangle = Theme.Pixels(play.Rect, "Triangle", PlayTriangle, material);
            triangle.color = Theme.Ice;
            triangle.rectTransform.sizeDelta = new Vector2(11f, 13f);
            triangle.rectTransform.anchoredPosition = new Vector2(1.5f, 0f);
            voices.Add(new VoiceCard { Style = style, Glass = glass, Play = play });
            // The whole card, play button included, picks the voice and plays its sample.
            AddTarget(glass.Rect, WelcomeStep.Interests, () =>
            {
                flow.ChooseVoice(style);
                Say(style);
            });
        }

        /// Only what pairing needs, large enough to read in the headset: the title, the address to open and the code.
        void BuildPairing()
        {
            var card = Panel(stepTwo, "Pairing", PairPane.x, PairPane.y, PairPane.width, PairPane.height, 44f);
            card.SetLook(Theme.PanelFill, new Color(1f, 1f, 1f, 0.14f), Theme.Accent, new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.3f));
            var left = PairPane.x + 30f;
            var width = PairPane.width - 60f;
            Label(stepTwo, "Title", semibold, 46, Theme.Ice, TextAnchor.MiddleCenter, left, PairPane.y + 40f, width, 64f).text = "Turn on location!";
            link = Label(stepTwo, "Link", semibold, 36, Theme.Accent, TextAnchor.MiddleCenter, left, PairPane.y + 135f, width, 52f);
            link.horizontalOverflow = HorizontalWrapMode.Overflow;
            const float digitWidth = 120f;
            var digitsLeft = PairPane.center.x - digitWidth * 2f;
            for (var i = 0; i < 4; i++)
                digits[i] = Label(stepTwo, "Digit", semibold, 120, Theme.Ice, TextAnchor.MiddleCenter, digitsLeft + i * digitWidth, PairPane.y + 230f, digitWidth, 170f, shadow: false);
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

        /// Moves and resizes a panel built before its size was known.
        static void Reframe(Glass glass, float x, float y, float w, float h)
        {
            glass.Resize(new Vector2(w, h));
            Place(glass.Rect, x, y, w, h);
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
            float x, float y, float w, float h, bool centred = false, Vector2? within = null, bool shadow = true)
        {
            var text = Theme.Label(parent, name, face, size, color, anchor, material, shadow);
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

        // Upright to the head, as the Ask orb is, so the panel rolls with it instead of looking tilted.
        Quaternion Facing(Vector3 position) => Quaternion.LookRotation(position - eye.position, eye.up);
    }
}
