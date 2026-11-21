using System;
using System.Threading;
using System.Threading.Tasks;
using Edith.Backend;
using Edith.UI;
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
        static readonly Vector2 Size = new Vector2(600f, 400f);
        const float Ahead = 0.45f, Below = 0.06f, FollowRate = 8f;
        const float FadeSeconds = 0.3f;
        const float CallSeconds = 8f;
        const float PanelRadius = 40f;
        static readonly Vector2 DigitSize = new Vector2(92f, 112f);
        const float DigitGap = 18f;

        PairingFlow flow;
        PairingClient client;
        Canvas canvas;
        CanvasGroup group;
        Text title, line, address, andType;
        readonly Text[] digits = new Text[4];
        readonly Glass[] boxes = new Glass[4];
        PairPhase? shown;
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();

        /// The pairing state, or null when the build has no backend and the panel never started.
        public PairingFlow Flow => flow;

        /// While true the panel stays hidden because the welcome is showing the same pairing as its second step. The
        /// gate and the flow run on regardless.
        public bool Suppressed { get; set; }

        /// The place the backend resolved from the phone's position, or null when it has none yet or cannot be reached.
        public async Task<string> PlaceAsync()
        {
            if (client == null) return null;
            try
            {
                using var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                cancel.CancelAfter(TimeSpan.FromSeconds(CallSeconds));
                return await client.PlaceAsync(cancel.Token);
            }
            catch (Exception e)
            {
                Debug.Log($"[EDITH pair] place lookup failed: {e.GetBaseException().Message}");
                return null;
            }
        }

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
                        flow.OnCode(issued.code, issued.phone_url, Time.realtimeSinceStartup, issued.expires_in_s > 0 ? issued.expires_in_s : 600f);
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
                address.text = andType.text = "";
                ShowCode(null);
            }
            else if (flow.Phase == PairPhase.Showing)
            {
                title.text = "Share your location";
                line.text = "On the phone running your hotspot, open";
                address.text = PairingText.TypedAddress(flow.PhoneUrl);
                andType.text = "and type";
                ShowCode(flow.Code);
            }
            var target = flow.Visible && !Suppressed ? 1f : 0f;
            group.alpha = Mathf.MoveTowards(group.alpha, target, Time.deltaTime / FadeSeconds);
            canvas.enabled = group.alpha > 0f;
        }

        void Build()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            Theme.Sharpen(gameObject);
            group = gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            canvas.enabled = false;
            group.interactable = false;
            group.blocksRaycasts = false;
            var root = (RectTransform)transform;
            root.sizeDelta = Size;
            root.localScale = Vector3.one / UnitsPerMetre;

            var glass = Glass.Build(root, "Backing", Size, PanelRadius, material);
            var semibold = Theme.SemiBold(font);
            var regular = Theme.Regular(font);
            var width = Size.x - 40f;
            title = Label("Title", root, semibold, 42, Theme.Ice, new Vector2(width, 56f), new Vector2(0f, 150f));
            line = Label("Line", root, regular, 27, Theme.Muted, new Vector2(width, 40f), new Vector2(0f, 98f));
            address = Label("Address", root, semibold, 36, Theme.Accent, new Vector2(width, 48f), new Vector2(0f, 50f));
            andType = Label("AndType", root, regular, 27, Theme.Muted, new Vector2(width, 40f), new Vector2(0f, 8f));
            for (var i = 0; i < 4; i++)
            {
                var x = (i - 1.5f) * (DigitSize.x + DigitGap);
                boxes[i] = Glass.Build(root, "Digit", DigitSize, 22f, material);
                boxes[i].Rect.anchoredPosition = new Vector2(x, -85f);
                digits[i] = Label("DigitText", boxes[i].Rect, semibold, 76, Theme.Ice, DigitSize, Vector2.zero);
            }
            ShowCode(null);

            var rest = Rest();
            transform.SetPositionAndRotation(rest, Facing(rest));
        }

        Text Label(string name, RectTransform parent, Font face, int size, Color color, Vector2 box, Vector2 position)
        {
            var text = Theme.Label(parent, name, face, size, color, TextAnchor.MiddleCenter, material);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            Place(text.rectTransform, parent, box, position);
            return text;
        }

        /// The four digit boxes: the code's digits when there is one, empty and dim otherwise.
        void ShowCode(string code)
        {
            for (var i = 0; i < 4; i++)
            {
                var digit = code != null && i < code.Length;
                digits[i].text = digit ? code[i].ToString() : "";
                boxes[i].SetLook(digit ? Theme.ChipFill : Theme.Tint(0.2f), new Color(1f, 1f, 1f, 0.1f),
                    digit ? Theme.Accent : new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.4f), Color.clear);
            }
        }

        static void Place(RectTransform rect, RectTransform parent, Vector2 size, Vector2 position)
        {
            rect.SetParent(parent, false);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        /// Below the line of sight rather than straight down, so looking up or down carries the panel with it.
        Vector3 Rest() => eye.position + eye.forward * Ahead - eye.up * Below;

        /// Faces the eye and stays upright.
        Quaternion Facing(Vector3 position) => Quaternion.LookRotation(position - eye.position, Vector3.up);

        void OnDestroy()
        {
            lifetime.Cancel();
            lifetime.Dispose();
        }
    }
}
