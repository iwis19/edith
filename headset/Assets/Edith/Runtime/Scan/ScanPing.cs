using UnityEngine;
using UnityEngine.UI;

namespace Edith.Scan
{
    /// The scan's wave: flat rings of light that spread out level with the person's waist, through the air, like a
    /// ripple on a pond at waist height. Fire one with Burst. A ring reaches a thing `HorizontalDistance / Speed`
    /// seconds after it leaves, which is when a pin on that thing should appear.
    public sealed class ScanPing : MonoBehaviour
    {
        /// How fast a ring grows, in metres per second, and how far it goes before it has faded out.
        public const float Speed = 2.5f, MaxRadius = 4.5f;
        // The wave lies this far below the eyes, about waist height.
        const float BelowEyesMetres = 0.6f;
        // A burst is three rings this far apart, each dimmer than the last.
        const int Rings = 3;
        const float RingGap = 0.18f, RingDimming = 0.55f, StartRadius = 0.25f;

        struct Ring
        {
            public Image Image;
            public float Born, Brightness;
        }

        readonly Ring[] rings = new Ring[Rings];
        GameObject plane;
        Sprite sprite;
        float busyUntil = -1f;

        /// Builds the wave's flat canvas, hidden until the first burst. `material` lets real objects hide it.
        public static ScanPing Create(GameObject host, Material material)
        {
            var ping = host.AddComponent<ScanPing>();
            ping.sprite = Band(512);
            ping.plane = new GameObject("ScanWave", typeof(Canvas));
            ping.plane.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var root = (RectTransform)ping.plane.transform;
            root.sizeDelta = Vector2.zero;
            // A canvas unit is a millimetre; the canvas lies flat, facing up.
            root.localScale = Vector3.one * 0.001f;
            root.rotation = Quaternion.Euler(90f, 0f, 0f);
            for (var i = 0; i < Rings; i++)
            {
                var image = new GameObject("Ring", typeof(Image)).GetComponent<Image>();
                image.sprite = ping.sprite;
                image.raycastTarget = false;
                image.material = material;
                image.rectTransform.SetParent(root, false);
                image.enabled = false;
                ping.rings[i] = new Ring { Image = image, Born = -100f };
            }
            ping.plane.SetActive(false);
            return ping;
        }

        /// How long a ring takes to reach `point` from `eye`, across the floor plan.
        public static float Reach(Vector3 eye, Vector3 point)
        {
            var across = point - eye;
            across.y = 0f;
            return Mathf.Max(0f, across.magnitude - StartRadius) / Speed;
        }

        /// Sends a burst of rings out at waist height under `eye`.
        public void Burst(Vector3 eye)
        {
            if (plane == null) return;
            plane.transform.position = eye + Vector3.down * BelowEyesMetres;
            for (var i = 0; i < Rings; i++)
            {
                rings[i].Born = Time.time + i * RingGap;
                rings[i].Brightness = Mathf.Pow(RingDimming, i);
            }
            busyUntil = Time.time + (Rings - 1) * RingGap + (MaxRadius - StartRadius) / Speed;
            plane.SetActive(true);
            Debug.Log($"[EDITH scan] wave from {plane.transform.position}");
        }

        void Update()
        {
            if (plane == null || !plane.activeSelf) return;
            if (Time.time > busyUntil)
            {
                plane.SetActive(false);
                return;
            }
            for (var i = 0; i < Rings; i++)
            {
                var age = Time.time - rings[i].Born;
                var radius = StartRadius + age * Speed;
                var image = rings[i].Image;
                image.enabled = age >= 0f && radius < MaxRadius;
                if (!image.enabled) continue;
                image.rectTransform.sizeDelta = Vector2.one * (radius * 2000f);
                var fade = 1f - radius / MaxRadius;
                image.color = new Color(1f, 1f, 1f, rings[i].Brightness * fade * fade);
            }
        }

        void OnDestroy()
        {
            if (plane != null) Destroy(plane);
            if (sprite != null)
            {
                Destroy(sprite.texture);
                Destroy(sprite);
            }
        }

        /// A ring of light: a crisp bright edge at the rim and a glow trailing inward behind it, clear in the middle.
        static Sprite Band(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            var centre = (size - 1) * 0.5f;
            var half = size * 0.5f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                float dx = x - centre, dy = y - centre;
                var r = Mathf.Sqrt(dx * dx + dy * dy) / half;
                var edge = Mathf.Clamp01((1f - r) * half * 0.5f);
                var trail = Mathf.Pow(Mathf.Clamp01(1f - (1f - r) / 0.12f), 2f);
                var line = Mathf.Clamp01(1f - Mathf.Abs(r - 0.985f) / 0.012f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(edge * (0.45f * trail + line)));
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }
    }
}
