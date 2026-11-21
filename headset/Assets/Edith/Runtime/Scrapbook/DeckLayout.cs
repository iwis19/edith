using UnityEngine;

namespace Edith.Scrapbook
{
    /// Where one card of the deck sits, in the deck's frame.
    public readonly struct CardPose
    {
        public readonly Vector3 Position;
        /// Degrees about the up axis. Positive turns the card's face toward the left, as a card on the right needs.
        public readonly float Yaw;
        public readonly float Scale;
        public readonly float Alpha;
        /// The card's name, fact and caption, which only the card in the middle shows.
        public readonly float TextAlpha;

        public CardPose(Vector3 position, float yaw, float scale, float alpha, float textAlpha)
        {
            Position = position;
            Yaw = yaw;
            Scale = scale;
            Alpha = alpha;
            TextAlpha = textAlpha;
        }

        public Quaternion Rotation => Quaternion.Euler(0f, Yaw, 0f);
    }

    /// The shape of the deck: one card in the middle at full size, facing the user, and the cards beside it folded
    /// toward it, smaller and further back. The deck's frame has its origin at the eye when the Scrapbook opened,
    /// level, with +z ahead and +x to the right. Distances are metres.
    public static class DeckLayout
    {
        /// The card in the middle is this wide at Scale 1.
        public const float CardWidth = ScrapbookPlacement.CardWidth;
        /// The room a card's photo is fitted into is this high.
        public const float CardHeight = CardWidth * 0.75f;
        /// A card further than this from the middle is not drawn, so at most 2 * Reach + 1 cards exist.
        public const int Reach = 3;

        const float FirstAside = ScrapbookPlacement.FirstAside, NextAside = ScrapbookPlacement.NextAside, Back = ScrapbookPlacement.Back;
        const float SideYaw = 50f, SideScale = 0.7f;
        // Cards up to this far from the middle are fully drawn; they fade out over the next card's width.
        const float SolidReach = 2f;
        const float TextReach = 0.5f;

        /// The pose of the card `offset` cards from the middle: its index minus the deck's Position, so a positive
        /// offset is an older card, on the right.
        public static CardPose Pose(float offset)
        {
            var side = Mathf.Sign(offset);
            var away = Mathf.Abs(offset);
            var fold = Mathf.Clamp01(away);
            return new CardPose(
                new Vector3(side * (FirstAside * fold + NextAside * Mathf.Max(0f, away - 1f)), ScrapbookPlacement.CardCentre.y, ScrapbookPlacement.CardAhead + Back * Mathf.Min(away, Reach)),
                side * SideYaw * fold,
                Mathf.Lerp(1f, SideScale, fold),
                1f - Mathf.Clamp01(away - SolidReach),
                1f - Mathf.Clamp01(away / TextReach));
        }

        /// The deck's frame for an eye at `eye`: at the eye, turned the way it faces, and level whatever its pitch
        /// and roll.
        public static Pose Frame(Pose eye)
        {
            var ahead = Vector3.ProjectOnPlane(eye.rotation * Vector3.forward, Vector3.up);
            // Looking straight up or down leaves no heading, and the top of the head still points along it.
            if (ahead.sqrMagnitude < 1e-4f) ahead = Vector3.ProjectOnPlane(eye.rotation * Vector3.up, Vector3.up);
            return new Pose(eye.position, Quaternion.LookRotation(ahead.normalized, Vector3.up));
        }
    }

    /// Whether a pinch is on a flat panel, judged along the line from the eye through the pinch point, so the hand
    /// does not have to reach the panel.
    public static class SightLine
    {
        /// True when the line from `eye` through `through` crosses the rectangle of `size` centred at `centre` and
        /// turned by `rotation`, or passes within `margin` metres of it.
        public static bool Hits(Vector3 eye, Vector3 through, Vector3 centre, Quaternion rotation, Vector2 size, float margin = 0f)
        {
            var toRect = Quaternion.Inverse(rotation);
            var origin = toRect * (eye - centre);
            var direction = toRect * (through - eye);
            if (Mathf.Abs(direction.z) < 1e-6f) return false;
            var along = -origin.z / direction.z;
            if (along <= 0f) return false;
            var at = origin + direction * along;
            return Mathf.Abs(at.x) <= size.x * 0.5f + margin && Mathf.Abs(at.y) <= size.y * 0.5f + margin;
        }
    }
}
