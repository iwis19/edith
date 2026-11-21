using System;
using System.Threading;
using System.Threading.Tasks;
using Edith.Backend;
using UnityEngine;
using UnityEngine.UI;

namespace Edith.Pairing
{
    /// Until a phone is paired, floats a 4-digit code and a short link in front of the person and keeps PairingGate
    /// closed. They open the link on the phone that runs the hotspot, type the code and tap Share location. The timers
    /// and states live in PairingFlow, which this drives and draws. The panel fades out once the backend confirms the
    /// phone, and comes back, closing the gate again, if the backend loses the phone's position.
    public sealed class PhonePairing : MonoBehaviour
    {
        [SerializeField] Transform eye;
        [SerializeField] Font font;
        [SerializeField] Material material;

        // Four canvas units per millimetre, which makes the panel 15 cm wide. Held 0.45 m out it looks as large as a
        // 30 cm panel at 0.9 m did, and sits well in front of real surfaces so they cannot hide it.
        const float UnitsPerMetre = 4000f;
        static readonly Vector2 Size = new Vector2(600f, 380f);
        const float Ahead = 0.45f, Below = 0.06f, FollowRate = 8f;
        const float FadeSeconds = 0.3f;
        const float CallSeconds = 8f;
        const int CornerRadius = 36;
        static readonly Color Backing = new Color(0.04f, 0.05f, 0.07f, 0.8f);
        static readonly Color White = new Color(0.97f, 0.97f, 0.99f);
        static readonly Color Muted = new Color(0.82f, 0.84f, 0.88f);

        PairingFlow flow;
        PairingClient client;
        Canvas canvas;
        CanvasGroup group;
        Text title, line, address, andType, code;
        Sprite rounded;
        PairPhase? shown;
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();

        /// The pairing state, or null when the build has no backend and the panel never started.
        public PairingFlow Flow => flow;

        void Start()
        {
            Build();
            var settings = BackendSettings.Load();
            if (settings == null)
            {
                // Nothing can pair, so the gate stays as the headset last left it.
                title.text = "No EDITH server";
                line.text = "This build has no backend address.";
                group.alpha = PairingGate.Open ? 0f : 1f;
                canvas.enabled = !PairingGate.Open;
                enabled = false;
                return;
            }
            client = new PairingClient(settings.Url, settings.DeviceToken, BackendSettings.ProfileId);
            flow = new PairingFlow(PairingGate.Current.Paired, Time.realtimeSinceStartup);
        }

        void Update()
        {
            if (flow == null) return;
            var call = flow.Next(Time.realtimeSinceStartup);
            if (call != PairCall.None) _ = Run(call);
            PairingGate.Current.Paired = !flow.Locked;
            Draw();
        }

        void LateUpdate()
        {
            if (group == null || group.alpha <= 0f) return;
            var follow = 1f - Mathf.Exp(-FollowRate * Time.deltaTime);
            var position = Vector3.Lerp(transform.position, Rest(), follow);
            transform.SetPositionAndRotation(position, Quaternion.Slerp(transform.rotation, Facing(position), follow));
        }

        async Task Run(PairCall call)
        {
            try
            {
                using var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                cancel.CancelAfter(TimeSpan.FromSeconds(CallSeconds));
                switch (call)
                {
                    case PairCall.CheckLocation:
                        var has = await client.HasLocationAsync(cancel.Token);
                        if (this == null) return;
                        Debug.Log($"[EDITH pair] phone position {(has ? "already kept" : "not kept yet")}");
                        flow.OnLocation(has, Time.realtimeSinceStartup);
                        break;
                    case PairCall.RequestCode:
                        var issued = await client.RequestCodeAsync(cancel.Token);
                        if (this == null) return;
                        Debug.Log($"[EDITH pair] code issued, good for {issued.expires_in_s} s");
                        flow.OnCode(issued.code, issued.phone_url, Time.realtimeSinceStartup);
                        break;
                    case PairCall.PollStatus:
                        var status = await client.StatusAsync(flow.Code, cancel.Token);
                        if (this == null) return;
                        if (status.paired || status.expired) Debug.Log($"[EDITH pair] code {(status.paired ? "claimed" : "expired")}");
                        flow.OnStatus(status.paired, status.expired, Time.realtimeSinceStartup);
                        break;
                }
            }
            catch (Exception e)
            {
                if (this == null) return;
                var why = e is OperationCanceledException ? "timeout" : e.GetBaseException().Message;
                Debug.Log($"[EDITH pair] {call} failed: {why}");
                flow.OnError(Time.realtimeSinceStartup);
            }
        }

        void Draw()
        {
            if (flow.Phase != shown)
            {
                shown = flow.Phase;
                Debug.Log($"[EDITH pair] {shown}, gate {(flow.Locked ? "closed" : "open")}");
            }
            if (flow.Phase == PairPhase.Requesting)
            {
                title.text = "Pair your phone";
                line.text = flow.Failures > 0 ? "Can't reach the EDITH server. Trying again." : "Getting a code...";
                address.text = andType.text = code.text = "";
            }
            else if (flow.Phase == PairPhase.Showing)
            {
                title.text = "Share your location";
                line.text = "On the phone running your hotspot, open";
                address.text = PairingText.TypedAddress(flow.PhoneUrl);
                andType.text = "and type";
                code.text = PairingText.Spaced(flow.Code);
            }
            else if (flow.Phase == PairPhase.Connected)
            {
                title.text = "Phone connected";
                line.text = "EDITH knows where you are.";
                address.text = andType.text = code.text = "";
            }
            var target = flow.Visible ? 1f : 0f;
            group.alpha = Mathf.MoveTowards(group.alpha, target, Time.deltaTime / FadeSeconds);
            canvas.enabled = group.alpha > 0f;
        }

        void Build()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            group = gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            canvas.enabled = false;
            group.interactable = false;
            group.blocksRaycasts = false;
            var root = (RectTransform)transform;
            root.sizeDelta = Size;
            root.localScale = Vector3.one / UnitsPerMetre;

            rounded = RoundedRect();
            var backing = new GameObject("Backing", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            backing.sprite = rounded;
            backing.type = Image.Type.Sliced;
            backing.color = Backing;
            backing.raycastTarget = false;
            backing.material = material;
            Place(backing.rectTransform, root, Size, Vector2.zero);

            var width = Size.x - 40f;
            title = Label("Title", root, 40, FontStyle.Bold, White, new Vector2(width, 56f), new Vector2(0f, 142f));
            line = Label("Line", root, 26, FontStyle.Normal, Muted, new Vector2(width, 40f), new Vector2(0f, 88f));
            address = Label("Address", root, 34, FontStyle.Bold, White, new Vector2(width, 48f), new Vector2(0f, 40f));
            andType = Label("AndType", root, 26, FontStyle.Normal, Muted, new Vector2(width, 40f), new Vector2(0f, -4f));
            code = Label("Code", root, 120, FontStyle.Bold, White, new Vector2(width, 150f), new Vector2(0f, -100f));

            var rest = Rest();
            transform.SetPositionAndRotation(rest, Facing(rest));
        }

        Text Label(string name, RectTransform parent, int size, FontStyle style, Color color, Vector2 box, Vector2 position)
        {
            var text = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.material = material;
            Place(text.rectTransform, parent, box, position);
            return text;
        }

        static void Place(RectTransform rect, RectTransform parent, Vector2 size, Vector2 position)
        {
            rect.SetParent(parent, false);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        /// A white rounded square with a soft edge, cut into nine slices so any size keeps its corners.
        static Sprite RoundedRect()
        {
            const int size = CornerRadius * 2 + 4;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];
            var centre = (size - 1) * 0.5f;
            var straight = centre - CornerRadius;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = Mathf.Max(Mathf.Abs(x - centre) - straight, 0f);
                var dy = Mathf.Max(Mathf.Abs(y - centre) - straight, 0f);
                var alpha = Mathf.Clamp01(CornerRadius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            // 100 pixels per unit matches the canvas's reference, so the sliced border is drawn at its pixel size.
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(CornerRadius, CornerRadius, CornerRadius, CornerRadius));
        }

        /// Below the line of sight rather than straight down, so looking up or down carries the panel with it.
        Vector3 Rest() => eye.position + eye.forward * Ahead - eye.up * Below;

        /// Faces the eye and stays upright.
        Quaternion Facing(Vector3 position) => Quaternion.LookRotation(position - eye.position, Vector3.up);

        void OnDestroy()
        {
            lifetime.Cancel();
            lifetime.Dispose();
            if (rounded == null) return;
            Destroy(rounded.texture);
            Destroy(rounded);
        }
    }
}
