using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Edith.Ask;
using Edith.Backend;
using Edith.Grab;
using Edith.Hands;
using Edith.Look;
using Edith.Scan;
using UnityEngine;

namespace Edith.Scrapbook
{
    /// The Scrapbook: the collected memories as a deck of cards to scroll through. Both palms held up side by side
    /// open it in front of the user, where it stays put in the room, and the same pose held again closes it. A
    /// thumb swipe moves one card. Cards and the rarity chips under the deck are picked with the hand rays: aim, pinch
    /// and let go on the same thing. A pinch pressed on a card that moves 6 cm or more sideways scrubs the deck
    /// instead. A chip narrows the deck to one tier and wins over a card behind it. While it is open it holds the
    /// hand gate, so pinches make no Looks and palms start no Scan.
    public sealed class ScrapbookPanel : MonoBehaviour
    {
        [SerializeField] LeftHandSource leftHand;
        [SerializeField] RightHandSource rightHand;
        [SerializeField] ThumbGestures gestures;
        [SerializeField] HandGate gate;
        [SerializeField] Transform eye;
        [SerializeField] Font font;
        /// Draws the cards so a hand in front of them hides them.
        [SerializeField] Material material;
        [SerializeField] BookTuning book = new BookTuning();

        const float FadeSeconds = 0.25f;
        const float ListSeconds = 8f;
        const float NearLogSeconds = 1f;
        // The ring that fills while the open pose is held floats this far above the palms, clear of the hands.
        const float RingAboveMeters = 0.12f;
        // More than the cards in reach, so scrolling back a few cards shows their photos at once.
        const int PhotosKept = 16;
        const int PhotosAtOnce = 3;
        // Ray targets: cards are their index in the deck, chips this plus their tier.
        const int ChipTarget = 1 << 24;

        enum Content { Loading, Browsing, Empty, Failed }

        readonly HashSet<int> photosLoading = new HashSet<int>();
        readonly HashSet<int> photosFailed = new HashSet<int>();
        readonly List<Chip> chips = new List<Chip>();
        RecentCache<Texture2D> photos;
        Func<int, Texture> photoOf;
        ScrapbookClient client;
        BookPose pose;
        GrabInput grab;
        ScanMarks ring;
        Vector3 ringAt;
        float ringProgress, nearLoggedAt;
        Transform frame;
        FrameFollow follow = new FrameFollow();
        bool listeningForRecentre;
        // Frames left before the deck jumps in front of the eye after a recentre, once the eye has its new pose.
        int recentreIn;
        DeckView deckView, leaving;
        bool leavingShown;
        /// The deck a change replaced, drawn fading out while the new one forms, and the card that stays in the middle.
        readonly List<Memory> replaced = new List<Memory>();
        float replacedAt;
        Memory kept;
        TierBar bar;
        AudioSource sound;
        CancellationTokenSource session;
        Content content;
        Browse browse;
        Deck deck;
        (Rarity? filter, int count)? barShows;
        float fade;
        /// The scrubbing hand's pinch point in the deck's frame, a frame ago.
        Vector3 draggedTo;
        /// The pinch pressed on a card, or null, which scrubs once it sweeps far enough sideways.
        PinchIntent intent;
        HandRays rays;
        RayTargets findTarget;

        public bool Open { get; private set; }

        void Awake()
        {
            pose = new BookPose(book);
            grab = rightHand.GetComponent<GrabInput>();
            ring = new ScanMarks(font, material);
            frame = new GameObject("Deck").transform;
            frame.SetParent(transform, false);
            frame.gameObject.SetActive(false);
            deckView = new DeckView(frame, font, material);
            bar = new TierBar(frame, font, material);
            photos = new RecentCache<Texture2D>(PhotosKept, Destroy);
            photoOf = id => photos.TryGet(id, out var photo) ? photo : null;
            sound = gameObject.AddComponent<AudioSource>();
            sound.spatialBlend = 0f;
            sound.playOnAwake = false;
            rays = new HandRays("Scrapbook");
            findTarget = TargetOn;
        }

        void Start()
        {
            var settings = BackendSettings.Load();
            if (settings != null) client = new ScrapbookClient(settings.Url, settings.DeviceToken, BackendSettings.ProfileId);
        }

        void OnEnable() => gestures.Recognized += OnGesture;

        void OnDisable()
        {
            gestures.Recognized -= OnGesture;
            if (listeningForRecentre && OVRManager.display != null) OVRManager.display.RecenteredPose -= OnRecentred;
            listeningForRecentre = false;
            if (Open) Close();
        }

        // Recentring moves the whole tracking space, so the deck would hang where the old space put it.
        void OnRecentred() => recentreIn = 2;

        void OnDestroy()
        {
            session?.Cancel();
            photos.Clear();
            ring.Dispose();
            rays.Dispose();
        }

        void Update()
        {
            // OVRManager makes its display after the scene's first frames, so the subscription waits for it.
            if (!listeningForRecentre && OVRManager.display != null)
            {
                OVRManager.display.RecenteredPose += OnRecentred;
                listeningForRecentre = true;
            }
            if (recentreIn > 0 && --recentreIn == 0 && Open)
            {
                var front = DeckLayout.Frame(new Pose(eye.position, eye.rotation));
                frame.SetPositionAndRotation(front.position, front.rotation);
                follow = new FrameFollow();
                Debug.Log("[EDITH scrapbook] recentred, deck moved in front");
            }
            ring.SetDeck(ringAt, ringProgress);
            ring.Tick(eye.position);
            fade = Mathf.MoveTowards(fade, Open ? 1f : 0f, Time.deltaTime / FadeSeconds);
            if (!frame.gameObject.activeSelf) return;
            if (!Open && fade == 0f)
            {
                frame.gameObject.SetActive(false);
                photos.Clear();
                return;
            }

            var eased = fade * fade * (3f - 2f * fade);
            bar.Alpha = eased;
            if (content != Content.Browsing)
            {
                if (barShows != null) bar.Hide();
                barShows = null;
                HideLeaving();
                var (title, body) = Notice(content);
                deckView.ShowNotice(title, body, eased);
                return;
            }

            if (Open)
            {
                var placed = follow.Step(new Pose(frame.position, frame.rotation), new Pose(eye.position, eye.rotation),
                    Time.deltaTime, intent != null);
                frame.SetPositionAndRotation(placed.position, placed.rotation);
            }
            deck.Advance(Time.deltaTime);
            if (Open) LoadPhotosInReach();
            deckView.Show(browse.Visible, deck.Position, photoOf, eased, deck.Form, kept);
            DrawLeaving(eased);
            var shows = (browse.Filter, browse.Visible.Count);
            if (barShows is { } had && had.Equals(shows)) return;
            barShows = shows;
            browse.Chips(chips);
            bar.Show(chips);
        }

        void DrawLeaving(float eased)
        {
            if (deck.Form >= 1f)
            {
                HideLeaving();
                return;
            }
            leaving ??= new DeckView(frame, font, material);
            leavingShown = true;
            leaving.Show(replaced, replacedAt, photoOf, eased, 1f - deck.Form, null, kept);
        }

        void HideLeaving()
        {
            if (!leavingShown) return;
            leavingShown = false;
            leaving.Hide();
        }

        // After hand tracking has updated, like the pinch and the grab.
        void LateUpdate()
        {
            var left = leftHand.Read();
            var right = rightHand.Read();
            // A hand closing on, holding or letting go of a card is busy, so it cannot pose.
            if (grab != null && grab.Claimed(HandSide.Left)) left = left.Unseen();
            if (grab != null && grab.Claimed(HandSide.Right)) right = right.Unseen();
            Point(left, right);
            var reading = pose.Step(left, right);
            ringProgress = reading.Progress;
            if (reading.Progress > 0f) ringAt = reading.Between + Vector3.up * RingAboveMeters;
            if (reading.Toggled && !Open)
            {
                Debug.Log($"[EDITH book] opened: {reading}");
                Play(659.3f, 987.8f);
                Show();
            }
            else if (reading.Toggled)
            {
                Debug.Log($"[EDITH book] closed: {reading}");
                Play(987.8f, 659.3f);
                Close();
            }
            // A near miss logs what each hand read, so the thresholds can be tuned from real hands.
            else if (reading.Near && reading.Progress == 0f && Time.time - nearLoggedAt >= NearLogSeconds)
            {
                nearLoggedAt = Time.time;
                Debug.Log($"[EDITH book] near: {reading}");
            }
        }

        static (string title, string body) Notice(Content content)
        {
            switch (content)
            {
                case Content.Empty:
                    return ("Nothing collected yet", "Grab a snapshot and pull it to your chest to keep it.");
                case Content.Failed:
                    return ("Couldn't reach EDITH", "Check the Wi-Fi, then open the Scrapbook again.");
                default:
                    return ("Opening your Scrapbook…", null);
            }
        }

        async void Show()
        {
            Open = true;
            gate.Hold(this, true);
            content = Content.Loading;
            barShows = null;
            bar.Hide();
            photosFailed.Clear();
            follow = new FrameFollow();
            var at = DeckLayout.Frame(new Pose(eye.position, eye.rotation));
            frame.SetPositionAndRotation(at.position, at.rotation);
            frame.gameObject.SetActive(true);
            session = new CancellationTokenSource();
            var opened = session;
            Debug.Log("[EDITH scrapbook] opened");
            try
            {
                if (client == null) throw new InvalidOperationException("no backend in this build");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(opened.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(ListSeconds));
                var memories = await client.List(timeout.Token);
                if (this == null || opened.IsCancellationRequested) return;
                browse = new Browse(memories);
                deck = new Deck(browse.Visible.Count);
                content = memories.Count == 0 ? Content.Empty : Content.Browsing;
                Debug.Log($"[EDITH scrapbook] {memories.Count} memories");
            }
            catch (Exception e)
            {
                if (this == null || opened.IsCancellationRequested) return;
                Debug.Log($"[EDITH scrapbook] no memories: {(e is OperationCanceledException ? "timeout" : e.GetBaseException().Message)}");
                content = Content.Failed;
                Play(440f);
            }
        }

        void Close()
        {
            Open = false;
            gate.Hold(this, false);
            intent = null;
            rays.Hide();
            session.Cancel();
            Debug.Log("[EDITH scrapbook] closed");
        }

        void OnGesture(ThumbGesture gesture)
        {
            if (!Open || content != Content.Browsing) return;
            // The cards move with the thumb: a swipe left brings the next older card in from the right.
            if (gesture == ThumbGesture.SwipeLeft) deck.Step(1);
            else if (gesture == ThumbGesture.SwipeRight) deck.Step(-1);
        }

        /// Steps the hand rays while the deck is up: a click picks, and a pinch pressed on a card can scrub instead.
        void Point(in HandFrame left, in HandFrame right)
        {
            if (!Open || content != Content.Browsing)
            {
                intent = null;
                if (!Open) rays.Hide();
                else rays.Step(right, left, NoTargets);
                return;
            }
            var reading = rays.Step(right, left, findTarget);
            if (reading.Event == RayEvent.Pressed && reading.Pressed >= 0 && reading.Pressed < ChipTarget)
            {
                var pressedInFrame = frame.InverseTransformPoint((reading.Owner == HandSide.Left ? left : right).PinchPoint);
                intent = new PinchIntent(pressedInFrame);
                // The deck scrubs from where the sweep began, so it does not jump by the 6 cm it took to tell a sweep
                // from a reach.
                draggedTo = pressedInFrame;
            }
            if (intent != null && reading.Owner is HandSide side) Follow(side == HandSide.Left ? left : right);
            if (reading.Event is RayEvent.Clicked or RayEvent.Released or RayEvent.Cancelled) End(reading);
        }

        static int NoTargets(Ray ray, out float distance)
        {
            distance = 0f;
            return -1;
        }

        /// Moves the deck with the pinch once it has gone far enough sideways to scrub.
        void Follow(in HandFrame hand)
        {
            // A hand lost mid-drag reads wherever its joints were left.
            if (!hand.Tracked) return;
            var at = frame.InverseTransformPoint(hand.PinchPoint);
            var wasDragging = intent.Scrubbing;
            if (!intent.Move(at)) return;
            if (wasDragging) deck.Scrub((draggedTo.x - at.x) / ScrubTuning.MetersPerCard, Time.time);
            draggedTo = at;
        }

        /// A pinch ends in a scrub's landing, or a click picks what it was pressed on.
        void End(in RayReading reading)
        {
            var scrubbed = intent != null && intent.Scrubbing;
            var how = intent != null ? $"{reading.Event}, {intent.Sideways * 100f:0.0} cm sideways" : reading.Event.ToString();
            intent = null;
            if (scrubbed)
            {
                Debug.Log($"[EDITH scrapbook] scrubbed the deck ({how}), landed on card {deck.Release(Time.time)}");
                return;
            }
            if (reading.Event != RayEvent.Clicked)
            {
                Debug.Log($"[EDITH scrapbook] ignored pinch ({how})");
                return;
            }
            Debug.Log($"[EDITH scrapbook] {Pick(reading.Pressed)} ({how})");
        }

        /// What `ray` points at: a chip, which wins over a card behind it, or a card, and how far along the ray it
        /// meets the deck. The distance is to the plane of the card in the middle, which the chips share closely enough
        /// for the cursor, and is 0 when the ray passes outside the deck and its chips.
        int TargetOn(Ray ray, out float distance)
        {
            var plane = new Plane(frame.forward, frame.TransformPoint(ScrapbookPlacement.CardCentre));
            if (!plane.Raycast(ray, out distance)) distance = 0f;
            var through = ray.GetPoint(1f);
            var target = bar.Hit(ray.origin, through, out var chip, out _) ? ChipTarget + (int)chip.Tier
                : deckView.Hit(ray.origin, through, out _);
            if (target < 0 && distance > 0f && !OnDeck(frame.InverseTransformPoint(ray.GetPoint(distance)))) distance = 0f;
            return target;
        }

        /// Whether `local`, a point on the middle card's plane in the deck's frame, lies over the deck: from the side
        /// cards' outer edges across, and from the top of the photos down past the chips.
        static bool OnDeck(Vector3 local)
        {
            const float sideways = ScrapbookPlacement.FirstAside + 2f * ScrapbookPlacement.NextAside + DeckLayout.CardWidth * 0.5f;
            var top = ScrapbookPlacement.CardCentre.y + DeckLayout.CardHeight * 0.5f + 0.03f;
            var bottom = ScrapbookPlacement.ChipsCentre.y * (ScrapbookPlacement.CardAhead / ScrapbookPlacement.ChipsCentre.z) - 0.05f;
            return Mathf.Abs(local.x) <= sideways && local.y <= top && local.y >= bottom;
        }

        /// Does what the click on `target` chose, and says what that was.
        string Pick(int target)
        {
            if (target >= ChipTarget)
            {
                var tier = (Rarity)(target - ChipTarget);
                var chip = chips.Find(c => c.Tier == tier);
                Narrow(() => browse.ToggleFilter(tier));
                Play(1318.5f);
                return $"chose tier \"{chip.Label}\"";
            }
            deck.JumpTo(target);
            return $"chose card {target} \"{browse.Visible[target].Name}\"";
        }

        /// Changes what the deck is narrowed to. The card in the middle stays there when it is still in the deck, and
        /// the others fold away and fan out again.
        void Narrow(Action change)
        {
            var middle = browse.Visible[deck.Target];
            replaced.Clear();
            replaced.AddRange(browse.Visible);
            replacedAt = deck.Position;
            change();
            if (browse.Visible.Count == replaced.Count && browse.Visible.SequenceEqual(replaced)) return;
            var at = browse.IndexOf(middle.Id);
            kept = at >= 0 ? middle : null;
            deck = new Deck(browse.Visible.Count, Mathf.Max(0, at), true);
            Debug.Log($"[EDITH scrapbook] {(browse.Filter is Rarity tier ? tier.ToString() : "every")} tier, {browse.Visible.Count} cards");
        }

        void LoadPhotosInReach()
        {
            var middle = Mathf.RoundToInt(deck.Position);
            for (var away = 0; away <= DeckLayout.Reach; away++)
                for (var side = -1; side <= 1; side += 2)
                {
                    var at = middle + side * away;
                    if (at < 0 || at >= browse.Visible.Count || photosLoading.Count >= PhotosAtOnce) continue;
                    var id = browse.Visible[at].Id;
                    if (!photosLoading.Contains(id) && !photosFailed.Contains(id) && !photos.TryGet(id, out _)) LoadPhoto(id, session);
                }
        }

        async void LoadPhoto(int id, CancellationTokenSource opened)
        {
            photosLoading.Add(id);
            try
            {
                var jpeg = await client.Image(id, opened.Token);
                if (this == null || opened.IsCancellationRequested) return;
                var photo = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (photo.LoadImage(jpeg))
                {
                    var rounded = MemoryCard.Rounded(photo);
                    if (rounded != null)
                    {
                        Destroy(photo);
                        photo = rounded;
                    }
                    photos.Put(id, photo);
                }
                else
                {
                    Destroy(photo);
                    photosFailed.Add(id);
                }
            }
            catch (Exception e)
            {
                if (this == null || opened.IsCancellationRequested) return;
                Debug.Log($"[EDITH scrapbook] no photo for memory {id}: {e.GetBaseException().Message}");
                photosFailed.Add(id);
            }
            finally
            {
                photosLoading.Remove(id);
            }
        }

        void Play(float first, float second = 0f)
        {
            // Each tone is made fresh: a clip made earlier plays nothing once the sound output has been reopened.
            var clip = ScanController.Tone(first, second);
            sound.PlayOneShot(clip);
            Destroy(clip, clip.length + 0.5f);
        }
    }
}
