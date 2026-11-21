using System;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace Edith.Ask
{
    public enum MicAccess { Unknown, Asking, Granted, Denied }

    /// Ask's sound: the microphone at 16 kHz, the answer's 24 kHz speech through a streamed clip fed from Speech, and
    /// the chime and low tone, all made in code.
    public sealed class AskVoice : MonoBehaviour
    {
        public const int MicRate = LiveLink.MicRate;
        public const int SpeakerRate = 24000;
        /// 40 ms of microphone audio per message.
        public const int ChunkSamples = MicRate / 25;
        const int MicClipSeconds = 2;
        const int SpeechSeconds = 90;
        const float ReopenDelaySeconds = 1f;

        AudioSource speaker;
        AudioSource cues;
        AudioClip answer, chime, lowTone, mic;
        int micRead;
        float reopenAt = -1f;
        bool focusLostWhileAsking;
        volatile int access;

        /// Answer speech waiting to play.
        public PcmRing Speech { get; } = new PcmRing(SpeakerRate * SpeechSeconds);

        public MicAccess Access => (MicAccess)access;
        public bool MicRunning => mic != null;

        /// The sound output was reopened, which stops the microphone and the answer.
        public event Action Reopened;

        void Awake()
        {
            speaker = gameObject.AddComponent<AudioSource>();
            speaker.spatialBlend = 0f;
            speaker.loop = true;
            speaker.playOnAwake = false;
            cues = gameObject.AddComponent<AudioSource>();
            cues.spatialBlend = 0f;
            cues.playOnAwake = false;
            BuildCues();
#if !UNITY_ANDROID || UNITY_EDITOR
            access = (int)MicAccess.Granted;
#endif
        }

        void OnDestroy() => StopMic();

        public void PlayChime() => cues.PlayOneShot(chime);
        public void PlayLowTone() => cues.PlayOneShot(lowTone);

        /// Starts the answer stream from silence.
        public void StartSpeaker()
        {
            Speech.Clear();
            speaker.Stop();
            // A clip made in code plays nothing after AudioSettings.Reset, so each answer gets a new one.
            var old = answer;
            answer = AudioClip.Create("EDITH answer", SpeakerRate, 1, SpeakerRate, true, FillSpeaker);
            speaker.clip = answer;
            speaker.Play();
            if (old != null) Destroy(old);
        }

        public void StopSpeaker()
        {
            speaker.Stop();
            Speech.Clear();
        }

        /// Asks for the microphone if it has not been granted, without waiting for the answer.
        public void RequestMic()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                access = (int)MicAccess.Granted;
                return;
            }
            if (Access == MicAccess.Asking) return;
            access = (int)MicAccess.Asking;
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => access = (int)MicAccess.Granted;
            callbacks.PermissionDenied += _ => access = (int)MicAccess.Denied;
            callbacks.PermissionDeniedAndDontAskAgain += _ => access = (int)MicAccess.Denied;
            Permission.RequestUserPermission(Permission.Microphone, callbacks);
#endif
        }

        public void StartMic()
        {
            if (mic != null) return;
            mic = Microphone.Start(null, true, MicClipSeconds, MicRate);
            micRead = 0;
            if (mic == null) Debug.LogWarning("[EDITH ask] the microphone did not start");
        }

        public void StopMic()
        {
            if (mic == null) return;
            Microphone.End(null);
            mic = null;
        }

        /// Fills `chunk` with the next ChunkSamples of microphone audio, or returns false when they are not in yet.
        public bool TryReadMic(float[] chunk)
        {
            if (mic == null) return false;
            var length = mic.samples;
            var position = Microphone.GetPosition(null);
            if ((position - micRead + length) % length < chunk.Length) return false;
            // GetData wraps past the end of the looping clip.
            mic.GetData(chunk, micRead);
            micRead = (micRead + chunk.Length) % length;
            return true;
        }

        // Runs on the audio thread.
        void FillSpeaker(float[] data) => Speech.Read(data);

        // Horizon OS mutes an audio stream opened while the headset is off and keeps it muted once it is back on, so
        // the output is reopened after focus returns. Resetting inside these callbacks froze the app, so they only
        // schedule it. A permission dialog takes focus too, without muting anything.
        void OnApplicationFocus(bool focused)
        {
            if (!focused)
            {
                focusLostWhileAsking = Access == MicAccess.Asking;
                return;
            }
            if (focusLostWhileAsking) focusLostWhileAsking = false;
            else ScheduleReopen();
        }

        void OnApplicationPause(bool paused)
        {
            if (!paused && !focusLostWhileAsking) ScheduleReopen();
        }

        void ScheduleReopen() => reopenAt = Time.realtimeSinceStartup + ReopenDelaySeconds;

        void Update()
        {
            if (reopenAt < 0f || Time.realtimeSinceStartup < reopenAt) return;
            reopenAt = -1f;
            StopMic();
            speaker.Stop();
            Speech.Clear();
            AudioSettings.Reset(AudioSettings.GetConfiguration());
            BuildCues();
            Debug.Log($"[EDITH ask] sound reopened, output {AudioSettings.outputSampleRate} Hz");
            Reopened?.Invoke();
        }

        void BuildCues()
        {
            if (chime != null) Destroy(chime);
            if (lowTone != null) Destroy(lowTone);
            chime = Tones("EDITH chime", new[] { 987.8f, 1318.5f }, 0.09f, 0.22f, 0.4f);
            lowTone = Tones("EDITH low tone", new[] { 330f, 247f }, 0.14f, 0.36f, 0.35f);
        }

        /// Plucked notes, one after another `step` seconds apart.
        static AudioClip Tones(string name, float[] hertz, float step, float seconds, float gain)
        {
            const int rate = 48000;
            var samples = new float[Mathf.RoundToInt(rate * seconds)];
            for (var i = 0; i < samples.Length; i++)
            {
                var t = i / (float)rate;
                var note = Mathf.Min(hertz.Length - 1, Mathf.FloorToInt(t / step));
                var local = t - note * step;
                var envelope = Mathf.Clamp01(local * 200f) * Mathf.Exp(-local * 18f);
                samples[i] = gain * envelope * Mathf.Sin(2f * Mathf.PI * hertz[note] * t);
            }
            var clip = AudioClip.Create(name, samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
