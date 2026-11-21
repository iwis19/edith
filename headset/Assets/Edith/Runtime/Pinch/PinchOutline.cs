using Edith.Hands;
using Edith.Snapshots;
using Edith.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Edith.Pinch
{
    /// Draws the box while it is dragged and freezes it on release: a glowing cyan rounded frame with a white sight
    /// corner just inside each corner of the box. Once the release is confirmed the snapshot's card takes its place.
    public sealed class PinchOutline : MonoBehaviour
    {
        // The frame is laid out in millimetres, like a snapshot.
        const float UnitsPerMetre = 1000f;
        const float RadiusMm = 12f;
        const float ArmMm = 9f, ThickMm = 1.4f, InsetMm = 8f;
        static readonly Color Edge = new Color(Theme.Cyan.r, Theme.Cyan.g, Theme.Cyan.b, 0.95f);

        [SerializeField] PinchInput input;
        /// While another panel holds it, a dragged pinch draws no box. Optional.
        [SerializeField] HandGate gate;

        RectTransform rect;
        Canvas canvas;
        Glass frame;
        readonly Image[] bars = new Image[8];

        void Awake()
        {
            // The old line is replaced by the frame.
            var line = GetComponent<LineRenderer>();
            if (line != null) line.enabled = false;
            Build();
        }

        void OnEnable() => input.Stepped += OnStepped;
        void OnDisable() => input.Stepped -= OnStepped;

        void OnDestroy()
        {
            if (rect != null) Destroy(rect.gameObject);
        }

        void OnStepped(PinchReading reading)
        {
            if (gate != null && gate.Held)
            {
                line.enabled = false;
                return;
            }
            switch (reading.Event)
            {
                case PinchEvent.Tap:
                case PinchEvent.Cancelled:
                case PinchEvent.Confirmed:
                    canvas.enabled = false;
                    return;
                case PinchEvent.Completed:
                    Show(reading.Box, 1f);
                    return;
            }
            if (reading.Phase == PinchPhase.Dragging && reading.HasBox) Show(reading.Box, 0.85f);
            else if (reading.Phase == PinchPhase.Pressed) canvas.enabled = false;
        }

        /// Not parented to this object, so the frame keeps world scale whatever the scene does to it.
        void Build()
        {
            var root = new GameObject("Pinch frame", typeof(RectTransform), typeof(Canvas));
            rect = (RectTransform)root.transform;
            rect.localScale = Vector3.one / UnitsPerMetre;
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.enabled = false;

            frame = Glass.Build(rect, "Frame", Vector2.one * 100f, RadiusMm, null);
            for (var i = 0; i < bars.Length; i++)
            {
                var bar = new GameObject("Corner", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                bar.transform.SetParent(rect, false);
                bar.color = Theme.Ice;
                bar.raycastTarget = false;
                bars[i] = bar;
            }
        }

        void Show(in SightBox box, float strength)
        {
            var size = box.Size * UnitsPerMetre;
            rect.SetPositionAndRotation(box.Center, box.Eye.rotation);
            rect.sizeDelta = size;
            frame.Resize(size);
            frame.SetLook(new Color(Theme.Cyan.r, Theme.Cyan.g, Theme.Cyan.b, 0.04f * strength),
                new Color(Theme.Cyan.r, Theme.Cyan.g, Theme.Cyan.b, 0.3f * strength),
                new Color(Edge.r, Edge.g, Edge.b, Edge.a * strength),
                new Color(Theme.Cyan.r, Theme.Cyan.g, Theme.Cyan.b, 0.5f * strength));

            // Corners shrink with a small box so they never cross.
            var shortSide = Mathf.Min(size.x, size.y);
            float arm = Mathf.Min(ArmMm, shortSide * 0.18f), inset = Mathf.Min(InsetMm, shortSide * 0.12f);
            float x = size.x * 0.5f - inset, y = size.y * 0.5f - inset;
            for (var i = 0; i < 4; i++)
            {
                float sx = i % 2 == 0 ? -1f : 1f, sy = i < 2 ? 1f : -1f;
                Bar(bars[i * 2], new Vector2(arm, ThickMm), new Vector2(sx * (x - arm * 0.5f), sy * (y - ThickMm * 0.5f)));
                Bar(bars[i * 2 + 1], new Vector2(ThickMm, arm), new Vector2(sx * (x - ThickMm * 0.5f), sy * (y - arm * 0.5f)));
            }
            canvas.enabled = true;
        }

        static void Bar(Image bar, Vector2 size, Vector2 position)
        {
            var r = bar.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.sizeDelta = size;
            r.anchoredPosition = position;
        }
    }
}
