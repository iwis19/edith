using Edith.Look;
using UnityEngine;
using UnityEngine.UI;

namespace Edith.Snapshots
{
    /// A snapshot photo hanging where its box was drawn, at the box's size, facing the eye that drew it, with the
    /// card the backend writes about it underneath. It fades in when placed, fades out while a new box covers it,
    /// and fades out for good when it leaves the room. A fist can carry it off its hook and it floats back, or
    /// shrink it into a pocket point.
    public sealed class Snapshot : MonoBehaviour
    {
        const float FadeSeconds = 0.25f;
        const float PocketSeconds = 0.3f;
        // Rates of the exponential approach to the pose, per second. A carried card trails the hand slightly,
        // which hides tracking jitter; a released one drifts back more slowly.
        const float CarryFollow = 30f;
        const float ReturnFollow = 10f;
        // The canvas is laid out in millimetres so card text can use ordinary font sizes.
        const float UnitsPerMetre = 1000f;
        static readonly Color EmptyPhoto = new Color(1f, 1f, 1f, 0.35f);

        static int nextId;

        CanvasGroup group;
        float fade;
        float target = 1f;
        bool carried;
        Vector3 carryPosition;
        Quaternion carryRotation;
        bool pocketing;
        float pocketProgress;
        Vector3 pocketFrom;
        Vector3 pocketPoint;
        Vector3 fullScale;
        CardView view;

        public SightBox Box { get; private set; }

        /// The photo it shows, or null for a plain panel.
        public Texture Photo { get; private set; }
        public bool Leaving { get; private set; }

        /// What the backend has said about the photo so far. Feed it events through Apply.
        public LookCard Card { get; } = new LookCard();

        /// Unique among snapshots, and stable for as long as this one lives.
        public int Id { get; private set; }

        /// Shows on the plane of `box`. `parent` must have unit world scale. A null `photo` shows a plain panel; any
        /// other is destroyed with the snapshot. `material`, when given, draws the photo so real objects in front
        /// of it hide it. `font` writes the card's text.
        public static Snapshot Create(in SightBox box, Transform parent, Texture photo = null, Material material = null, Font font = null)
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
            raw.material = material;
            raw.texture = photo;
            if (photo == null) raw.color = EmptyPhoto;

            var snapshot = root.AddComponent<Snapshot>();
            snapshot.Id = nextId++;
            snapshot.Box = box;
            snapshot.Photo = photo;
            snapshot.group = root.GetComponent<CanvasGroup>();
            snapshot.group.alpha = 0f;
            snapshot.group.interactable = false;
            snapshot.group.blocksRaycasts = false;
            snapshot.view = CardView.Build(rect, box.Size * UnitsPerMetre, font, material);
            snapshot.view.Show(snapshot.Card);
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

        /// Updates the card with one event from the backend.
        public void Apply(in LookEvent e)
        {
            Card.Apply(e);
            view.Show(Card);
            enabled = true;
        }

        /// Fades out, then destroys the snapshot.
        public void Leave()
        {
            Leaving = true;
            carried = false;
            target = 0f;
            enabled = true;
        }

        /// Follows `position` and `rotation` until FloatBack or Leave. Ignored once leaving.
        public void Hold(Vector3 position, Quaternion rotation)
        {
            if (Leaving) return;
            carried = true;
            carryPosition = position;
            carryRotation = rotation;
            enabled = true;
        }

        /// Eases back to the box it was drawn in.
        public void FloatBack()
        {
            carried = false;
            enabled = true;
        }

        /// Shrinks and fades into `point`, then destroys the snapshot.
        public void Pocket(Vector3 point)
        {
            Leave();
            pocketing = true;
            pocketFrom = transform.position;
            pocketPoint = point;
            fullScale = transform.localScale;
        }

        void OnDestroy()
        {
            if (Photo != null) Destroy(Photo);
        }

        void Update()
        {
            fade = Mathf.MoveTowards(fade, target, Time.deltaTime / FadeSeconds);
            group.alpha = fade * fade * (3f - 2f * fade);
            var settled = pocketing ? Shrink() : Follow();
            var typing = Card.Typing && !Leaving;
            if (typing)
            {
                Card.Advance(Time.deltaTime);
                view.Show(Card);
            }
            if (fade != target || !settled || typing) return;
            if (Leaving) Destroy(gameObject);
            else enabled = false;
        }

        bool Shrink()
        {
            pocketProgress = Mathf.MoveTowards(pocketProgress, 1f, Time.deltaTime / PocketSeconds);
            var eased = pocketProgress * pocketProgress * (3f - 2f * pocketProgress);
            transform.position = Vector3.Lerp(pocketFrom, pocketPoint, eased);
            transform.localScale = fullScale * (1f - eased);
            return pocketProgress >= 1f;
        }

        /// True once it has come to rest at its box.
        bool Follow()
        {
            var goalPosition = carried ? carryPosition : Box.Center;
            var goalRotation = carried ? carryRotation : Box.Eye.rotation;
            var blend = 1f - Mathf.Exp(-(carried ? CarryFollow : ReturnFollow) * Time.deltaTime);
            var position = Vector3.Lerp(transform.position, goalPosition, blend);
            var rotation = Quaternion.Slerp(transform.rotation, goalRotation, blend);
            var arrived = !carried
                && (position - goalPosition).sqrMagnitude < 1e-6f
                && Quaternion.Angle(rotation, goalRotation) < 0.1f;
            if (arrived)
            {
                position = goalPosition;
                rotation = goalRotation;
            }
            transform.SetPositionAndRotation(position, rotation);
            return arrived;
        }
    }
}
