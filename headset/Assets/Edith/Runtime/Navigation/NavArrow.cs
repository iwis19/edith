using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Edith.Backend;
using Edith.Hands;
using Edith.Pairing;
using Edith.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Edith.Navigation
{
    /// The chevron that points the way to the place EDITH found. It floats low in front of the person, facing them,
    /// and turns in the plane of the view like a compass needle: up for straight ahead, sideways for a turn, down for
    /// behind. It points at the next point of the walking route the backend follows, or straight along the compass
    /// bearing to the target when it has no route, both measured from the phone's newest position.
    /// The headset has no compass, so north comes from NorthCalibration, fed by the phone's compass and GPS course
    /// paired with the head's yaw at the moment each reading was taken, and only by readings taken while the head
    /// held still. North found by the compass alone is found afresh when directions start, since the phone may have
    /// been lying anywhere. Until north is known, and while the phone is silent, only the card shows. On arrival it
    /// says so, then stops the directions. Nothing shows while Welcome or the Scrapbook holds the hands, and EDITH's
    /// answer orb keeps clear of what does.
    public sealed class NavArrow : MonoBehaviour, IShownPanel
    {
        [SerializeField] Transform eye;
        [SerializeField] Font font;
        [SerializeField] Material material;

        const float UnitsPerMetre = 1000f;
        const float AheadMetres = 0.55f, BelowMetres = 0.19f, LabelBelowMetres = 0.055f;
        /// The arrow and label are laid out in metres as seen from 0.85 m and shrunk by this much, so held nearer
        /// they look the same size.
        const float Shrink = AheadMetres / 0.85f;
        const float ChevronWidthMetres = 0.1f, ChevronHeightMetres = 0.05f;
        const float FollowRate = 4f, TurnRate = 6f, FadeSeconds = 0.35f;
        const float ActivePollSeconds = 0.7f, IdlePollSeconds = 4f;
        /// A compass reading counts when the head kept within CompassSteadyDegrees from this long before it to this
        /// long after, which also covers the reading being a little older than it is taken to be.
        const float CompassBeforeSeconds = 1.5f, CompassAfterSeconds = 0.5f, CompassSteadyDegrees = 10f;
        /// A course is the walk over the last couple of seconds, so the head must have faced one way through them.
        const float CourseBeforeSeconds = 2.5f, CourseSteadyDegrees = 25f;
        const float StalePhoneSeconds = 20f;
        const float ArrivedLingerSeconds = 5f;
        const float YawRecordSeconds = 0.05f;
        const float NoticeSeconds = 2.5f;

        static volatile bool poked;

        readonly YawHistory yaws = new YawHistory();
        readonly NorthCalibration north = new NorthCalibration();
        readonly CancellationTokenSource cancel = new CancellationTokenSource();
        readonly object gate = new object();
        Transform arrowRoot, labelRoot;
        CanvasGroup arrowGroup, labelGroup;
        NavCard card;
        HandGate hands;
        Texture2D arrowTexture;
        Vector3 anchor;
        bool placed;
        float shownYaw;
        float nextPoll, nextYawRecord, lastSampledAt = float.NegativeInfinity, arrivedAt = -1f;
        volatile bool polling;
        bool stopSent;
        NavStatus status, arrivedStatus;
        // Set by the poll on a worker thread, taken by Update. A failed poll leaves `received` null.
        NavStatus received;
        float receivedSentAt;
        bool hasReceived;
        // A short line shown in place of everything else, such as after the stop gesture, until noticeUntil.
        string notice;
        float noticeUntil = -1f;
        // Polls sent before a cancel may still report the old target, so their answers are dropped.
        float ignoreBefore = -1f;

        /// Asks for a poll on the next frame, as when Ask has just set or stopped a target.
        public static void Poke() => poked = true;

        /// Stops the directions under way at once, as the finger guns pointing up do, and says so. With none under
        /// way it says that instead.
        public void Cancel()
        {
            var now = Time.realtimeSinceStartup;
            notice = status is { active: true } ? "Directions stopped" : "No directions to stop";
            noticeUntil = now + NoticeSeconds;
            ignoreBefore = now;
            status = null;
            arrivedAt = -1f;
            stopSent = false;
            Stop();
        }

        void Awake()
        {
            arrowTexture = ChevronTexture();
            arrowRoot = MakeCanvas("NavArrow", ChevronWidthMetres, ChevronHeightMetres, out arrowGroup);
            var chevron = new GameObject("Chevron", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            chevron.texture = arrowTexture;
            chevron.material = material;
            chevron.color = Edith.UI.Theme.Accent;
            chevron.raycastTarget = false;
            chevron.rectTransform.SetParent(arrowRoot, false);
            chevron.rectTransform.anchorMin = Vector2.zero;
            chevron.rectTransform.anchorMax = Vector2.one;
            chevron.rectTransform.sizeDelta = Vector2.zero;

            labelRoot = MakeCanvas("NavLabel", 0.34f, 0.15f, out labelGroup);
            Edith.UI.Theme.Sharpen(labelRoot.gameObject);
            card = new NavCard((RectTransform)labelRoot, font, material);
            arrowGroup.alpha = labelGroup.alpha = 0f;
        }

        void Start() => hands = FindAnyObjectByType<HandGate>();

        void OnEnable()
        {
            ShownPanels.Add(this);
            if (OVRManager.display != null) OVRManager.display.RecenteredPose += OnRecentered;
        }

        void OnDisable()
        {
            ShownPanels.Remove(this);
            if (OVRManager.display != null) OVRManager.display.RecenteredPose -= OnRecentered;
        }

        void OnDestroy()
        {
            cancel.Cancel();
            if (arrowTexture != null) Destroy(arrowTexture);
            if (arrowRoot != null) Destroy(arrowRoot.gameObject);
            if (labelRoot != null) Destroy(labelRoot.gameObject);
        }

        /// Recentring turns the whole tracking space, so the north found so far no longer holds.
        void OnRecentered()
        {
            north.Reset();
            Debug.Log("[EDITH nav] recentred, finding north again");
        }

        void Update()
        {
            var now = Time.realtimeSinceStartup;
            if (eye == null) return;
            if (now >= nextYawRecord)
            {
                yaws.Record(now, eye.eulerAngles.y);
                nextYawRecord = now + YawRecordSeconds;
            }
            TakeReceived();
            if (PairingGate.Open && !polling && (poked || now >= nextPoll)) StartPoll(now);
            Show(now);
        }

        void StartPoll(float now)
        {
            var settings = BackendSettings.Load();
            if (settings == null) return;
            poked = false;
            polling = true;
            nextPoll = now + (status is { active: true } ? ActivePollSeconds : IdlePollSeconds);
            var url = settings.Url;
            var token = settings.DeviceToken;
            var profile = BackendSettings.ProfileId;
            var sentAt = now;
            Task.Run(async () =>
            {
                NavStatus found = null;
                try
                {
                    found = await NavClient.StatusAsync(url, token, profile, cancel.Token).ConfigureAwait(false);
                }
                catch (Exception e) when (!cancel.IsCancellationRequested)
                {
                    Debug.Log($"[EDITH nav] poll failed: {e.GetBaseException().Message}");
                }
                catch (Exception)
                {
                }
                lock (gate)
                {
                    received = found;
                    receivedSentAt = sentAt;
                    hasReceived = true;
                }
                polling = false;
            });
        }

        /// Hands the newest poll result to the main thread and learns north from the phone reading it carries.
        void TakeReceived()
        {
            NavStatus next;
            float sentAt;
            lock (gate)
            {
                if (!hasReceived) return;
                hasReceived = false;
                next = received;
                sentAt = receivedSentAt;
                received = null;
            }
            if (next == null || sentAt < ignoreBefore) return;
            var now = Time.realtimeSinceStartup;
            if (next.active && status is not { active: true })
            {
                Debug.Log($"[EDITH nav] guiding to {next.name}");
                // A compass-only north may date from a phone lying anywhere; one found by walking stands.
                if (north.Ready && !north.Walked)
                {
                    north.Reset();
                    Debug.Log("[EDITH nav] finding north again for the new directions");
                }
            }
            if (!next.active && status is { active: true }) Debug.Log("[EDITH nav] directions ended");
            status = next;
            // An idle reply still carries the newest phone reading, so north is learned before directions start.
            if (next.fix_age_s < 0f) return;

            // The backend received the fix fix_age_s before it answered, and it answered about half a round trip ago.
            var fixAt = sentAt + (now - sentAt) * 0.5f - next.fix_age_s;
            if (Mathf.Abs(fixAt - lastSampledAt) < 0.5f || !yaws.TryAt(fixAt, 0.3f, out var headYaw)) return;
            lastSampledAt = fixAt;
            var wasReady = north.Ready;
            var was = north.NorthYaw;
            if (next.course_deg >= 0f && next.speed_mps >= NavMath.CourseMinSpeed
                && yaws.Steady(fixAt - CourseBeforeSeconds, fixAt, CourseSteadyDegrees))
                north.AddCourse(NavMath.NorthFrom(headYaw, next.course_deg));
            if (next.heading_deg >= 0f && NavMath.CompassUsable(next.heading_accuracy_deg)
                && yaws.Steady(fixAt - CompassBeforeSeconds, fixAt + CompassAfterSeconds, CompassSteadyDegrees))
                north.AddCompass(NavMath.NorthFrom(headYaw, next.heading_deg));
            if (north.Ready && (!wasReady || Mathf.Abs(NavMath.Wrap(north.NorthYaw - was)) >= 1f))
                Debug.Log($"[EDITH nav] north {(wasReady ? "moved" : "locked")} at {north.NorthYaw:F0} degrees{(north.Walked ? ", from walking" : ", from the compass")}");
        }

        void Show(float now)
        {
            var dt = Time.deltaTime;
            var s = status;
            var wantArrow = false;
            var wantLabel = false;
            if (!PairingGate.Open || hands != null && hands.Held)
            {
            }
            else if (now < noticeUntil)
            {
                card.Show(notice, null, null);
                wantLabel = true;
            }
            else if (s == null)
            {
            }
            else if (s.active && s.arrived)
            {
                if (arrivedAt < 0f)
                {
                    arrivedAt = now;
                    arrivedStatus = s;
                    Debug.Log($"[EDITH nav] arrived at {s.name}");
                }
                card.Show(s.name, s.address, "You're here");
                wantLabel = true;
                if (now - arrivedAt >= ArrivedLingerSeconds) Stop();
            }
            else if (s.active)
            {
                arrivedAt = -1f;
                wantLabel = true;
                if (!s.HasFix || s.fix_age_s > StalePhoneSeconds)
                    card.Show(s.name, s.address, "Waiting for your phone");
                else if (!north.Ready)
                    card.Show(s.name, s.address, "Hold your phone flat, facing ahead");
                else
                {
                    card.Show(s.name, s.address, NavMath.GuideText(s.HasRoute ? s.maneuver : "", s.maneuver_distance_m, s.WalkMetres));
                    wantArrow = true;
                }
            }
            else if (arrivedAt >= 0f && now - arrivedAt < ArrivedLingerSeconds + 1f && arrivedStatus != null)
            {
                // The target was cleared after arriving; keep the message for its last moment.
                wantLabel = true;
            }
            else
            {
                arrivedAt = -1f;
                stopSent = false;
            }

            var step = dt / FadeSeconds;
            arrowGroup.alpha = Mathf.MoveTowards(arrowGroup.alpha, wantArrow ? 1f : 0f, step);
            labelGroup.alpha = Mathf.MoveTowards(labelGroup.alpha, wantLabel ? 1f : 0f, step);
            if (arrowGroup.alpha <= 0f && labelGroup.alpha <= 0f)
            {
                placed = false;
                return;
            }

            var flat = Vector3.ProjectOnPlane(eye.forward, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.ProjectOnPlane(eye.up, Vector3.up);
            flat.Normalize();
            var want = eye.position + flat * AheadMetres + Vector3.down * BelowMetres;
            anchor = placed ? Vector3.Lerp(anchor, want, 1f - Mathf.Exp(-FollowRate * dt)) : want;

            if (wantArrow)
            {
                var target = NavMath.ArrowYaw(north.NorthYaw, s.ArrowBearing);
                shownYaw = placed ? Mathf.LerpAngle(shownYaw, target, 1f - Mathf.Exp(-TurnRate * dt)) : target;
            }
            placed = true;
            // The way to go is fixed in the room and the head's yaw is read live, so the chevron turns the instant
            // the head does. A canvas facing the eye turns anticlockwise for a positive roll, hence the minus.
            var facing = Quaternion.LookRotation(anchor - eye.position, Vector3.up);
            arrowRoot.SetPositionAndRotation(anchor,
                facing * Quaternion.Euler(0f, 0f, -NavMath.ChevronTurn(shownYaw, eye.eulerAngles.y)));
            var labelAt = anchor + Vector3.down * LabelBelowMetres;
            labelRoot.SetPositionAndRotation(labelAt, Quaternion.LookRotation(labelAt - eye.position, Vector3.up));
        }

        public void AddQuads(List<PanelQuad> into)
        {
            if (arrowGroup == null || !placed) return;
            if (arrowGroup.alpha > 0f) into.Add(PanelQuad.Of((RectTransform)arrowRoot));
            if (labelGroup.alpha > 0f) into.Add(PanelQuad.Of(card.Rect));
        }

        void Stop()
        {
            if (stopSent) return;
            stopSent = true;
            var settings = BackendSettings.Load();
            if (settings == null) return;
            var url = settings.Url;
            var token = settings.DeviceToken;
            var profile = BackendSettings.ProfileId;
            Task.Run(async () =>
            {
                try
                {
                    await NavClient.StopAsync(url, token, profile, cancel.Token).ConfigureAwait(false);
                }
                catch (Exception e) when (!cancel.IsCancellationRequested)
                {
                    Debug.Log($"[EDITH nav] stop failed: {e.GetBaseException().Message}");
                }
                catch (Exception)
                {
                }
                Poke();
            });
        }

        static Transform MakeCanvas(string name, float widthMetres, float heightMetres, out CanvasGroup group)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(widthMetres, heightMetres) * UnitsPerMetre;
            rect.localScale = Vector3.one * (Shrink / UnitsPerMetre);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 2;
            group = root.GetComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            return rect;
        }

        /// One white chevron pointing up the texture, two strokes with round ends, antialiased by its distance to
        /// them and tinted by its RawImage colour.
        static Texture2D ChevronTexture()
        {
            const int width = 128, height = 64;
            const float halfStroke = 7.5f;
            var left = new Vector2(12f, 12f);
            var tip = new Vector2(64f, 52f);
            var right = new Vector2(116f, 12f);
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "EDITH nav chevron", wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color32[width * height];
            for (var py = 0; py < height; py++)
            for (var px = 0; px < width; px++)
            {
                var at = new Vector2(px + 0.5f, py + 0.5f);
                var distance = Mathf.Min(ToSegment(at, left, tip), ToSegment(at, tip, right));
                var alpha = Mathf.Clamp01(halfStroke + 0.5f - distance);
                pixels[py * width + px] = new Color(1f, 1f, 1f, alpha);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        static float ToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            var along = b - a;
            var t = Mathf.Clamp01(Vector2.Dot(point - a, along) / along.sqrMagnitude);
            return Vector2.Distance(point, a + along * t);
        }
    }
}
