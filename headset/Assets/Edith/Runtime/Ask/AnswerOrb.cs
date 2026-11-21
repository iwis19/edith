using System;
using System.Collections.Generic;
using Edith.Snapshots;
using Edith.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Edith.Ask
{
    /// EDITH's presence while a question is under way: a small glowing orb with a caption of at most two lines to its
    /// right. It lines up along the top edge of the snapshot asked about when that covers no other snapshot, and
    /// otherwise rests low and to the left of the line of sight and drifts after the head, moving to the nearest clear
    /// spot while a snapshot is in the way. It appears where it rests, pulses with the answer's loudness, and types the
    /// answer out as it is spoken.
    public sealed class AnswerOrb : MonoBehaviour
    {
        // Four canvas units per millimetre, so the font is rasterised sharply at this size.
        const float UnitsPerMetre = 4000f;
        public const float OrbMetres = 0.036f;
        public const float GapMetres = 0.012f;
        const int FontSize = 56;
        const float CaptionHeight = 160f;
        public const int MaxLines = 2;
        public const float HomeCaptionMetres = 0.26f, MinCaptionMetres = 0.1f;
        static readonly Color Quiet = Theme.Muted;
        static readonly Color Bright = Theme.Ice;
        const int ListeningFontSize = 34;
        const float HeadShare = 0.64f;
        const float PulseRate = 3f;

        // Rates of the exponential approach, per second: the position trails the head lazily, and the turn onto a
        // photo's plane or back to facing the eye is quicker.
        const float FollowRate = 4f, TurnRate = 12f;
        const float BobMetres = 0.005f, BobRate = 1.5f;
        /// How long home must stay clear before an orb moved off it goes back, so it does not flit at an edge.
        const float HomeReturnSeconds = 0.75f;
        const float SpeakPulse = 0.18f, LevelAttack = 18f, LevelRelease = 6f;
        const float FadeSeconds = 0.3f, FlickMetres = 0.45f;

        readonly TextGenerator lines = new TextGenerator();
        readonly List<Rect> covered = new List<Rect>();
        Transform eye;
        SnapshotRoom room;
        Snapshot asked;
        bool atPerch;
        Vector2 spot = OrbPlacement.Home;
        float homeClearSince = -1f;
        Text caption, listeningLabel;
        RectTransform disc, halo;
        Glass haloGlass;
        bool listening;
        CanvasGroup group;
        float captionMetres;
        string shown = "";
        string answer = "";
        int revealed;
        float level;
        bool leaving;
        float leaveProgress;
        Vector3 leaveFrom, leaveDirection;

        /// How loud the answer is right now, from 0 to 1.
        public float Level { get; set; }

        /// What the caption shows, already cut to its last MaxLines lines.
        public string Caption => caption.text;

        /// An orb for a question about `asked`, or about nothing when it is null, kept clear of every snapshot in
        /// `room`, which may be null.
        public static AnswerOrb Create(Transform eye, Font font, Material material, SnapshotRoom room = null, Snapshot asked = null)
        {
            var root = new GameObject("AnswerOrb", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = Vector2.one * (OrbMetres * UnitsPerMetre);
            rect.localScale = Vector3.one / UnitsPerMetre;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            Theme.Sharpen(root);
            // Drawn after the snapshots whatever the distances, since transparent UI sorts by distance otherwise.
            canvas.sortingOrder = 1;

            var orb = root.AddComponent<AnswerOrb>();
            orb.eye = eye;
            orb.room = room;
            orb.asked = asked;
            orb.group = root.GetComponent<CanvasGroup>();
            orb.group.interactable = false;
            orb.group.blocksRaycasts = false;

            var diameter = OrbMetres * UnitsPerMetre;
            var regular = Theme.Regular(font);
            // The ring behind the disc: it breathes while EDITH listens and swells with the voice while it speaks.
            orb.haloGlass = Glass.Build(rect, "Halo", Vector2.one * diameter, diameter * 0.5f, material);
            orb.haloGlass.SetLook(Color.clear, Color.clear, new Color(Theme.Cyan.r, Theme.Cyan.g, Theme.Cyan.b, 0.4f), Color.clear);
            orb.halo = orb.haloGlass.Rect;

            var body = Glass.Build(rect, "Disc", Vector2.one * diameter, diameter * 0.5f, material);
            body.SetLook(Theme.DiscFill, new Color(1f, 1f, 1f, 0.1f), Theme.Cyan,
                new Color(Theme.Cyan.r, Theme.Cyan.g, Theme.Cyan.b, 0.38f));
            orb.disc = body.Rect;
            var head = Theme.Pixels(orb.disc, "Head", Theme.Head, material);
            var headWidth = diameter * HeadShare;
            head.rectTransform.sizeDelta = new Vector2(headWidth, headWidth * 17f / 21f);

            var text = Theme.Label(rect, "Caption", regular, FontSize, Bright, TextAnchor.MiddleLeft, material);
            var captionRect = text.rectTransform;
            captionRect.anchorMin = captionRect.anchorMax = new Vector2(1f, 0.5f);
            captionRect.pivot = new Vector2(0f, 0.5f);
            captionRect.anchoredPosition = new Vector2(GapMetres * UnitsPerMetre, 0f);
            orb.caption = text;

            orb.listeningLabel = Theme.Label(rect, "Listening", regular, ListeningFontSize, Bright, TextAnchor.UpperCenter, material);
            orb.listeningLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            var labelRect = orb.listeningLabel.rectTransform;
            labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.sizeDelta = new Vector2(diameter * 2f, ListeningFontSize * 1.4f);
            labelRect.anchoredPosition = new Vector2(0f, -diameter * 0.08f);
            orb.listeningLabel.text = "Listening";
            orb.listeningLabel.enabled = false;

            orb.Choose(Time.time, true);
            orb.FitCaption(orb.CaptionMetres);
            var rest = orb.Rest();
            rect.SetPositionAndRotation(rest, orb.atPerch ? asked.transform.rotation : OrbPlacement.FacingEye(orb.EyePose, rest));
            orb.ShowListening();
            return orb;
        }

        /// Resting on the snapshot asked about rather than following the head.
        public bool Perched => atPerch;

        float CaptionMetres => atPerch ? PerchedCaptionMetres(asked.Box.Size.x) : HomeCaptionMetres;

        static float RowMetres(float captionMetres) => OrbMetres + GapMetres + captionMetres;

        Pose EyePose => new Pose(eye.position, eye.rotation);

        /// Picks where the row rests now: on the snapshot asked about when the row there covers no other snapshot,
        /// else home when it is clear, else the clear spot nearest where it is. `immediate` skips the wait before
        /// going back home.
        void Choose(float now, bool immediate)
        {
            var eyePose = EyePose;
            atPerch = false;
            if (asked != null && !asked.Leaving)
            {
                Gather(eyePose, asked);
                var photo = asked.transform;
                var perch = OrbPlacement.Perch(new Pose(photo.position, photo.rotation), asked.Box.Size);
                var row = OrbPlacement.WorldRow(eyePose, perch, photo.rotation, RowMetres(PerchedCaptionMetres(asked.Box.Size.x)));
                if (row is { } seen && OrbPlacement.Overlap(seen, covered) <= 0f)
                {
                    atPerch = true;
                    return;
                }
            }

            Gather(eyePose, null);
            var rowMetres = RowMetres(HomeCaptionMetres);
            var homeClear = OrbPlacement.Overlap(OrbPlacement.HeadRow(OrbPlacement.Home, rowMetres), covered) <= 0f;
            if (!homeClear) homeClearSince = -1f;
            else if (homeClearSince < 0f) homeClearSince = now;
            if (homeClear && (immediate || spot == OrbPlacement.Home || now - homeClearSince >= HomeReturnSeconds))
            {
                spot = OrbPlacement.Home;
                return;
            }
            if (OrbPlacement.Overlap(OrbPlacement.HeadRow(spot, rowMetres), covered) > 0f)
                spot = OrbPlacement.Nearest(spot, covered, rowMetres);
        }

        /// Fills `covered` with what each snapshot in the room but `except` takes of the view from `eyePose`.
        void Gather(Pose eyePose, Snapshot except)
        {
            covered.Clear();
            if (room == null) return;
            var snapshots = room.Snapshots;
            for (var i = 0; i < snapshots.Count; i++)
            {
                var snapshot = snapshots[i].item;
                if (snapshot == null || snapshot.Leaving || snapshot == except) continue;
                var t = snapshot.transform;
                if (OrbPlacement.Covered(eyePose, new Pose(t.position, t.rotation), snapshot.Box.Size) is { } patch) covered.Add(patch);
            }
        }

        Vector3 Rest()
        {
            if (!atPerch) return OrbPlacement.AtHead(EyePose, spot);
            var photo = asked.transform;
            return OrbPlacement.Perch(new Pose(photo.position, photo.rotation), asked.Box.Size);
        }

        /// True from the start of a question until the answer or a problem takes over: the orb breathes and says so
        /// under itself, and the caption stays empty.
        public bool Listening => listening;

        public void ShowListening()
        {
            SetListening(true);
            Show("", Quiet);
        }

        public void ShowProblem(string line)
        {
            SetListening(false);
            Show(line, Quiet);
        }

        void SetListening(bool value)
        {
            listening = value;
            listeningLabel.enabled = value;
        }

        /// Shows `text`, the answer so far, typed out to the `spoken` fraction of it. The typed part only grows until
        /// a different answer starts.
        public void ShowAnswer(string text, float spoken)
        {
            text ??= "";
            var typed = text.StartsWith(answer, StringComparison.Ordinal) ? revealed : 0;
            typed = Mathf.Clamp(Mathf.Max(typed, Mathf.CeilToInt(text.Length * Mathf.Clamp01(spoken))), 0, text.Length);
            if (text == answer && typed == revealed && caption.color == Bright) return;
            answer = text;
            revealed = typed;
            SetListening(false);
            Show(text.Substring(0, revealed), Bright);
        }

        /// The end of `value` that fills at most `count` lines of `text`, starting at the first line kept. Rows leave
        /// whole, so the caption scrolls a line at a time instead of a word at a time.
        public static string LastLines(Text text, TextGenerator generator, string value, int count)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var settings = text.GetGenerationSettings(text.rectTransform.rect.size);
            settings.verticalOverflow = VerticalWrapMode.Overflow;
            generator.Populate(value, settings);
            var total = generator.lineCount;
            if (total <= count) return value;
            return value.Substring(generator.lines[total - count].startCharIdx).TrimStart();
        }

        /// The caption's width on a photo `photoMetres` wide: the orb and caption together span the photo, but never
        /// narrower than MinCaptionMetres or wider than at home.
        public static float PerchedCaptionMetres(float photoMetres) =>
            Mathf.Clamp(photoMetres - OrbMetres - GapMetres, MinCaptionMetres, HomeCaptionMetres);

        void Show(string value, Color color)
        {
            shown = value;
            caption.color = color;
            caption.text = LastLines(caption, lines, value, MaxLines);
        }

        void FitCaption(float metres)
        {
            if (Mathf.Approximately(metres, captionMetres)) return;
            captionMetres = metres;
            caption.rectTransform.sizeDelta = new Vector2(metres * UnitsPerMetre, CaptionHeight);
            if (shown.Length > 0) caption.text = LastLines(caption, lines, shown, MaxLines);
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

            Choose(Time.time, false);
            FitCaption(CaptionMetres);
            var rest = Rest();
            if (!atPerch) rest += Vector3.up * (BobMetres * Mathf.Sin(Time.time * BobRate));
            var position = Vector3.Lerp(transform.position, rest, 1f - Mathf.Exp(-FollowRate * dt));
            // On a photo it lies in the photo's plane so the row reads as part of the snapshot; anywhere else it faces
            // the eye from where it is now.
            var facing = atPerch ? asked.transform.rotation : OrbPlacement.FacingEye(EyePose, position);
            var rotation = Quaternion.Slerp(transform.rotation, facing, 1f - Mathf.Exp(-TurnRate * dt));
            transform.SetPositionAndRotation(position, rotation);

            var goal = Mathf.Clamp01(Level);
            level = Mathf.Lerp(level, goal, 1f - Mathf.Exp(-(goal > level ? LevelAttack : LevelRelease) * dt));
            disc.localScale = Vector3.one * (1f + SpeakPulse * level);
            Breathe();
        }

        /// The ring behind the disc: a slow swell that fades as it grows while listening, and a ring that follows the
        /// voice's loudness while speaking.
        void Breathe()
        {
            var cyan = Theme.Cyan;
            if (listening)
            {
                var swell = 0.5f + 0.5f * Mathf.Sin(Time.time * PulseRate);
                halo.localScale = Vector3.one * (1f + 0.22f * swell);
                haloGlass.SetOutline(new Color(cyan.r, cyan.g, cyan.b, Mathf.Lerp(0.55f, 0.08f, swell)));
                return;
            }
            halo.localScale = Vector3.one * (1f + 0.3f * level);
            haloGlass.SetOutline(new Color(cyan.r, cyan.g, cyan.b, 0.12f + 0.4f * level));
        }
    }
}
