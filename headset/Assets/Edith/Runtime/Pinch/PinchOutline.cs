using Edith.Snapshots;
using UnityEngine;

namespace Edith.Pinch
{
    /// Draws the box while it is dragged and freezes it on release. Once the release is confirmed the snapshot's photo
    /// takes its place.
    [RequireComponent(typeof(LineRenderer))]
    public sealed class PinchOutline : MonoBehaviour
    {
        const float DragWidth = 0.0015f;
        const float LockedWidth = 0.003f;

        [SerializeField] PinchInput input;

        LineRenderer line;
        readonly Vector3[] corners = new Vector3[4];

        void Awake()
        {
            line = GetComponent<LineRenderer>();
            line.enabled = false;
        }

        void OnEnable() => input.Stepped += OnStepped;
        void OnDisable() => input.Stepped -= OnStepped;

        void OnStepped(PinchReading reading)
        {
            switch (reading.Event)
            {
                case PinchEvent.Tap:
                case PinchEvent.Cancelled:
                case PinchEvent.Confirmed:
                    line.enabled = false;
                    return;
                case PinchEvent.Completed:
                    Show(reading.Box, LockedWidth);
                    return;
            }
            if (reading.Phase == PinchPhase.Dragging && reading.HasBox) Show(reading.Box, DragWidth);
            else if (reading.Phase == PinchPhase.Pressed) line.enabled = false;
        }

        void Show(in SightBox box, float width)
        {
            var t = box.Tangent;
            corners[0] = At(box, t.xMin, t.yMin);
            corners[1] = At(box, t.xMax, t.yMin);
            corners[2] = At(box, t.xMax, t.yMax);
            corners[3] = At(box, t.xMin, t.yMax);
            line.SetPositions(corners);
            line.widthMultiplier = width;
            line.enabled = true;
        }

        static Vector3 At(in SightBox box, float x, float y) =>
            box.Eye.position + box.Eye.rotation * new Vector3(x * box.Depth, y * box.Depth, box.Depth);
    }
}
