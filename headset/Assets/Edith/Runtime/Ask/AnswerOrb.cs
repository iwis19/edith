using System;
using System.Collections.Generic;
using System.Linq;
using Edith.Snapshots;
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
        const int SourcesFontSize = 36;
        public const int MaxSources = 3;
        public const float HomeCaptionMetres = 0.26f, MinCaptionMetres = 0.1f;
        static readonly Color Quiet = new Color(0.78f, 0.8f, 0.85f, 0.85f);
        static readonly Color Bright = new Color(0.97f, 0.97f, 0.99f, 1f);
        static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.85f);
        static readonly Color Core = new Color(0.95f, 0.98f, 1f, 1f);
        static readonly Color Rim = new Color(0.45f, 0.72f, 1f, 1f);

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
        Text caption, sources;
        RectTransform disc;
        Texture2D discTexture;
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
            // Drawn after the snapshots whatever the distances, since transparent UI sorts by distance otherwise.
            canvas.sortingOrder = 1;

            var orb = root.AddComponent<AnswerOrb>();
            orb.eye = eye;
            orb.room = room;
            orb.asked = asked;
            orb.group = root.GetComponent<CanvasGroup>();
            orb.group.interactable = false;
            orb.group.blocksRaycasts = false;

            orb.discTexture = Disc();
            var image = new GameObject("Disc", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            image.texture = orb.discTexture;
            image.raycastTarget = false;
            image.material = material;
            orb.disc = image.rectTransform;
            orb.disc.SetParent(rect, false);
            orb.disc.anchorMin = Vector2.zero;
            orb.disc.anchorMax = Vector2.one;
            orb.disc.sizeDelta = Vector2.zero;

            var text = new GameObject("Caption", typeof(RectTransform), typeof(Text), typeof(UnityEngine.UI.Shadow)).GetComponent<Text>();
            text.font = font;
            text.fontSize = FontSize;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.material = material;
            var shadow = text.GetComponent<UnityEngine.UI.Shadow>();
            shadow.effectColor = ShadowColor;
            shadow.effectDistance = new Vector2(2f, -2f);
            var captionRect = text.rectTransform;
            captionRect.SetParent(rect, false);
            captionRect.anchorMin = captionRect.anchorMax = new Vector2(1f, 0.5f);
            captionRect.pivot = new Vector2(0f, 0.5f);
            captionRect.anchoredPosition = new Vector2(GapMetres * UnitsPerMetre, 0f);
            orb.caption = text;

            var names = new GameObject("Sources", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            names.font = font;
            names.fontSize = SourcesFontSize;
            names.color = Quiet;
            names.alignment = TextAnchor.UpperLeft;
            names.horizontalOverflow = HorizontalWrapMode.Wrap;
            names.verticalOverflow = VerticalWrapMode.Truncate;
            names.raycastTarget = false;
            names.material = material;
            var namesRect = names.rectTransform;
            namesRect.SetParent(rect, false);
            namesRect.anchorMin = namesRect.anchorMax = new Vector2(1f, 0.5f);
            namesRect.pivot = new Vector2(0f, 1f);
            namesRect.anchoredPosition = new Vector2(GapMetres * UnitsPerMetre, -CaptionHeight * 0.5f);
            orb.sources = names;

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

        public void ShowListening() => Show("listening…", Quiet);
        public void ShowProblem(string line) => Show(line, Quiet);

        /// Shows `words`, what was heard so far, in quotes. Nothing heard yet leaves the caption as it is.
        public void ShowHeard(string words)
        {
            var quoted = Quoted(words);
            if (quoted.Length > 0) Show(quoted, Quiet);
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
            Show(text.Substring(0, revealed), Bright);
        }

        /// Shows up to three of `names` as one quiet line under the caption; nothing when there are none.
        public void ShowSources(IReadOnlyList<string> names)
        {
            sources.text = SourcesLine(names);
            sources.rectTransform.sizeDelta = new Vector2(captionMetres * UnitsPerMetre, CaptionHeight * 0.5f);
        }

        /// The first three of `names` joined by a middle dot, led by "Sources:". Empty when there are none.
        public static string SourcesLine(IReadOnlyList<string> names)
        {
            if (names == null || names.Count == 0) return "";
            return "Sources: " + string.Join(" · ", names.Take(MaxSources));
        }

        /// `words` with its spacing tidied, in curly quotes, or empty when nothing was heard.
        public static string Quoted(string words)
        {
            var tidy = string.Join(" ", (words ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
            return tidy.Length == 0 ? "" : "“" + tidy + "”";
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
            sources.rectTransform.sizeDelta = new Vector2(metres * UnitsPerMetre, CaptionHeight * 0.5f);
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
        }

        void OnDestroy()
        {
            if (discTexture != null) Destroy(discTexture);
        }

        /// A soft disc, white at the centre and blue toward a feathered rim.
        static Texture2D Disc()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "EDITH orb", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            var centre = (size - 1) * 0.5f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var r = new Vector2(x - centre, y - centre).magnitude / (size * 0.5f);
                var colour = Color.Lerp(Core, Rim, Mathf.SmoothStep(0f, 1f, r * 1.3f));
                colour.a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 1f, r));
                pixels[y * size + x] = colour;
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
