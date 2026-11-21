using System;
using System.Collections.Generic;
using System.Threading;
using Edith.Ask;
using Edith.Backend;
using Edith.Hands;
using Edith.Look;
using Edith.Pinch;
using Edith.Scan;
using UnityEngine;

namespace Edith.Scrapbook
{
    /// The Scrapbook: the collected memories as a deck of cards to scroll through. The wrist button opens it in
    /// front of the user, where it stays put in the room, and closes it. A thumb swipe moves one card, a sideways
    /// pinch-drag scrubs, and a quick pinch on a side card brings it to the middle. The lens bar under the deck jumps
    /// to a day, narrows the deck to a rarity, or shows the globe of places: a pinch on a city's pin dives into its
    /// street map, and a pinch on a spot's pin narrows the deck to that spot. A pinch goes to the first thing in its
    /// line: a chip, a pin, the globe, a card, and otherwise the deck. While it is open it holds the hand gate, so
    /// pinches make no Looks and palms start no Scan.
    public sealed class ScrapbookPanel : MonoBehaviour
    {
        [SerializeField] LeftHandSource leftHand;
        [SerializeField] RightHandSource rightHand;
        [SerializeField] PinchInput pinch;
        [SerializeField] ThumbGestures gestures;
        [SerializeField] HandGate gate;
        [SerializeField] Transform eye;
        [SerializeField] Font font;
        /// Draws the cards so a hand in front of them hides them.
        [SerializeField] Material material;
        /// Draws the globe and the street map, which a hand in front also hides.
        [SerializeField] Material mapMaterial;
        [SerializeField] WristTuning wrist = new WristTuning();

        const float FadeSeconds = 0.25f;
        /// A pinch dragged this far sideways scrolls one card.
        const float MetersPerCard = 0.07f;
        const float ListSeconds = 8f;
        // More than the cards in reach, so scrolling back a few cards shows their photos at once.
        const int PhotosKept = 16;
        const int PhotosAtOnce = 3;

        enum Content { Loading, Browsing, Empty, Failed }

        readonly HashSet<int> photosLoading = new HashSet<int>();
        readonly HashSet<int> photosFailed = new HashSet<int>();
        readonly List<Chip> tabs = new List<Chip>(), chips = new List<Chip>();
        RecentCache<Texture2D> photos;
        Func<int, Texture> photoOf;
        ScrapbookClient client;
        WristButton button;
        WristButtonView buttonView;
        Transform frame;
        DeckView deckView;
        LensBar bar;
        MapImages maps;
        PlacesView places;
        AudioSource sound;
        CancellationTokenSource session;
        Content content;
        Browse browse;
        Deck deck;
        (Lens lens, Rarity? filter, Place place, DateTime day, int count)? barShows;
        float fade;
        /// What the pinch-drag under way moves, or null between drags.
        DragTarget? dragging;
        /// The dragging hand's pinch point in the deck's frame, a frame ago.
        Vector3 draggedTo;

        public bool Open { get; private set; }

        void Awake()
        {
            button = new WristButton(wrist);
            buttonView = new WristButtonView(transform, font, material, wrist.buttonRadiusMeters);
            frame = new GameObject("Deck").transform;
            frame.SetParent(transform, false);
            frame.gameObject.SetActive(false);
            deckView = new DeckView(frame, font, material);
            bar = new LensBar(frame, font, material);
            maps = new MapImages();
            places = new PlacesView(frame, font, material, mapMaterial, maps);
            photos = new RecentCache<Texture2D>(PhotosKept, Destroy);
            photoOf = id => photos.TryGet(id, out var photo) ? photo : null;
            sound = gameObject.AddComponent<AudioSource>();
            sound.spatialBlend = 0f;
            sound.playOnAwake = false;
        }

        void Start()
        {
            var settings = BackendSettings.Load();
            if (settings != null) client = new ScrapbookClient(settings.Url, settings.DeviceToken, BackendSettings.ProfileId);
        }

        void OnEnable()
        {
            pinch.Stepped += OnPinch;
            gestures.Recognized += OnGesture;
        }

        void OnDisable()
        {
            pinch.Stepped -= OnPinch;
            gestures.Recognized -= OnGesture;
            if (Open) Close();
        }

        void OnDestroy()
        {
            session?.Cancel();
            photos.Clear();
            maps.Clear();
            places.Release();
            buttonView.Release();
        }

        void Update()
        {
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
                var (title, body) = Notice(content);
                deckView.ShowNotice(title, body, eased);
                places.Hide();
                return;
            }

            deck.Advance(Time.deltaTime);
            if (Open) LoadPhotosInReach();
            deckView.Show(browse.Visible, deck.Position, photoOf, eased);
            places.Draw(browse, eased, eye.position, Time.deltaTime, Open ? client : null, session);
            var shows = (browse.Lens, browse.Filter, (Place)browse.Spot ?? browse.City, browse.Visible[deck.Target].Date, browse.Visible.Count);
            if (barShows is { } had && had.Equals(shows)) return;
            barShows = shows;
            browse.Tabs(tabs);
            browse.Chips(shows.Date, chips);
            bar.Show(tabs, chips);
        }

        // After hand tracking has updated, like the pinch and the grab.
        void LateUpdate()
        {
            var reading = button.Step(leftHand.Read(), rightHand.Read());
            buttonView.Show(reading, eye.position, Open, Time.deltaTime);
            if (!reading.Pressed) return;
            if (Open)
            {
                Play(659.3f);
                Close();
            }
            else
            {
                Play(659.3f, 987.8f);
                Show();
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
            gate.Held = true;
            content = Content.Loading;
            barShows = null;
            bar.Hide();
            photosFailed.Clear();
            maps.Retry();
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
                deck = new Deck(memories.Count);
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
            gate.Held = false;
            dragging = null;
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

        void OnPinch(PinchReading reading)
        {
            if (!Open || content != Content.Browsing) return;
            if (reading.Phase == PinchPhase.Dragging)
            {
                var hand = pinch.Owner == HandSide.Left ? leftHand.Read() : rightHand.Read();
                // A hand lost mid-drag reads wherever its joints were left.
                if (!hand.Tracked) return;
                var at = frame.InverseTransformPoint(hand.PinchPoint);
                if (dragging == null)
                    dragging = PinchRoute.DragStart(places.GlobeSpins, eye.position, reading.Start, places.GlobePosition, GlobeView.Radius);
                else if (dragging == DragTarget.Globe) places.Drag(at - draggedTo);
                else deck.Scrub((draggedTo.x - at.x) / MetersPerCard);
                draggedTo = at;
            }
            else if (dragging != null)
            {
                dragging = null;
                deck.Release();
            }
            if (reading.Event == PinchEvent.Tap) Tap(reading.Start);
        }

        void Tap(Vector3 point)
        {
            if (bar.Hit(eye.position, point, out var chip))
            {
                Choose(chip);
                Play(1318.5f);
                return;
            }
            var pin = places.HitPin(eye.position, point);
            if (pin != null)
            {
                Narrow(() =>
                {
                    if (pin is City city) browse.Dive(city);
                    else browse.ToggleSpot((Spot)pin);
                });
                Play(880f);
                return;
            }
            var card = deckView.Hit(eye.position, point);
            if (card >= 0) deck.JumpTo(card);
        }

        void Choose(in Chip chip)
        {
            if (chip.Kind == ChipKind.Date)
            {
                deck.JumpTo(browse.IndexOfDate(chip.Date));
                return;
            }
            var chosen = chip;
            Narrow(() =>
            {
                switch (chosen.Kind)
                {
                    case ChipKind.Tab: browse.SetLens(chosen.Lens); break;
                    case ChipKind.City: browse.Dive(chosen.City); break;
                    case ChipKind.Globe: browse.Surface(); break;
                    case ChipKind.Spot: browse.ToggleSpot(chosen.Spot); break;
                    default: browse.ToggleFilter(chosen.Kind == ChipKind.Tier ? chosen.Tier : (Rarity?)null); break;
                }
            });
        }

        /// Changes what the deck is narrowed to. The card in the middle stays there when it is still in the deck.
        void Narrow(Action change)
        {
            var keep = browse.Visible[deck.Target].Id;
            change();
            deck = new Deck(browse.Visible.Count, Mathf.Max(0, browse.IndexOf(keep)));
            Debug.Log($"[EDITH scrapbook] {browse.Lens} lens, {(browse.Filter is Rarity tier ? tier.ToString() : "every")} tier, " +
                      $"{((Place)browse.Spot ?? browse.City)?.Label ?? "every place"}, {browse.Visible.Count} cards");
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
                if (photo.LoadImage(jpeg)) photos.Put(id, photo);
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
