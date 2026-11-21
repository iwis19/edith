using System.Collections.Generic;
using System.Text;
using Edith.Backend;
using Edith.Grab;
using Edith.Pinch;
using Edith.Snapshots;
using UnityEngine;

namespace Edith.Ask
{
    /// Ask on the headset. A thumb tap chimes and starts a spoken question, with the photo of the snapshot nearest the
    /// line of sight when there is one; Gemini's activity detection ends the question. The answer is spoken and written
    /// on a card that follows the gaze and fades once the answer is over. A thumb swipe flicks the card away, a fist
    /// carries it and pins it where it is let go, and a quick pinch in line with a pinned card clears it.
    public sealed class AskController : MonoBehaviour, IGrabTargets
    {
        [SerializeField] ThumbGestures gestures;
        [SerializeField] AskVoice voice;
        [SerializeField] GrabInput grab;
        [SerializeField] PinchInput pinch;
        [SerializeField] Transform eye;
        [SerializeField] Font font;
        [SerializeField] Material material;

        const int PhotoLongSide = 512;
        const int MaxPinned = 3;
        // About one degree of slack around a pinned card for a quick pinch.
        const float TapMargin = 0.02f;

        readonly AskSession session = new AskSession();
        readonly List<AnswerCard> pinned = new List<AnswerCard>();
        readonly List<Vector3> photoCentres = new List<Vector3>();
        readonly List<Texture> photos = new List<Texture>();
        readonly StringBuilder said = new StringBuilder();
        AskPhase phase;
        SnapshotRoom room;
        AnswerCard current;
        LiveLink link;
        PhotoJpeg photo;
        bool photoSent;
        int nextCardId = -1;

        /// The room whose snapshot photos can go with a question. Without one, every question goes without a photo.
        public void Bind(SnapshotRoom room) => this.room = room;

        // Update runs only while a question is under way or fading, and Start would wait for that, so the
        // subscriptions are made here and outlast the disabled stretches.
        void Awake()
        {
            enabled = false;
            gestures.Recognized += OnGesture;
            voice.Reopened += OnSoundReopened;
            grab.Register(this);
            grab.Stepped += OnGrab;
            pinch.Stepped += OnPinch;
        }

        void OnDestroy()
        {
            gestures.Recognized -= OnGesture;
            voice.Reopened -= OnSoundReopened;
            grab.Unregister(this);
            grab.Stepped -= OnGrab;
            pinch.Stepped -= OnPinch;
            link?.Dispose();
        }

        void OnGesture(ThumbGesture gesture)
        {
            var now = Time.time;
            switch (gesture)
            {
                case ThumbGesture.Tap:
                    if (session.Tap(now)) Begin();
                    Sync(now);
                    return;
                case ThumbGesture.SwipeLeft:
                case ThumbGesture.SwipeRight:
                    if (current == null || current.Pinned) return;
                    current.Leave(gesture == ThumbGesture.SwipeLeft ? -1f : 1f);
                    current = null;
                    session.Dismiss(now);
                    Sync(now);
                    return;
            }
        }

        void Begin()
        {
            if (current != null && !current.Pinned) current.Leave(-1f);
            current = AnswerCard.Create(nextCardId--, eye, font, material);
            said.Clear();
            photoSent = false;
            voice.PlayChime();
            voice.StartSpeaker();
            voice.RequestMic();
            enabled = true;

            var settings = BackendSettings.Load();
            if (settings == null)
            {
                session.Fail(AskProblem.NotConfigured, Time.time);
                return;
            }
            link = LiveLink.Open(settings.Url, settings.DeviceToken, BackendSettings.ProfileId, voice.Speech, AskVoice.ChunkSamples);
            photo = NearestPhoto() is { } texture ? PhotoJpeg.Start(texture, PhotoLongSide) : null;
            Debug.Log($"[EDITH ask] listening, {(photo != null ? "with" : "without")} a photo");
        }

        /// The photo of the snapshot nearest the line of sight, or null when no snapshot has one.
        Texture NearestPhoto()
        {
            if (room == null) return null;
            photoCentres.Clear();
            photos.Clear();
            var snapshots = room.Snapshots;
            for (var i = 0; i < snapshots.Count; i++)
            {
                var (snapshot, box) = snapshots[i];
                if (snapshot == null || snapshot.Photo == null) continue;
                photoCentres.Add(box.Center);
                photos.Add(snapshot.Photo);
            }
            var nearest = AskGeometry.NearestToGaze(eye.position, eye.forward, photoCentres);
            return nearest < 0 ? null : photos[nearest];
        }

        void Update()
        {
            var now = Time.time;
            if (session.MicWanted(now)) PumpMicrophone(now);
            if (link != null && !photoSent && photo?.Result is { } jpeg)
            {
                photoSent = true;
                if (session.Phase == AskPhase.Connecting || session.Phase == AskPhase.Listening) link.SendImage(jpeg);
            }
            while (link != null && link.TryNext(out var e))
            {
                Handle(e, now);
                Sync(now);
            }
            session.Tick(now, voice.Speech.Count);
            Sync(now);
        }

        void PumpMicrophone(float now)
        {
            switch (voice.Access)
            {
                case MicAccess.Denied:
                    session.Fail(AskProblem.MicDenied, now);
                    return;
                case MicAccess.Granted:
                    voice.StartMic();
                    break;
                default:
                    return;
            }
            if (link == null) return;
            var chunk = link.RentChunk();
            while (voice.TryReadMic(chunk))
            {
                link.SendAudio(chunk);
                chunk = link.RentChunk();
            }
            link.ReturnChunk(chunk);
        }

        void Handle(in LiveEvent e, float now)
        {
            switch (e.Kind)
            {
                case LiveEventKind.Ready:
                    session.Ready(now);
                    return;
                case LiveEventKind.Audio:
                    session.AnswerAudio(now);
                    return;
                case LiveEventKind.OutputText:
                    said.Append(e.Text);
                    if (current != null) current.ShowAnswer(SpokenNumbers.ToDigits(said.ToString().Trim()));
                    return;
                case LiveEventKind.InputText:
                    return;
                case LiveEventKind.TurnComplete:
                    session.TurnComplete(now);
                    return;
                case LiveEventKind.Interrupted:
                    Debug.Log("[EDITH ask] Gemini was interrupted");
                    return;
                case LiveEventKind.Closed:
                    session.Closed(now);
                    return;
                case LiveEventKind.Failed:
                    session.Fail(e.Problem, now);
                    return;
            }
        }

        /// Acts on a change of phase once, whatever caused it.
        void Sync(float now)
        {
            if (session.Phase == phase) return;
            var from = phase;
            phase = session.Phase;
            Debug.Log($"[EDITH ask] {from} -> {phase}{(session.Problem != AskProblem.None ? $" ({session.Problem})" : "")}");
            switch (phase)
            {
                case AskPhase.Answering:
                    voice.StopMic();
                    link?.EndAudio();
                    if (said.Length == 0 && current != null) current.ShowAnswer("…");
                    return;
                case AskPhase.Fading:
                    Close();
                    if (session.Problem != AskProblem.None)
                    {
                        voice.PlayLowTone();
                        if (current != null) current.ShowProblem(AskSession.Describe(session.Problem));
                    }
                    return;
                case AskPhase.Idle:
                    Close();
                    if (current != null && !current.Pinned) current.Leave(0f);
                    current = null;
                    enabled = false;
                    return;
            }
        }

        void Close()
        {
            voice.StopMic();
            voice.StopSpeaker();
            link?.Dispose();
            link = null;
            photo = null;
        }

        void OnSoundReopened()
        {
            session.Fail(AskProblem.Dropped, Time.time);
            Sync(Time.time);
        }

        public void AddGrabTargets(List<(int id, SightBox box)> targets)
        {
            if (current != null && !current.Leaving) targets.Add((current.Id, current.Box));
            for (var i = 0; i < pinned.Count; i++)
                if (pinned[i] != current && !pinned[i].Leaving) targets.Add((pinned[i].Id, pinned[i].Box));
        }

        AnswerCard Find(int id)
        {
            if (current != null && current.Id == id) return current;
            for (var i = 0; i < pinned.Count; i++)
                if (pinned[i].Id == id) return pinned[i];
            return null;
        }

        void OnGrab(GrabReading reading)
        {
            if (reading.CardId >= 0) return;
            var card = Find(reading.CardId);
            if (card == null) return;
            switch (reading.Event)
            {
                case GrabEvent.Grabbed:
                    card.BeginHold(reading.Grip, reading.Rotation);
                    return;
                case GrabEvent.Dropped:
                case GrabEvent.Pocketed:
                    Pin(card);
                    return;
            }
            if (reading.Phase == GrabPhase.Holding && reading.HasGrip) card.HoldAt(reading.Grip, reading.Rotation);
        }

        void Pin(AnswerCard card)
        {
            card.Pin();
            if (!pinned.Contains(card)) pinned.Add(card);
            while (pinned.Count > MaxPinned)
            {
                var oldest = pinned[0];
                pinned.RemoveAt(0);
                oldest.Leave(-1f);
                if (oldest == current) current = null;
            }
            Debug.Log($"[EDITH ask] answer card {card.Id} pinned, {pinned.Count} pinned");
        }

        void OnPinch(PinchReading reading)
        {
            if (reading.Event != PinchEvent.Tap || pinned.Count == 0) return;
            for (var i = pinned.Count - 1; i >= 0; i--)
            {
                var card = pinned[i];
                if (!AskGeometry.InLine(card.Box, eye.position, reading.Start, TapMargin)) continue;
                pinned.RemoveAt(i);
                if (card == current) current = null;
                card.Leave(1f);
                Debug.Log($"[EDITH ask] pinned answer card {card.Id} cleared");
                return;
            }
        }
    }
}
