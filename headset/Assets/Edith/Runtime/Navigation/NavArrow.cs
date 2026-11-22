using System;
using System.Threading;
using System.Threading.Tasks;
using Edith.Backend;
using Edith.Pairing;
using UnityEngine;
using UnityEngine.UI;

namespace Edith.Navigation
{
    /// The blue arrow that points the way to the place EDITH found. It floats low in front of the person, follows the
    /// head lazily, and turns to point at the next point of the walking route the backend follows, or straight along the
    /// compass bearing to the target when it has no route, both measured from the phone's newest position.
    /// The headset has no compass, so north comes from NorthCalibration, fed by the phone's compass and GPS course
    /// paired with the head's yaw at the moment each reading was taken. It learns from every poll, directions or not,
    /// so north is usually known by the time directions start. Until north is known, and while the phone is silent,
    /// only the label shows. On arrival it says so, then stops the directions.
    public sealed class NavArrow : MonoBehaviour
    {
        [SerializeField] Transform eye;
        [SerializeField] Font font;
        [SerializeField] Material material;

        const float UnitsPerMetre = 1000f;
        const float ArrowMetres = 0.14f;
        const float AheadMetres = 0.55f, BelowMetres = 0.19f, LabelBelowMetres = 0.065f;
        /// How far the arrow tips up from lying flat, so it reads from a standing eye height.
        const float TiltDegrees = 62f;
        const float FollowRate = 4f, TurnRate = 6f, FadeSeconds = 0.35f;
        const float ActivePollSeconds = 1f, IdlePollSeconds = 4f;
        const float StalePhoneSeconds = 20f;
        const float ArrivedLingerSeconds = 5f;
        const float YawRecordSeconds = 0.05f;
        const int LabelFontSize = 34;
        const float NoticeSeconds = 2.5f;

        /// The chevrons from the back of the arrow to its tip: deep blue to ice, so the tip reads first.
        static readonly Color[] ChevronColours =
        {
            new Color32(41, 133, 255, 255), new Color32(111, 184, 255, 255), new Color32(207, 240, 255, 255),
        };
        /// One pulse runs from the back chevron to the tip in PulseSeconds, each chevron PulseLag behind the next.
        const float PulseSeconds = 1.2f, PulseLag = 0.2f, DimAlpha = 0.25f;
        static readonly Color LabelColor = new Color(0.95f, 0.97f, 1f, 1f);
        static volatile bool poked;

        readonly YawHistory yaws = new YawHistory();
        readonly NorthCalibration north = new NorthCalibration();
        readonly CancellationTokenSource cancel = new CancellationTokenSource();
        readonly object gate = new object();
        Transform arrowRoot, labelRoot;
        CanvasGroup arrowGroup, labelGroup;
        Text label;
        Texture2D arrowTexture;
        RawImage[] chevrons;
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
            arrowRoot = MakeCanvas("NavArrow", ArrowMetres, ArrowMetres, out arrowGroup);
            chevrons = new RawImage[ChevronColours.Length];
            for (var i = 0; i < chevrons.Length; i++)
            {
                var image = new GameObject($"Chevron {i}", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
                image.texture = arrowTexture;
                image.material = material;
                image.color = ChevronColours[i];
                image.raycastTarget = false;
                var rect = image.rectTransform;
                rect.SetParent(arrowRoot, false);
                rect.anchorMin = new Vector2(0f, (float)i / chevrons.Length);
                rect.anchorMax = new Vector2(1f, (float)(i + 1) / chevrons.Length);
                rect.sizeDelta = Vector2.zero;
                chevrons[i] = image;
            }

            labelRoot = MakeCanvas("NavLabel", 0.42f, 0.09f, out labelGroup);
            label = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(UnityEngine.UI.Shadow))
                .GetComponent<Text>();
            label.font = Edith.UI.Theme.Regular(font);
            label.fontSize = LabelFontSize;
            label.alignment = TextAnchor.UpperCenter;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.color = LabelColor;
            label.material = material;
            label.raycastTarget = false;
            var shadow = label.GetComponent<UnityEngine.UI.Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
            Fill(label.rectTransform, labelRoot);
            arrowGroup.alpha = labelGroup.alpha = 0f;
        }

        void OnEnable()
        {
            if (OVRManager.display != null) OVRManager.display.RecenteredPose += OnRecentered;
        }

        void OnDisable()
        {
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
            if (next.active && status is not { active: true }) Debug.Log($"[EDITH nav] guiding to {next.name}");
            if (!next.active && status is { active: true }) Debug.Log("[EDITH nav] directions ended");
            status = next;
            // An idle reply still carries the newest phone reading, so north is learned before directions start.
            if (next.fix_age_s < 0f) return;

            // The backend received the fix fix_age_s before it answered, and it answered about half a round trip ago.
            var fixAt = sentAt + (now - sentAt) * 0.5f - next.fix_age_s;
            if (Mathf.Abs(fixAt - lastSampledAt) < 0.5f || !yaws.TryAt(fixAt, 0.3f, out var headYaw)) return;
            lastSampledAt = fixAt;
            if (next.course_deg >= 0f && next.speed_mps >= NavMath.CourseMinSpeed)
                north.Add(NavMath.NorthFrom(headYaw, next.course_deg), NavMath.CourseWeight);
            if (next.heading_deg >= 0f)
                north.Add(NavMath.NorthFrom(headYaw, next.heading_deg), NavMath.CompassWeightFor(next.heading_accuracy_deg));
        }

        void Show(float now)
        {
            var dt = Time.deltaTime;
            var s = status;
            var wantArrow = false;
            var wantLabel = false;
            if (!PairingGate.Open)
            {
            }
            else if (now < noticeUntil)
            {
                label.text = notice;
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
                label.text = $"You're at {s.name}";
                wantLabel = true;
                if (now - arrivedAt >= ArrivedLingerSeconds) Stop();
            }
            else if (s.active)
            {
                arrivedAt = -1f;
                wantLabel = true;
                if (!s.HasFix || s.fix_age_s > StalePhoneSeconds)
                    label.text = $"{s.name}\nWaiting for your phone's position";
                else if (!north.Ready)
                    label.text = $"{s.name} · {NavMath.DistanceText(s.WalkMetres)}\nHold your phone flat, pointing where you face";
                else
                {
                    label.text = $"{s.name}\n{NavMath.DistanceText(s.WalkMetres)}";
                    if (s.HasRoute && NavMath.ManeuverText(s.maneuver, s.maneuver_distance_m) is { Length: > 0 } turn)
                        label.text += "\n" + turn;
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
            var bob = Mathf.Sin(now * 2f) * 0.006f;
            arrowRoot.SetPositionAndRotation(anchor + Vector3.up * bob,
                Quaternion.Euler(0f, shownYaw, 0f) * Quaternion.Euler(TiltDegrees, 0f, 0f));
            for (var i = 0; i < chevrons.Length; i++)
            {
                var colour = ChevronColours[i];
                colour.a = NavMath.ChevronPulse(now, i, PulseSeconds, PulseLag, DimAlpha);
                chevrons[i].color = colour;
            }
            var labelAt = anchor + Vector3.down * LabelBelowMetres;
            labelRoot.SetPositionAndRotation(labelAt, Quaternion.LookRotation(labelAt - eye.position, Vector3.up));
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
            rect.localScale = Vector3.one / UnitsPerMetre;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 2;
            group = root.GetComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            return rect;
        }

        static void Fill(RectTransform child, Transform parent)
        {
            child.SetParent(parent, false);
            child.anchorMin = Vector2.zero;
            child.anchorMax = Vector2.one;
            child.sizeDelta = Vector2.zero;
        }

        /// One white chevron pointing up the texture, an outline with round ends, antialiased by its distance to the
        /// two strokes. The chevrons are tinted by their RawImage colour.
        static Texture2D ChevronTexture()
        {
            const int width = 128, height = 64;
            const float halfStroke = 5.5f;
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
