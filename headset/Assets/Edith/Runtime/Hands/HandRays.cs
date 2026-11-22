using System;
using Edith.UI;
using UnityEngine;

namespace Edith.Hands
{
    /// Both hands' rays for a page that is selected by pointing, drawn as Horizon OS draws its own: a thin line that
    /// fades in from the hand, and a ring cursor wherever it meets the page, which shrinks and fills as the fingers
    /// pinch. A ray that misses the page fades out at its tip. A page steps it every frame it is open and hides it when it closes.
    public sealed class HandRays : IDisposable
    {
        // The line starts this far ahead of the hand, so it never shows through the fingers.
        const float StartAhead = 0.04f;
        const float StartWidth = 0.006f, EndWidth = 0.002f;
        // The cursor fills about 1.2 degrees, whatever its distance, and lies this far in front of what it is on.
        const float CursorPerMetre = 0.021f, CursorLift = 0.003f;
        const float PinchedScale = 0.65f;
        const int Order = 100;

        static readonly Color Ice = Theme.Ice, Ink = Theme.OnAccent;

        readonly RaySelect select = new RaySelect();
        readonly GameObject root;
        readonly Material material;
        readonly Texture2D ringTexture, dotTexture;
        readonly Hand[] hands = new Hand[2];

        sealed class Hand
        {
            public LineRenderer Line;
            public Transform Cursor;
            public SpriteRenderer Ring, Dot;
        }

        public HandRays(string name)
        {
            root = new GameObject(name + " rays");
            material = new Material(Shader.Find("Sprites/Default"));
            ringTexture = Bake(Ring);
            dotTexture = Bake(Dot);
            var ring = Sprite.Create(ringTexture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), Size);
            var dot = Sprite.Create(dotTexture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), Size);
            for (var i = 0; i < 2; i++) hands[i] = NewHand((HandSide)i, ring, dot);
            root.SetActive(false);
        }

        /// Reads both hands, draws their rays and says what they did. `targets` finds what a ray points at.
        public RayReading Step(in HandFrame right, in HandFrame left, RayTargets targets)
        {
            if (!root.activeSelf)
            {
                select.Reset();
                root.SetActive(true);
            }
            var reading = select.Step(right, left, targets);
            for (var i = 0; i < 2; i++) Draw(hands[i], select.Aim((HandSide)i));
            return reading;
        }

        public void Hide()
        {
            if (root.activeSelf) root.SetActive(false);
        }

        public void Dispose()
        {
            UnityEngine.Object.Destroy(root);
            UnityEngine.Object.Destroy(material);
            UnityEngine.Object.Destroy(ringTexture);
            UnityEngine.Object.Destroy(dotTexture);
        }

        void Draw(Hand hand, in RayAim aim)
        {
            var reaches = aim.Shown && aim.Distance > StartAhead;
            hand.Line.enabled = reaches;
            hand.Cursor.gameObject.SetActive(reaches && aim.OnPage);
            if (!reaches) return;

            var ray = aim.Ray;
            hand.Line.SetPosition(0, ray.GetPoint(StartAhead));
            hand.Line.SetPosition(1, ray.GetPoint(aim.Distance));
            var bright = aim.Held ? 0.95f : 0.75f;
            hand.Line.colorGradient = Fade(bright * 0.8f, aim.OnPage ? bright : 0f);
            if (!aim.OnPage) return;

            var pinch = aim.Held ? 1f : Mathf.Clamp01(aim.Pinch);
            hand.Cursor.SetPositionAndRotation(ray.GetPoint(aim.Distance - CursorLift), Quaternion.LookRotation(ray.direction));
            hand.Cursor.localScale = Vector3.one * (aim.Distance * CursorPerMetre * Mathf.Lerp(1f, PinchedScale, pinch));
            hand.Dot.color = new Color(Ice.r, Ice.g, Ice.b, pinch);
        }

        static Gradient Fade(float middle, float end)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Ice, 0f), new GradientColorKey(Ice, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(middle, 0.35f), new GradientAlphaKey(end, 1f) });
            return gradient;
        }

        Hand NewHand(HandSide side, Sprite ring, Sprite dot)
        {
            var line = new GameObject(side + " ray", typeof(LineRenderer)).GetComponent<LineRenderer>();
            line.transform.SetParent(root.transform, false);
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = StartWidth;
            line.endWidth = EndWidth;
            line.numCapVertices = 4;
            line.sharedMaterial = material;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sortingOrder = Order;

            var cursor = new GameObject(side + " cursor").transform;
            cursor.SetParent(root.transform, false);
            return new Hand { Line = line, Cursor = cursor, Ring = Layer(cursor, "Ring", ring, Order + 1), Dot = Layer(cursor, "Dot", dot, Order + 2) };
        }

        SpriteRenderer Layer(Transform cursor, string name, Sprite sprite, int order)
        {
            var layer = new GameObject(name, typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
            layer.transform.SetParent(cursor, false);
            layer.sprite = sprite;
            layer.sharedMaterial = material;
            layer.sortingOrder = order;
            return layer;
        }

        // ---- the cursor's two layers, baked once per page

        const int Size = 64;

        /// An ice ring with a thin ink edge on both sides, so it reads over bright sky and dark glass alike.
        static Color Ring(float r)
        {
            var ring = Band(r, 0.33f, 0.46f);
            var edge = Mathf.Max(Band(r, 0.29f, 0.33f), Band(r, 0.46f, 0.5f));
            return Color.Lerp(new Color(Ink.r, Ink.g, Ink.b, edge * 0.6f), new Color(Ice.r, Ice.g, Ice.b, 1f), ring);
        }

        /// The middle that fills as the fingers close; the renderer's colour sets how much.
        static Color Dot(float r) => new Color(1f, 1f, 1f, Band(r, -1f, 0.27f));

        /// 1 between `inner` and `outer` (radii out of 0.5), falling to 0 over about a pixel at each edge.
        static float Band(float r, float inner, float outer)
        {
            var pixel = 1f / Size;
            return Mathf.Clamp01((r - inner) / pixel + 0.5f) * Mathf.Clamp01((outer - r) / pixel + 0.5f);
        }

        static Texture2D Bake(Func<float, Color> shade)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color[Size * Size];
            for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                var r = new Vector2((x + 0.5f) / Size - 0.5f, (y + 0.5f) / Size - 0.5f).magnitude;
                pixels[y * Size + x] = shade(r);
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
