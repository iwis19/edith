using System;

namespace Edith.Pairing
{
    public enum PairPhase { Checking, Requesting, Showing, Connected, Hidden }

    public enum PairCall { None, CheckLocation, RequestCode, PollStatus }

    public enum PairHideReason { None, HasLocation, TimedOut, Errors, Done }

    /// The pairing panel's life as a function of time and the results of its three backend calls. The driver asks
    /// `Next` what to call, makes at most one call at a time, and hands back what happened. All times are seconds on
    /// one clock.
    public sealed class PairingFlow
    {
        public const float GiveUpSeconds = 180f;
        public const float PollSeconds = 3f;
        public const float RetrySeconds = 10f;
        public const float ConnectedSeconds = 2f;
        public const int MaxRetries = 3;

        readonly float startedAt;
        float nextCallAt;
        float connectedUntil;
        bool inFlight;
        int failures;

        public PairingFlow(float now)
        {
            startedAt = now;
            nextCallAt = now;
        }

        public PairPhase Phase { get; private set; } = PairPhase.Checking;
        public PairHideReason HideReason { get; private set; }
        public string Code { get; private set; }
        public string PhoneUrl { get; private set; }

        /// The panel is on screen: the code is up, or the pairing was just confirmed.
        public bool Visible => Phase == PairPhase.Showing || Phase == PairPhase.Connected;

        /// The call to make now, or None. Asking again before its result comes back returns None.
        public PairCall Next(float now)
        {
            if (Phase == PairPhase.Hidden) return PairCall.None;
            if (Phase == PairPhase.Connected)
            {
                if (now >= connectedUntil) Hide(PairHideReason.Done);
                return PairCall.None;
            }
            if (now - startedAt >= GiveUpSeconds)
            {
                Hide(PairHideReason.TimedOut);
                return PairCall.None;
            }
            if (inFlight || now < nextCallAt) return PairCall.None;
            inFlight = true;
            return Phase switch
            {
                PairPhase.Checking => PairCall.CheckLocation,
                PairPhase.Requesting => PairCall.RequestCode,
                _ => PairCall.PollStatus,
            };
        }

        public void OnLocation(bool hasPhoneLocation, float now)
        {
            if (!Answer(PairPhase.Checking)) return;
            if (hasPhoneLocation)
            {
                Hide(PairHideReason.HasLocation);
                return;
            }
            Phase = PairPhase.Requesting;
            nextCallAt = now;
        }

        public void OnCode(string code, string phoneUrl, float now)
        {
            if (!Answer(PairPhase.Requesting)) return;
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

        /// The call failed. It is tried again after RetrySeconds, up to MaxRetries times in a row.
        public void OnError(float now)
        {
            if (Phase == PairPhase.Hidden || Phase == PairPhase.Connected || !inFlight) return;
            inFlight = false;
            if (++failures > MaxRetries)
            {
                Hide(PairHideReason.Errors);
                return;
            }
            nextCallAt = now + RetrySeconds;
        }

        // A result only counts for the call that is in flight, so a late reply after the panel hid changes nothing.
        bool Answer(PairPhase expected)
        {
            if (Phase != expected || !inFlight) return false;
            inFlight = false;
            failures = 0;
            return true;
        }

        void Hide(PairHideReason reason)
        {
            Phase = PairPhase.Hidden;
            HideReason = reason;
            Code = null;
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
