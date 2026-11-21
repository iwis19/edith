using Edith.Capture;
using Edith.Grab;
using Edith.Hands;
using Edith.Look;
using Edith.Pairing;
using Edith.Pinch;
using Edith.Scan;
using UnityEngine;
#if EDITH_DIAGNOSTICS
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Edith.Backend;
using UnityEngine.UI;
#endif

namespace Edith.Diagnostics
{
    /// A small world-space panel of live status for the diagnostics build: pinch and grab state, counts, camera,
    /// pairing, the last Scan, the backend's /health and the last error in the log. A left fist held for a second
    /// shows or hides it. Release builds carry none of its code, and the component destroys itself there.
    public sealed class StatusPanel : MonoBehaviour
    {
        [SerializeField] PinchInput pinch;
        [SerializeField] GrabInput grab;
        [SerializeField] LookController look;
        [SerializeField] HeadsetCamera headsetCamera;
        [SerializeField] PhonePairing pairing;
        [SerializeField] ScanController scan;
        [SerializeField] LeftHandSource leftHand;
        [SerializeField] Transform eye;
        [SerializeField] Font font;
        [SerializeField] Material material;

#if EDITH_DIAGNOSTICS
        // Two canvas units per millimetre, which makes the panel 31 cm wide.
        const float UnitsPerMetre = 2000f;
        static readonly Vector2 Size = new Vector2(620f, 330f);
        const float Ahead = 0.8f, Below = 0.18f, Aside = 0.3f, FollowRate = 6f;
        const float FadeSeconds = 0.2f, RefreshSeconds = 0.25f;
        const float HealthSeconds = 45f, HealthTimeoutSeconds = 5f;
        static readonly Color Backing = new Color(0.04f, 0.05f, 0.07f, 0.8f);
        static readonly Color White = new Color(0.97f, 0.97f, 0.99f);
        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        readonly StatusTally tally = new StatusTally();
        readonly object errorGate = new object();
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        PanelToggle toggle;
        CanvasGroup group;
        Text text;
        BackendSettings settings;
        string backendHost;
        BackendHealth health = new BackendHealth(BackendReach.Unknown, "", 0f);
        string lastError;
        bool shown = true;
        float nextRefresh;
        float nextHealth;

        void Awake() => toggle = new PanelToggle(new GrabTuning());

        void OnEnable()
        {
            pinch.Stepped += OnPinch;
            grab.Stepped += OnGrab;
            Application.logMessageReceivedThreaded += OnLog;
        }

        void OnDisable()
        {
            pinch.Stepped -= OnPinch;
            grab.Stepped -= OnGrab;
            Application.logMessageReceivedThreaded -= OnLog;
        }

        void OnDestroy()
        {
            lifetime.Cancel();
            lifetime.Dispose();
        }

        void Start()
        {
            settings = BackendSettings.Load();
            if (settings != null && Uri.TryCreate(settings.Url, UriKind.Absolute, out var uri)) backendHost = uri.Authority;
            Build();
        }

        void OnPinch(PinchReading reading) => tally.OnPinch(reading, Time.realtimeSinceStartup);

        void OnGrab(GrabReading reading) => tally.OnGrab(reading);

        // Logs arrive from any thread, and an exception inside an async call arrives from a worker.
        void OnLog(string message, string stackTrace, LogType type)
        {
            if (!LogLine.IsFailure(type)) return;
            lock (errorGate) lastError = message;
        }

        void Update()
        {
            var now = Time.realtimeSinceStartup;
            if (toggle.Step(leftHand.Read()))
            {
                shown = !shown;
                Debug.Log($"[EDITH diagnostics] panel {(shown ? "shown" : "hidden")}");
            }
            group.alpha = Mathf.MoveTowards(group.alpha, shown && PairingGate.Open ? 1f : 0f, Time.deltaTime / FadeSeconds);

            if (settings != null && now >= nextHealth)
            {
                nextHealth = now + HealthSeconds;
                _ = CheckHealth();
            }
            if (now < nextRefresh) return;
            nextRefresh = now + RefreshSeconds;
            text.text = StatusText.Format(Gather(now));
        }

        void LateUpdate()
        {
            var follow = 1f - Mathf.Exp(-FollowRate * Time.deltaTime);
            var position = Vector3.Lerp(transform.position, Rest(), follow);
            transform.SetPositionAndRotation(position, Quaternion.Slerp(transform.rotation, Facing(position), follow));
        }

        StatusView Gather(float now)
        {
            var flow = pairing != null ? pairing.Flow : null;
            string error;
            lock (errorGate) error = lastError;
            return new StatusView
            {
                Tally = tally,
                Snapshots = look.Room.Snapshots.Count,
                Camera = headsetCamera.Status,
                HasPairing = flow != null,
                Pairing = flow?.Phase ?? default,
                ScanResult = scan.LastResult,
                BackendHost = backendHost,
                Backend = health,
                LastError = error,
                Now = now,
            };
        }

        async Task CheckHealth()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                cancel.CancelAfter(TimeSpan.FromSeconds(HealthTimeoutSeconds));
                using var response = await Http.GetAsync(settings.Url + "/health", cancel.Token);
                if (this == null) return;
                health = response.IsSuccessStatusCode
                    ? new BackendHealth(BackendReach.Up, $"{watch.ElapsedMilliseconds} ms", Time.realtimeSinceStartup)
                    : new BackendHealth(BackendReach.Down, $"HTTP {(int)response.StatusCode}", Time.realtimeSinceStartup);
            }
            catch (Exception e)
            {
                if (this == null) return;
                var why = e is OperationCanceledException ? "timeout" : e.GetBaseException().Message;
                health = new BackendHealth(BackendReach.Down, why, Time.realtimeSinceStartup);
            }
        }

        void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            group = gameObject.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            var root = (RectTransform)transform;
            root.sizeDelta = Size;
            root.localScale = Vector3.one / UnitsPerMetre;

            var backing = new GameObject("Backing", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            backing.color = Backing;
            backing.raycastTarget = false;
            backing.material = material;
            backing.rectTransform.SetParent(root, false);
            backing.rectTransform.sizeDelta = Size;

            text = new GameObject("Text", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.font = font;
            text.fontSize = 22;
            text.color = White;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.material = material;
            text.rectTransform.SetParent(root, false);
            text.rectTransform.sizeDelta = Size - new Vector2(40f, 30f);

            var rest = Rest();
            transform.SetPositionAndRotation(rest, Facing(rest));
        }

        Vector3 Rest() => eye.position + eye.forward * Ahead - eye.up * Below - eye.right * Aside;

        Quaternion Facing(Vector3 position) => Quaternion.LookRotation(position - eye.position, Vector3.up);
#else
        void Awake() => Destroy(gameObject);
#endif
    }
}
