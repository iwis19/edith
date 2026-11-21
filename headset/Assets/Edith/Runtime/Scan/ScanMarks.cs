using System;
using System.Collections.Generic;
using Edith.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Edith.Scan
{
    /// What a scan draws in the room, each with its own shape so none is mistaken for another:
    /// - the Scan hold: two arcs of light grow from either palm and close into a circle, then sonar rings sweep out
    ///   while the reply is awaited;
    /// - a pin: a small glass bead that lands with one ripple and breathes;
    /// - looking at a pin: the bead swells while its own rim fills like a clock hand;
    /// - the Scrapbook hold: three card outlines rise and fan out between the palms.
    /// Plus the line of text for a scan that found nothing. Each is a world-space canvas that faces the eye and is
    /// scaled with its distance, so it reads the same from anywhere in the room.
    public sealed class ScanMarks : IDisposable
    {
        public sealed class Mark
        {
            internal GameObject Go;
            internal CanvasGroup Group;
            internal Vector3 At;
            internal float Fade, Target = 1f;
            internal Image Gaze, Pulse, Glow;
            internal RectTransform Bead;
            internal float Age, Look, Phase, Wait;
        }

        // 1 canvas unit is half a millimetre at ReferenceMeters.
        const float MetresPerUnit = 0.0005f, ReferenceMeters = 0.5f, MinScaleMeters = 0.3f;
        const float LabelWidth = 420f, LabelHeight = 160f;
        const float FadeSeconds = 0.25f;
        const float StatusSeconds = 2.5f;
        // The hold circle is about 9 cm across at ReferenceMeters.
        const float RingSize = 180f;
        // While the reply is awaited a sonar ring leaves the circle every SonarEvery seconds and grows to SonarGrowth
        // times its size over SonarSeconds.
        const float SonarEvery = 0.7f, SonarSeconds = 1.4f, SonarGrowth = 4.5f;
        // A bead is about 1.3 cm across; it breathes this much and this fast, and swells by LookSwell while looked at.
        const float BeadSize = 26f, BreatheScale = 0.06f, BreatheHz = 0.4f, LookSwell = 0.35f;
        // A new bead's single ripple grows to RippleGrowth times its size over RippleSeconds.
        const float RippleSeconds = 0.9f, RippleGrowth = 5f;
        // The Scrapbook fan: three cards this big, spread this far and turned this much when the hold is full.
        static readonly Vector2 CardSize = new Vector2(90f, 120f);
        const float CardSpread = 62f, CardTurn = 14f, CardRadius = 12f;
        static readonly Color Ice = Theme.Accent;
        static readonly Color Frost = Theme.Ice;
        static readonly Color TextColor = new Color(0.95f, 0.95f, 0.97f);
        static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.85f);

        readonly Font font;
        readonly Material material;
        readonly List<Mark> pins = new List<Mark>();
        readonly Sprite disc = Circle(64, 0f);
        readonly Sprite thinRing = Circle(256, 0.94f);
        readonly Sprite rimRing = Circle(256, 0.88f);
        readonly Sprite glow = Glow(128);
        readonly Sprite bead = Bead(128);
        readonly List<(Image image, float age)> sonar = new List<(Image, float)>();
        Mark ring, deck, status;
        Image ringFill;
        readonly Image[] arcs = new Image[4];
        readonly Glass[] cards = new Glass[3];
        Text statusText;
        float statusHideAt, sonarClock;
        float ringProgress, deckProgress;
        bool ringPulsing;

        public ScanMarks(Font font, Material material)
        {
            this.font = font;
            this.material = material;
        }

        /// A pin at `at`: a glass bead that lands with a ripple after `wait` seconds, when the scan's ping reaches it.
        /// Hand it to Remove to fade it out.
        public Mark AddPin(Vector3 at, float wait = 0f)
        {
            var pin = new Mark { At = at, Phase = UnityEngine.Random.value * 6f, Wait = wait };
            var root = NewCanvas("ScanPin", pin);
            pin.Pulse = Disc(root, thinRing, Ice, BeadSize);
            pin.Glow = Disc(root, glow, new Color(Ice.r, Ice.g, Ice.b, 0.35f), BeadSize * 2.6f);
            pin.Bead = new GameObject("Bead", typeof(RectTransform)).GetComponent<RectTransform>();
            pin.Bead.SetParent(root, false);
            Disc(pin.Bead, bead, Color.white, BeadSize);
            Disc(pin.Bead, thinRing, new Color(Frost.r, Frost.g, Frost.b, 0.85f), BeadSize);
            pin.Gaze = Filled(pin.Bead, rimRing, Frost, BeadSize * 1.55f);
            pins.Add(pin);
            return pin;
        }

        Image Disc(RectTransform root, Sprite sprite, Color color, float size)
        {
            var image = new GameObject("Mark", typeof(Image)).GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            image.material = material;
            image.rectTransform.SetParent(root, false);
            image.rectTransform.sizeDelta = new Vector2(size, size);
            return image;
        }

        /// A ring drawn from `origin` round to its fill amount.
        Image Filled(RectTransform root, Sprite sprite, Color color, float size, Image.Origin360 origin = Image.Origin360.Top, bool clockwise = true)
        {
            var image = Disc(root, sprite, color, size);
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Radial360;
            image.fillOrigin = (int)origin;
            image.fillClockwise = clockwise;
            image.fillAmount = 0f;
            return image;
        }

        /// Fills the rim around `pin` to `progress` while it is looked at, and swells the bead with it.
        public void SetGaze(Mark pin, float progress)
        {
            pin.Look = progress;
            pin.Gaze.fillAmount = progress;
        }

        public void Remove(Mark pin) => pin.Target = 0f;

        /// The Scan hold at `at`: two arcs closing to `progress` while the pose is held, the full circle sending out
        /// sonar while `pulsing`, and faded out when neither.
        public void SetRing(Vector3 at, float progress, bool pulsing)
        {
            if (ring == null)
            {
                if (progress <= 0f && !pulsing) return;
                ring = new Mark();
                var root = NewCanvas("ScanRing", ring);
                ringFill = Disc(root, disc, Color.clear, RingSize);
                // Each palm sends an arc up and an arc down; the four meet at the top and bottom.
                arcs[0] = Filled(root, thinRing, Ice, RingSize, Image.Origin360.Left, true);
                arcs[1] = Filled(root, thinRing, Ice, RingSize, Image.Origin360.Left, false);
                arcs[2] = Filled(root, thinRing, Ice, RingSize, Image.Origin360.Right, true);
                arcs[3] = Filled(root, thinRing, Ice, RingSize, Image.Origin360.Right, false);
            }
            ringProgress = progress;
            ringPulsing = pulsing;
            ring.Target = pulsing || progress > 0f ? 1f : 0f;
            if (ring.Target > 0f) ring.At = at;
        }

        /// The Scrapbook hold at `at`: three cards rising and fanning out as `progress` goes from 0 to 1.
        public void SetDeck(Vector3 at, float progress)
        {
            if (deck == null)
            {
                if (progress <= 0f) return;
                deck = new Mark();
                var root = NewCanvas("ScrapbookFan", deck);
                for (var i = 0; i < cards.Length; i++) cards[i] = Glass.Build(root, "Card", CardSize, CardRadius, material);
            }
            deckProgress = progress;
            deck.Target = progress > 0f ? 1f : 0f;
            if (deck.Target > 0f) deck.At = at;
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
                if (pin.Wait > 0f && pin.Target > 0f)
                {
                    pin.Wait -= Time.deltaTime;
                    Step(pin, 0f, eye, 1f);
                    continue;
                }
                Step(pin, step, eye, 1f);
                Ripple(pin);
                Breathe(pin);
                if (pin.Target > 0f || pin.Fade > 0f) continue;
                UnityEngine.Object.Destroy(pin.Go);
                pins.RemoveAt(i);
            }

            if (ring != null)
            {
                var closed = ringPulsing ? 1f : ringProgress;
                var eased = closed * closed * (3f - 2f * closed);
                foreach (var arc in arcs) arc.fillAmount = 0.25f * eased;
                ringFill.color = new Color(Ice.r, Ice.g, Ice.b, 0.12f * eased);
                Sonar(ring.Go.transform as RectTransform);
                Step(ring, step, eye, ringPulsing ? 0.85f : 1f);
            }

            if (deck != null)
            {
                Fan(deckProgress);
                Step(deck, step, eye, 1f);
            }

            if (status != null)
            {
                if (Time.time >= statusHideAt) status.Target = 0f;
                Step(status, step, eye, 1f);
            }
        }

        public void Dispose()
        {
            foreach (var pin in pins) UnityEngine.Object.Destroy(pin.Go);
            pins.Clear();
            if (ring != null) UnityEngine.Object.Destroy(ring.Go);
            if (deck != null) UnityEngine.Object.Destroy(deck.Go);
            if (status != null) UnityEngine.Object.Destroy(status.Go);
            foreach (var sprite in new[] { disc, thinRing, rimRing, glow, bead })
            {
                UnityEngine.Object.Destroy(sprite.texture);
                UnityEngine.Object.Destroy(sprite);
            }
        }

        /// While the reply is awaited, rings leave the circle in turn and grow out across the room as they fade.
        void Sonar(RectTransform root)
        {
            if (ringPulsing)
            {
                sonarClock -= Time.deltaTime;
                if (sonarClock <= 0f)
                {
                    sonarClock = SonarEvery;
                    sonar.Add((Disc(root, thinRing, Ice, RingSize), 0f));
                }
            }
            else sonarClock = 0f;
            for (var i = sonar.Count - 1; i >= 0; i--)
            {
                var (image, age) = sonar[i];
                age += Time.deltaTime;
                var t = age / SonarSeconds;
                if (t >= 1f)
                {
                    UnityEngine.Object.Destroy(image.gameObject);
                    sonar.RemoveAt(i);
                    continue;
                }
                var eased = 1f - (1f - t) * (1f - t);
                image.rectTransform.sizeDelta = Vector2.one * (RingSize * Mathf.Lerp(0.85f, SonarGrowth, eased));
                image.color = new Color(Ice.r, Ice.g, Ice.b, 0.7f * (1f - t));
                sonar[i] = (image, age);
            }
        }

        /// The three cards rise from one stack and fan left to right, each lighting up in turn as the hold fills.
        void Fan(float progress)
        {
            var spread = progress * progress * (3f - 2f * progress);
            for (var i = 0; i < cards.Length; i++)
            {
                var side = i - 1;
                var lit = Mathf.Clamp01(progress * 3f - i);
                var rect = cards[i].Rect;
                rect.anchoredPosition = new Vector2(side * CardSpread * spread, -(1f - spread) * 40f - Mathf.Abs(side) * 8f * spread);
                rect.localRotation = Quaternion.Euler(0f, 0f, -side * CardTurn * spread);
                cards[i].SetLook(new Color(Ice.r, Ice.g, Ice.b, 0.08f + 0.18f * lit), Color.clear,
                    new Color(Ice.r, Ice.g, Ice.b, 0.35f + 0.6f * lit), new Color(Ice.r, Ice.g, Ice.b, 0.45f * lit));
            }
        }

        // A new bead sends out one ring that grows and fades, so it is easy to spot where it landed.
        static void Ripple(Mark pin)
        {
            if (!pin.Pulse.enabled) return;
            pin.Age += Time.deltaTime;
            var t = pin.Age / RippleSeconds;
            if (t >= 1f)
            {
                pin.Pulse.enabled = false;
                return;
            }
            var eased = 1f - (1f - t) * (1f - t);
            pin.Pulse.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(BeadSize, BeadSize * RippleGrowth, eased);
            pin.Pulse.color = new Color(Ice.r, Ice.g, Ice.b, 0.8f * (1f - t));
        }

        // The bead and its glow breathe gently, and the bead swells while it is looked at.
        static void Breathe(Mark pin)
        {
            var breath = Mathf.Sin((Time.time * BreatheHz + pin.Phase) * 2f * Mathf.PI);
            var landed = Mathf.Clamp01(pin.Age / 0.25f + (pin.Pulse.enabled ? 0f : 1f));
            pin.Bead.localScale = Vector3.one * (landed * (1f + LookSwell * pin.Look) * (1f + BreatheScale * breath));
            pin.Glow.color = new Color(Ice.r, Ice.g, Ice.b, 0.25f + 0.1f * breath + 0.3f * pin.Look);
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
        static Sprite Circle(int size, float inner) => Baked(size, (radius, _, _) =>
        {
            var half = size * 0.5f;
            return Mathf.Clamp01(half - 1f - radius) * (inner > 0f ? Mathf.Clamp01(radius - half * inner) : 1f);
        });

        /// A soft round glow that fades from the centre out.
        static Sprite Glow(int size) => Baked(size, (radius, _, _) =>
        {
            var t = Mathf.Clamp01(radius / (size * 0.5f));
            return (1f - t) * (1f - t);
        });

        /// A glass bead: an ice-blue body, a frost highlight up and to the left, and a soft edge. White is the frost;
        /// the bead's colour comes from the pixels, so the image is drawn untinted.
        static Sprite Bead(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            var centre = (size - 1) * 0.5f;
            var half = size * 0.5f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                float dx = x - centre, dy = y - centre;
                var radius = Mathf.Sqrt(dx * dx + dy * dy);
                var edge = Mathf.Clamp01(half - 1f - radius);
                var lightX = dx + half * 0.3f;
                var lightY = dy - half * 0.35f;
                var shine = Mathf.Clamp01(1f - Mathf.Sqrt(lightX * lightX + lightY * lightY) / (half * 0.75f));
                var body = Color.Lerp(new Color(Ice.r, Ice.g, Ice.b, 0.55f), new Color(Ice.r, Ice.g, Ice.b, 0.95f), 1f - radius / half);
                var colour = Color.Lerp(body, new Color(Frost.r, Frost.g, Frost.b, 1f), shine * shine);
                colour.a *= edge;
                pixels[y * size + x] = colour;
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        static Sprite Baked(int size, Func<float, float, float, float> alpha)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            var centre = (size - 1) * 0.5f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                float dx = x - centre, dy = y - centre;
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha(Mathf.Sqrt(dx * dx + dy * dy), dx, dy));
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }
    }
}
