using System;

namespace Edith.Pairing
{
    /// Confirming checks, at a launch the headset remembers a pairing, that the backend still keeps the phone's
    /// position before anything turns on. Paired watches that it stays kept.
    public enum PairPhase { Confirming, Requesting, Showing, Connected, Paired }

    public enum PairCall { None, CheckLocation, RequestCode, PollStatus }

    /// The pairing's life as a function of time and the results of its three backend calls. The driver asks `Next`
    /// what to call, makes at most one call at a time, and hands back what happened. All times are seconds on one
    /// clock. Only a code claimed on the phone pairs an unpaired headset; until then the flow is Locked and keeps
    /// asking, however long it takes. A remembered pairing stays Locked at launch until the backend confirms the phone
    /// position, and a missing one shows a code at once. Once paired it checks every WatchSeconds and locks again when
    /// the position is gone LostAfterMisses times in a row. A backend that cannot be reached never locks a paired
    /// headset.
    public sealed class PairingFlow
    {
        public const float PollSeconds = 3f;
        public const float RetrySeconds = 10f;
        public const float ConnectedSeconds = 2f;
        public const float WatchSeconds = 30f;
        // Three misses 30 s apart give a restarted backend a minute to hear again from a phone that resends every 10 s.
        public const int LostAfterMisses = 3;

        float nextCallAt;
        float connectedUntil;
        bool inFlight;
        int misses;

        /// `paired`: the headset remembers a pairing from an earlier launch.
        public PairingFlow(bool paired, float now)
        {
            Phase = paired ? PairPhase.Confirming : PairPhase.Requesting;
            nextCallAt = now;
        }

        public PairPhase Phase { get; private set; }
        public string Code { get; private set; }
        public string PhoneUrl { get; private set; }

        /// The clock time the shown code runs out, or 0 when no code is up.
        public float CodeExpiresAt { get; private set; }

        /// Backend calls that failed in a row.
        public int Failures { get; private set; }

        /// Every feature but the pairing panel stays off.
        public bool Locked => Phase == PairPhase.Confirming || Phase == PairPhase.Requesting || Phase == PairPhase.Showing;

        /// The panel is on screen: a code is on its way or up, or the pairing was just confirmed. Confirming a remembered
        /// pairing takes one call and shows nothing.
        public bool Visible => Phase == PairPhase.Requesting || Phase == PairPhase.Showing || Phase == PairPhase.Connected;

        /// The call to make now, or None. Asking again before its result comes back returns None.
        public PairCall Next(float now)
        {
            if (Phase == PairPhase.Connected)
            {
                if (now >= connectedUntil) Watch(now);
                return PairCall.None;
            }
            if (inFlight || now < nextCallAt) return PairCall.None;
            inFlight = true;
            return Phase switch
            {
                PairPhase.Requesting => PairCall.RequestCode,
                PairPhase.Showing => PairCall.PollStatus,
                _ => PairCall.CheckLocation,
            };
        }

        public void OnLocation(bool hasPhoneLocation, float now)
        {
            if (!Answer(PairPhase.Confirming) && !Answer(PairPhase.Paired)) return;
            if (hasPhoneLocation)
            {
                Watch(now);
                return;
            }
            if (Phase == PairPhase.Paired && ++misses < LostAfterMisses)
            {
                nextCallAt = now + WatchSeconds;
                return;
            }
            Phase = PairPhase.Requesting;
            nextCallAt = now;
        }

        public void OnCode(string code, string phoneUrl, float now, float expiresInSeconds = 600f)
        {
            if (!Answer(PairPhase.Requesting)) return;
            CodeExpiresAt = now + expiresInSeconds;
            Code = code;
            PhoneUrl = phoneUrl;
            Phase = PairPhase.Showing;
            nextCallAt = now + PollSeconds;
        }

        public void OnStatus(bool paired, bool expired, float now)
        {
            if (!Answer(PairPhase.Showing)) return;
            if (paired)
            {
                Code = null;
                Phase = PairPhase.Connected;
                connectedUntil = now + ConnectedSeconds;
            }
            else if (expired)
            {
                Code = null;
                Phase = PairPhase.Requesting;
                nextCallAt = now;
            }
            else
            {
                nextCallAt = now + PollSeconds;
            }
        }

        /// The call failed. It is tried again after RetrySeconds, for as long as it keeps failing. A remembered pairing
        /// that cannot be confirmed at launch is trusted, so an unreachable backend does not lock it.
        public void OnError(float now)
        {
            if (!inFlight) return;
            inFlight = false;
            Failures++;
            if (Phase == PairPhase.Confirming)
            {
                Watch(now);
                nextCallAt = now + RetrySeconds;
                return;
            }
            nextCallAt = now + RetrySeconds;
        }

        // A result only counts for the call that is in flight in the phase that made it.
        bool Answer(PairPhase expected)
        {
            if (Phase != expected || !inFlight) return false;
            inFlight = false;
            Failures = 0;
            return true;
        }

        void Watch(float now)
        {
            Phase = PairPhase.Paired;
            misses = 0;
            nextCallAt = now + WatchSeconds;
        }
    }

    public static class PairingText
    {
        /// "4821" as "4 8 2 1", so the digits read apart at a distance.
        public static string Spaced(string code) => code == null ? "" : string.Join(" ", code.ToCharArray());

        /// The address as the person types it: no scheme, no trailing slash.
        public static string TypedAddress(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            var start = url.IndexOf("://", StringComparison.Ordinal);
            return (start >= 0 ? url.Substring(start + 3) : url).TrimEnd('/');
        }
    }
}
