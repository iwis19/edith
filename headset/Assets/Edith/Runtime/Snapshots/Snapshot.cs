using Edith.Look;
using Edith.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Edith.Snapshots
{
    /// A snapshot photo hanging where its box was drawn, at the box's size, facing the eye that drew it, with the
    /// card the backend writes about it underneath. It fades in when placed with its card opening down out of the
    /// photo, fades out while a new box covers it, and fades out for good when it leaves the room. A fist can carry it
    /// off its hook and it floats back, or shrink it into a pocket point.
    public sealed class Snapshot : MonoBehaviour
    {
        const float FadeSeconds = 0.25f;
        const float PocketSeconds = 0.3f;
        // A new card opens out of the box round its photo while the backend is still looking.
        const float OpenSeconds = 1.2f;
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
        float opened;
        float looked;
        bool carried;
        Vector3 carryPosition;
        Quaternion carryRotation;
        bool pocketing;
        float pocketProgress;
        Vector3 pocketFrom;
        Vector3 pocketPoint;
        Vector3 fullScale;
        CardView view;
        string caption;
        // The photo as drawn, with its corners cut round. The photo itself stays whole, since Ask sends it on.
        Texture2D shownPhoto;

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
            Theme.Sharpen(root);

            var image = new GameObject("Photo", typeof(RectTransform), typeof(RawImage));
            var imageRect = (RectTransform)image.transform;
            imageRect.SetParent(rect, false);
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.sizeDelta = Vector2.zero;
            var raw = image.GetComponent<RawImage>();
            raw.raycastTarget = false;
            raw.material = material;
            var shown = Rounded(photo, CardView.PhotoRadiusMm(box.Depth) / (box.Size.x * UnitsPerMetre));
            raw.texture = shown != null ? shown : photo;
            if (photo == null) raw.color = EmptyPhoto;

            var snapshot = root.AddComponent<Snapshot>();
            snapshot.Id = nextId++;
            snapshot.Box = box;
            snapshot.Photo = photo;
            snapshot.shownPhoto = shown;
            snapshot.group = root.GetComponent<CanvasGroup>();
            snapshot.group.alpha = 0f;
            snapshot.group.interactable = false;
            snapshot.group.blocksRaycasts = false;
            snapshot.view = CardView.Build(rect, box.Size * UnitsPerMetre, box.Depth, font, material);
            snapshot.view.Opened = 0f;
            snapshot.view.Show(snapshot.Card, null);
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

        public void Apply(in LookEvent e)
        {
            var rated = Card.Tier != null;
            Card.Apply(e);
            // The cue plays once, as the rarity arrives, for a Look and for a scan pin opened as one.
            if (!rated && Card.Tier != null) RaritySounds.Play(Card.Tier, transform.position);
            view.Show(Card, caption, looked);
            enabled = true;
        }

        /// Text the card says in place of the backend's fact, such as a scan pin's hook. The backend's answer still
        /// colours the card.
        public string Caption
        {
            get => caption;
            set
            {
                caption = value;
                view.Show(Card, caption, looked);
                enabled = true;
            }
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

        /// Moves the point a pocketed snapshot shrinks into, so it follows the hand that took it.
        public void PocketToward(Vector3 point)
        {
            if (pocketing) pocketPoint = point;
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
            if (shownPhoto != null) Destroy(shownPhoto);
        }

        /// A copy of `photo` whose corners are transparent outside a radius of `radiusShare` of its width, or null
        /// when the photo can't be read.
        internal static Texture2D Rounded(Texture photo, float radiusShare)
        {
            if (!(photo is Texture2D source) || !source.isReadable) return null;
            int w = source.width, h = source.height;
            var pixels = source.GetPixels32();
            var radius = Mathf.Clamp(radiusShare * w, 0f, Mathf.Min(w, h) * 0.5f);
            float hx = w * 0.5f, hy = h * 0.5f;
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                float qx = Mathf.Abs(x + 0.5f - hx) - (hx - radius), qy = Mathf.Abs(y + 0.5f - hy) - (hy - radius);
                if (qx <= 0f || qy <= 0f) continue;
                var alpha = Mathf.Clamp01(radius + 0.5f - Mathf.Sqrt(qx * qx + qy * qy));
                pixels[y * w + x].a = (byte)(alpha * 255f);
            }
            var rounded = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            rounded.SetPixels32(pixels);
            rounded.Apply(false, true);
            return rounded;
        }

        void Update() => Step(Time.deltaTime);

        /// Moves its fade, opening, pose and typing on by `seconds`. Update calls it every frame.
        public void Step(float seconds)
        {
            fade = Mathf.MoveTowards(fade, target, seconds / FadeSeconds);
            group.alpha = fade * fade * (3f - 2f * fade);
            var opening = opened < 1f;
            if (opening)
            {
                opened = Mathf.MoveTowards(opened, 1f, seconds / OpenSeconds);
                view.Opened = opened;
            }
            var settled = pocketing ? Shrink(seconds) : Follow(seconds);
            var typing = Card.Typing && !Leaving;
            var looking = CardView.IsLooking(Card, caption) && !Leaving;
            if (looking) looked += seconds;
            if (typing) Card.Advance(seconds);
            if (typing || looking) view.Show(Card, caption, looked);
            if (fade != target || !settled || typing || looking || opening) return;
            if (Leaving) Destroy(gameObject);
            else enabled = false;
        }

        bool Shrink(float seconds)
        {
            pocketProgress = Mathf.MoveTowards(pocketProgress, 1f, seconds / PocketSeconds);
            var eased = pocketProgress * pocketProgress * (3f - 2f * pocketProgress);
            transform.position = Vector3.Lerp(pocketFrom, pocketPoint, eased);
            transform.localScale = fullScale * (1f - eased);
            return pocketProgress >= 1f;
        }

        /// True once it has come to rest at its box.
        bool Follow(float seconds)
        {
            var goalPosition = carried ? carryPosition : Box.Center;
            var goalRotation = carried ? carryRotation : Box.Eye.rotation;
            var blend = 1f - Mathf.Exp(-(carried ? CarryFollow : ReturnFollow) * seconds);
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
