using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Edith.Backend;
using Edith.Capture;
using Edith.Scan;
using Edith.Snapshots;
using UnityEngine;

namespace Edith.Ask
{
    /// Ask on the headset. A thumb tap chimes and starts a spoken question about what is nearest the line of sight: a
    /// Scan pin's name and hook, or a snapshot's photo, whichever is nearer, and a picture of the view ahead when
    /// neither is near; Gemini's activity detection ends the question. The answer is spoken and typed out beside an orb
    /// that perches on that snapshot, or rests low in the view clear of every snapshot, and fades once the answer is
    /// over. A thumb swipe flicks the orb away, and drawing a new box makes it vanish until the next tap.
    public sealed class AskController : MonoBehaviour
    {
        [SerializeField] ThumbGestures gestures;
        [SerializeField] AskVoice voice;
        [SerializeField] Transform eye;
        [SerializeField] Font font;
        [SerializeField] Material material;
        /// Optional: without it no pin is offered as a hint.
        [SerializeField] ScanController scan;

        /// What EDITH asks when the directions gesture opens a question.
        public const string NavigationPrompt = "Where's our next adventure?";

        const int PhotoLongSide = 512;
        // The whole view holds more than one framed object, so it keeps more detail.
        const int ViewLongSide = 768;

        readonly AskSession session = new AskSession();
        readonly List<Vector3> photoCentres = new List<Vector3>();
        readonly List<Snapshot> photographed = new List<Snapshot>();
        readonly StringBuilder said = new StringBuilder();
        readonly StringBuilder heard = new StringBuilder();
        readonly List<string> sources = new List<string>();
        AskPhase phase;
        float lastHeardAt, firstChunkAt;
        bool playingLogged;
        SnapshotRoom room;
        AnswerOrb current;
        LiveLink link;
        PhotoJpeg photo;
        Task<CameraFrame> view;
        HeadsetCamera headsetCamera;
        bool photoSent;

        /// The room whose snapshot photos can go with a question, and the camera that pictures the view when no
        /// snapshot is looked at. Without them, questions go without that photo.
        public void Bind(SnapshotRoom room, HeadsetCamera headsetCamera)
        {
            this.room = room;
            this.headsetCamera = headsetCamera;
        }

        public AskPhase Phase => session.Phase;

        // Update runs only while a question is under way or fading, and Start would wait for that, so the
        // subscriptions are made here and outlast the disabled stretches.
        void Awake()
        {
            enabled = false;
            gestures.Recognized += OnGesture;
            voice.Reopened += OnSoundReopened;
        }

        void OnDestroy()
        {
            gestures.Recognized -= OnGesture;
            voice.Reopened -= OnSoundReopened;
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
                    Dismiss(gesture == ThumbGesture.SwipeLeft ? -1f : 1f, now);
                    return;
            }
        }

        /// Sends the orb away and drops the question with it. `direction` is as AnswerOrb.Leave takes it.
        void Dismiss(float direction, float now)
        {
            if (current == null) return;
            current.Leave(direction);
            current = null;
            session.Dismiss(now);
            Sync(now);
        }

        /// Opens a question that asks where to go, as the two-handed finger-gun gesture does: the orb shows
        /// NavigationPrompt and Gemini is told to search for whatever is said. A question already under way is dropped.
        public void BeginNavigation()
        {
            var now = Time.time;
            if (session.Busy)
            {
                if (current != null) current.Leave(0f);
                current = null;
                session.Dismiss(now);
                Sync(now);
            }
            if (session.Tap(now)) Begin(navigate: true);
            Sync(now);
        }

        void Begin(bool navigate = false)
        {
            if (current != null) current.Leave(-1f);
            Snapshot asked = null;
            AskHint? hint = null;
            if (!navigate)
            {
                asked = NearestSnapshot(out var photoDegrees);
                var pin = default(ScanPin);
                var pinDegrees = 0f;
                var hasPin = scan != null && scan.TryNearest(new Pose(eye.position, eye.rotation),
                    AskGeometry.MaxGazeDegrees, out pin, out pinDegrees);
                if (AskGeometry.PinWins(hasPin, pinDegrees, asked != null, photoDegrees))
                {
                    hint = new AskHint(pin.Name, pin.Hook);
                    asked = null;
                }
            }
            current = AnswerOrb.Create(eye, font, material, room, asked);
            if (navigate) current.ShowAnswer(NavigationPrompt, 1f);
            said.Clear();
            heard.Clear();
            sources.Clear();
            lastHeardAt = -1f;
            playingLogged = false;
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
            link = LiveLink.Open(settings.Url, settings.DeviceToken, BackendSettings.ProfileId, hint, voice.Speech,
                AskVoice.ChunkSamples, navigate);
            photo = asked != null ? PhotoJpeg.Start(asked.Photo, PhotoLongSide) : null;
            view = !navigate && asked == null && hint == null ? CaptureView() : null;
            var about = navigate ? "for directions" : hint is { } h ? $"about the pin on {h.Name}"
                : photo != null ? "with a snapshot photo" : view != null ? "with the view" : "without a hint";
            Debug.Log($"[EDITH ask] listening, {about}");
        }

        /// A picture of the whole camera view, or null when the camera is not streaming.
        Task<CameraFrame> CaptureView()
        {
            if (headsetCamera == null || headsetCamera.Status != CameraStatus.Streaming) return null;
            try
            {
                return headsetCamera.CaptureFullAsync(ViewLongSide);
            }
            catch (InvalidOperationException e)
            {
                Debug.Log($"[EDITH ask] no view photo: {e.Message}");
                return null;
            }
        }

        /// The photo to send once it is ready: the snapshot's, or the view's.
        byte[] ReadyPhoto()
        {
            if (photo != null) return photo.Result;
            return view != null && view.IsCompletedSuccessfully ? view.Result.Jpeg : null;
        }

        /// The snapshot with a photo nearest the line of sight and its angle from it, or null when none is near it.
        Snapshot NearestSnapshot(out float degrees)
        {
            degrees = float.PositiveInfinity;
            if (room == null) return null;
            photoCentres.Clear();
            photographed.Clear();
            var snapshots = room.Snapshots;
            for (var i = 0; i < snapshots.Count; i++)
            {
                var (snapshot, box) = snapshots[i];
                if (snapshot == null || snapshot.Photo == null) continue;
                photoCentres.Add(box.Center);
                photographed.Add(snapshot);
            }
            var nearest = AskGeometry.NearestToGaze(eye.position, eye.forward, photoCentres, out degrees);
            return nearest < 0 ? null : photographed[nearest];
        }

        void Update()
        {
            var now = Time.time;
            if (room != null && room.Drawing)
            {
                Dismiss(0f, now);
                return;
            }
            if (session.MicWanted(now)) PumpMicrophone(now);
            if (link != null && !photoSent && ReadyPhoto() is { } jpeg)
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
            if (current != null)
            {
                current.Level = voice.Level;
                if (session.Phase == AskPhase.Answering && said.Length > 0) current.ShowAnswer(Answer(), Spoken());
            }
            if (session.Phase == AskPhase.Answering && !playingLogged && voice.Speech.Played > 0) LogFirstAudio();
        }

        void LogFirstAudio()
        {
            playingLogged = true;
            var at = Time.realtimeSinceStartup;
            var afterWords = lastHeardAt >= 0f ? $"{Ms(at - lastHeardAt)} ms after the last heard words, " : "";
            Debug.Log($"[EDITH ask] first answer audio playing {afterWords}{Ms(at - firstChunkAt)} ms after the first answer chunk");
        }

        string Answer() => SpokenNumbers.ToDigits(said.ToString().Trim());

        /// The share of the answer audio received so far that has been played, which paces the typed caption.
        float Spoken()
        {
            var played = voice.Speech.Played;
            var received = played + voice.Speech.Count;
            return received > 0 ? played / (float)received : 0f;
        }

        static int Ms(float seconds) => Mathf.RoundToInt(seconds * 1000f);

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
                    return;
                case LiveEventKind.Sources:
                    foreach (var name in e.Text.Split('\n'))
                        if (name.Length > 0 && !sources.Contains(name)) sources.Add(name);
                    var sinceHeard = lastHeardAt >= 0f ? $"{Ms(Time.realtimeSinceStartup - lastHeardAt)} ms after the last heard words" : "before any words were heard";
                    Debug.Log($"[EDITH ask] {sources.Count} sources, {sinceHeard}: {string.Join(", ", sources)}");
                    if (current != null) current.ShowSources(sources);
                    return;
                case LiveEventKind.InputText:
                    heard.Append(e.Text);
                    if (current != null) current.ShowHeard(heard.ToString());
                    if (session.Phase != AskPhase.Answering) lastHeardAt = Time.realtimeSinceStartup;
                    return;
                case LiveEventKind.TurnComplete:
                    session.TurnComplete(now);
                    return;
                case LiveEventKind.Navigation:
                    Edith.Navigation.NavArrow.Poke();
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
            var ended = from == AskPhase.Connecting || from == AskPhase.Listening || from == AskPhase.Answering;
            if (ended && !session.Busy) LogQuestion(from == AskPhase.Answering);
            switch (phase)
            {
                case AskPhase.Answering:
                    firstChunkAt = Time.realtimeSinceStartup;
                    Debug.Log($"[EDITH ask] answer starts, {voice.OutputState}");
                    voice.StopMic();
                    link?.EndAudio();
                    if (said.Length == 0 && current != null) current.ShowAnswer("…", 1f);
                    return;
                case AskPhase.Fading:
                    Close();
                    if (session.Problem != AskProblem.None)
                    {
                        voice.PlayLowTone();
                        if (current != null) current.ShowProblem(AskSession.Describe(session.Problem));
                    }
                    else if (current != null && said.Length > 0) current.ShowAnswer(Answer(), 1f);
                    return;
                case AskPhase.Idle:
                    Close();
                    voice.StopSpeaker();
                    if (current != null) current.Leave(0f);
                    current = null;
                    enabled = false;
                    return;
            }
        }

        void LogQuestion(bool answered)
        {
            Debug.Log($"[EDITH ask] heard \"{heard.ToString().Trim()}\"");
            Debug.Log($"[EDITH ask] said \"{said.ToString().Trim()}\"");
            if (!answered) return;
            var played = voice.Speech.Played;
            Debug.Log($"[EDITH ask] answer played {played} samples ({played / (float)AskVoice.SpeakerRate:F1} s), " +
                      $"{voice.Speech.Underruns} underruns, read {voice.Speech.ReadLength} at a time");
        }

        /// Stops listening and drops any answer still waiting. The speaker runs on until Idle so the speech Unity has
        /// already read out of Speech is heard to the end.
        void Close()
        {
            voice.StopMic();
            link?.Dispose();
            link = null;
            photo = null;
            view = null;
            voice.Speech.Clear();
        }

        void OnSoundReopened()
        {
            session.Fail(AskProblem.Dropped, Time.time);
            Sync(Time.time);
        }
    }
}
