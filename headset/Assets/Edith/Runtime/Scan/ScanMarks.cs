using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Edith.Scan
{
    /// What a scan draws in the room: a dot with a hook on each pin, the ring that fills between the palms, and the
    /// line of text for a scan that found nothing. Each is a world-space canvas that faces the eye and is scaled with
    /// its distance, so text reads the same from anywhere in the room.
    public sealed class ScanMarks : IDisposable
    {
        public sealed class Mark
        {
            internal GameObject Go;
            internal CanvasGroup Group;
            internal Vector3 At;
            internal float Fade, Target = 1f;
            internal Image Gaze;
        }

        // 1 canvas unit is half a millimetre at ReferenceMeters.
        const float MetresPerUnit = 0.0005f, ReferenceMeters = 0.5f, MinScaleMeters = 0.3f;
        const float DotSize = 28f, LabelWidth = 420f, LabelHeight = 160f, LabelGap = 10f;
        const float FadeSeconds = 0.25f;
        const float StatusSeconds = 2.5f;
        // The ring is about 9 cm across at ReferenceMeters. Its line is a share of its radius, and it breathes this
        // fast and this much while the reply is awaited.
        const float RingSize = 180f, RingThickness = 0.14f, PulseHz = 1.4f, PulseScale = 0.08f;
        const float GazeSize = 84f, CursorSize = 36f;
        static readonly Color GazeColor = new Color(0.35f, 0.9f, 0.5f);
        static readonly Color CursorColor = new Color(0.95f, 0.95f, 0.97f, 0.8f);
        static readonly Color TextColor = new Color(0.95f, 0.95f, 0.97f);
        static readonly Color HoldColor = new Color(0.95f, 0.95f, 0.97f, 0.55f);
        static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.85f);

        readonly Font font;
        readonly Material material;
        readonly List<Mark> pins = new List<Mark>();
        readonly Sprite dot = Circle(64, 0f);
        readonly Sprite ringSprite = Circle(256, 1f - RingThickness);
        Mark ring, status, cursor;
        Image ringFill;
        Text statusText;
        float statusHideAt;
        float ringProgress;
        bool ringPulsing;

        public ScanMarks(Font font, Material material)
        {
            this.font = font;
            this.material = material;
        }

        /// A pin at `at` that fades in. Hand it to Remove to fade it out.
        public Mark AddPin(Vector3 at, string label)
        {
            var pin = new Mark { At = at };
            var root = NewCanvas("ScanPin", pin);
            var image = new GameObject("Dot", typeof(Image), typeof(Shadow)).GetComponent<Image>();
            image.sprite = dot;
            image.color = TextColor;
            image.raycastTarget = false;
            image.material = material;
            image.GetComponent<Shadow>().effectColor = ShadowColor;
            image.rectTransform.SetParent(root, false);
            image.rectTransform.sizeDelta = new Vector2(DotSize, DotSize);
            Label(root, 19, TextAnchor.LowerCenter, new Vector2(0f, DotSize * 0.5f + LabelGap + LabelHeight * 0.5f)).text = label;
            var gaze = new GameObject("Gaze", typeof(Image)).GetComponent<Image>();
            gaze.sprite = ringSprite;
            gaze.type = Image.Type.Filled;
            gaze.fillMethod = Image.FillMethod.Radial360;
            gaze.fillOrigin = (int)Image.Origin360.Top;
            gaze.fillClockwise = true;
            gaze.fillAmount = 0f;
            gaze.color = GazeColor;
            gaze.raycastTarget = false;
            gaze.material = material;
            gaze.rectTransform.SetParent(root, false);
            gaze.rectTransform.sizeDelta = new Vector2(GazeSize, GazeSize);
            pin.Gaze = gaze;
            pins.Add(pin);
            return pin;
        }

        /// Fills the ring around `pin` to `progress` while it is looked at.
        public void SetGaze(Mark pin, float progress) => pin.Gaze.fillAmount = progress;

        /// A small ring at `at`, where the head points, while `show`. Pins open by where the head points, not the
        /// eyes, so this shows what aims.
        public void SetCursor(Vector3 at, bool show)
        {
            if (cursor == null)
            {
                if (!show) return;
                cursor = new Mark();
                var root = NewCanvas("ScanCursor", cursor);
                var image = new GameObject("Cursor", typeof(Image)).GetComponent<Image>();
                image.sprite = ringSprite;
                image.color = CursorColor;
                image.raycastTarget = false;
                image.material = material;
                image.rectTransform.SetParent(root, false);
                image.rectTransform.sizeDelta = new Vector2(CursorSize, CursorSize);
            }
            cursor.Target = show ? 1f : 0f;
            if (show) cursor.At = at;
        }

        public void Remove(Mark pin) => pin.Target = 0f;

        /// The ring at `at`: filled to `progress` while the pose is held, full and breathing while `pulsing`, and
        /// faded out when neither.
        public void SetRing(Vector3 at, float progress, bool pulsing)
        {
            if (ring == null)
            {
                if (progress <= 0f && !pulsing) return;
                ring = new Mark();
                var root = NewCanvas("ScanRing", ring);
                ringFill = new GameObject("Ring", typeof(Image)).GetComponent<Image>();
                ringFill.sprite = ringSprite;
                ringFill.type = Image.Type.Filled;
                ringFill.fillMethod = Image.FillMethod.Radial360;
                ringFill.fillOrigin = (int)Image.Origin360.Top;
                ringFill.fillClockwise = true;
                ringFill.raycastTarget = false;
                ringFill.rectTransform.SetParent(root, false);
                ringFill.rectTransform.sizeDelta = new Vector2(RingSize, RingSize);
            }
            ringProgress = progress;
            ringPulsing = pulsing;
            ring.Target = pulsing || progress > 0f ? 1f : 0f;
            if (ring.Target > 0f) ring.At = at;
        }

        /// One line of text at `at` for a few seconds.
        public void ShowStatus(Vector3 at, string message)
        {
            if (status == null)
            {
                status = new Mark();
                statusText = Label(NewCanvas("ScanStatus", status), 22, TextAnchor.MiddleCenter, Vector2.zero);
            }
            status.At = at;
            statusText.text = message;
            status.Target = 1f;
            statusHideAt = Time.time + StatusSeconds;
        }

        public void HideStatus() => statusHideAt = 0f;

        /// Once per frame: fades every mark, turns it to `eye` and destroys the pins that have faded out.
        public void Tick(Vector3 eye)
        {
            var step = Time.deltaTime / FadeSeconds;
            for (var i = pins.Count - 1; i >= 0; i--)
            {
                var pin = pins[i];
                Step(pin, step, eye, 1f);
                if (pin.Target > 0f || pin.Fade > 0f) continue;
                UnityEngine.Object.Destroy(pin.Go);
                pins.RemoveAt(i);
            }

            if (ring != null)
            {
                ringFill.fillAmount = ringPulsing ? 1f : ringProgress;
                ringFill.color = ringPulsing ? TextColor : Color.Lerp(HoldColor, TextColor, ringProgress);
                Step(ring, step, eye, ringPulsing ? 1f + PulseScale * Mathf.Sin(Time.time * 2f * Mathf.PI * PulseHz) : 1f);
            }

            if (status != null)
            {
                if (Time.time >= statusHideAt) status.Target = 0f;
                Step(status, step, eye, 1f);
            }

            if (cursor != null) Step(cursor, step, eye, 1f);
        }

        public void Dispose()
        {
            foreach (var pin in pins) UnityEngine.Object.Destroy(pin.Go);
            pins.Clear();
            if (ring != null) UnityEngine.Object.Destroy(ring.Go);
            if (status != null) UnityEngine.Object.Destroy(status.Go);
            if (cursor != null) UnityEngine.Object.Destroy(cursor.Go);
            foreach (var sprite in new[] { dot, ringSprite })
            {
                UnityEngine.Object.Destroy(sprite.texture);
                UnityEngine.Object.Destroy(sprite);
            }
        }

        static void Step(Mark mark, float step, Vector3 eye, float scale)
        {
            mark.Fade = Mathf.MoveTowards(mark.Fade, mark.Target, step);
            mark.Group.alpha = mark.Fade * mark.Fade * (3f - 2f * mark.Fade);
            var away = mark.At - eye;
            var distance = away.magnitude;
            if (distance < 1e-4f) return;
            var transform = mark.Go.transform;
            transform.SetPositionAndRotation(mark.At, Quaternion.LookRotation(away, Vector3.up));
            transform.localScale = Vector3.one * (scale * MetresPerUnit * Mathf.Max(distance, MinScaleMeters) / ReferenceMeters);
        }

        static RectTransform NewCanvas(string name, Mark mark)
        {
            mark.Go = new GameObject(name, typeof(Canvas));
            mark.Go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            mark.Group = mark.Go.AddComponent<CanvasGroup>();
            mark.Group.alpha = 0f;
            mark.Group.interactable = false;
            mark.Group.blocksRaycasts = false;
            var root = (RectTransform)mark.Go.transform;
            root.sizeDelta = Vector2.zero;
            return root;
        }

        Text Label(RectTransform parent, int size, TextAnchor anchor, Vector2 position)
        {
            var text = new GameObject("Text", typeof(Text), typeof(Shadow)).GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = TextColor;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.material = material;
            var shadow = text.GetComponent<Shadow>();
            shadow.effectColor = ShadowColor;
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
            text.rectTransform.SetParent(parent, false);
            text.rectTransform.sizeDelta = new Vector2(LabelWidth, LabelHeight);
            text.rectTransform.anchoredPosition = position;
            return text;
        }

        /// A white disc with soft edges, hollow inside `inner` of its radius. Unity's built-in round sprite cannot be
        /// loaded by name in a player.
        static Sprite Circle(int size, float inner)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            var centre = (size - 1) * 0.5f;
            var hole = size * 0.5f * inner;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var radius = Mathf.Sqrt((x - centre) * (x - centre) + (y - centre) * (y - centre));
                var alpha = Mathf.Clamp01(size * 0.5f - 1f - radius) * (inner > 0f ? Mathf.Clamp01(radius - hole) : 1f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }
    }
}
