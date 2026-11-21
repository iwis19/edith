using UnityEngine;
using UnityEngine.UI;

namespace Edith.Snapshots
{
    /// A snapshot photo hanging where its box was drawn, at the box's size, facing the eye that drew it. It fades in
    /// when placed, fades out while a new box covers it, and fades out for good when it leaves the room.
    public sealed class Snapshot : MonoBehaviour
    {
        const float FadeSeconds = 0.25f;
        // The canvas is laid out in millimetres so card text can use ordinary font sizes.
        const float UnitsPerMetre = 1000f;
        static readonly Color EmptyPhoto = new Color(1f, 1f, 1f, 0.35f);

        CanvasGroup group;
        float fade;
        float target = 1f;

        public SightBox Box { get; private set; }
        public bool Leaving { get; private set; }

        /// Shows on the plane of `box`. `parent` must have unit world scale. A null `photo` shows a plain panel.
        public static Snapshot Create(in SightBox box, Transform parent, Texture photo = null)
        {
            var root = new GameObject("Snapshot", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            var rect = (RectTransform)root.transform;
            rect.SetParent(parent, false);
            rect.SetPositionAndRotation(box.Center, box.Eye.rotation);
            rect.localScale = Vector3.one / UnitsPerMetre;
            rect.sizeDelta = box.Size * UnitsPerMetre;
            root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;

            var image = new GameObject("Photo", typeof(RectTransform), typeof(RawImage));
            var imageRect = (RectTransform)image.transform;
            imageRect.SetParent(rect, false);
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.sizeDelta = Vector2.zero;
            var raw = image.GetComponent<RawImage>();
            raw.raycastTarget = false;
            raw.texture = photo;
            if (photo == null) raw.color = EmptyPhoto;

            var snapshot = root.AddComponent<Snapshot>();
            snapshot.Box = box;
            snapshot.group = root.GetComponent<CanvasGroup>();
            snapshot.group.alpha = 0f;
            snapshot.group.interactable = false;
            snapshot.group.blocksRaycasts = false;
            return snapshot;
        }

        /// False while a box being drawn covers it and once it is leaving.
        public bool Visible => target > 0f;

        /// Fades out while a box being drawn covers it, and back in when the box moves off. Ignored once leaving.
        public void SetCovered(bool covered)
        {
            if (Leaving) return;
            target = covered ? 0f : 1f;
            enabled = true;
        }

        /// Fades out, then destroys the snapshot.
        public void Leave()
        {
            Leaving = true;
            target = 0f;
            enabled = true;
        }

        void Update()
        {
            fade = Mathf.MoveTowards(fade, target, Time.deltaTime / FadeSeconds);
            group.alpha = fade * fade * (3f - 2f * fade);
            if (fade != target) return;
            if (Leaving) Destroy(gameObject);
            else enabled = false;
        }
    }
}
