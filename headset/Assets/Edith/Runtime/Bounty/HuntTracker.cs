using System;
using System.Collections.Generic;

namespace Edith.Bounty
{
    public enum HuntPhase { None, Active, Ended }

    public enum HuntEnd { FoundAll, Stopped, Timeout }

    /// The headset's view of the profile's hunt: none, active, or ended with a reason. The countdown runs from the
    /// seconds the backend said were left, against `clock` read when that state arrived, so the headset's wall clock
    /// never enters into it. The owner calls Update every frame.
    public sealed class HuntTracker
    {
        /// The last seconds that tick, one tick each time the countdown passes one of these.
        public static readonly int[] TickSeconds = { 30, 20, 10 };

        readonly Func<float> clock;
        readonly List<HuntTarget> targets = new List<HuntTarget>();
        readonly bool[] ticked = new bool[TickSeconds.Length];
        float remainingAtReceipt, receivedAt, endedAt;

        public HuntTracker(Func<float> clock) => this.clock = clock;

        public HuntPhase Phase { get; private set; }
        /// Why the hunt ended; meaningful only while Phase is Ended.
        public HuntEnd Ending { get; private set; }
        public string HuntRef { get; private set; } = "";
        public IReadOnlyList<HuntTarget> Targets => targets;

        /// A hunt began: first learned of while none was active, or with a new ref.
        public event Action Started;
        /// A target was found and the hunt goes on. The find that completes the hunt raises Ended instead.
        public event Action<int> TargetFound;
        /// The countdown passed 30, 20 or 10 seconds left; the argument is that mark.
        public event Action<int> Ticked;
        public event Action<HuntEnd> Ended;

        public int Found
        {
            get
            {
                var found = 0;
                foreach (var target in targets)
                    if (target.Found) found++;
                return found;
            }
        }

        public float Remaining =>
            Phase == HuntPhase.Active ? Math.Max(0f, remainingAtReceipt - (clock() - receivedAt)) : 0f;

        /// Seconds since the hunt ended; 0 while it has not.
        public float SinceEnded => Phase == HuntPhase.Ended ? clock() - endedAt : 0f;

        public string TextOf(int index)
        {
            foreach (var target in targets)
                if (target.Index == index) return target.Text;
            return null;
        }

        /// Takes the backend's word on the hunt. The same hunt again keeps its ticks and raises TargetFound for each
        /// target it newly shows found.
        public void Apply(HuntState state)
        {
            var now = clock();
            var same = state.HuntRef == HuntRef && Phase != HuntPhase.None;
            var wasEnded = Phase == HuntPhase.Ended && same;
            var previouslyFound = new HashSet<int>();
            if (same)
                foreach (var target in targets)
                    if (target.Found) previouslyFound.Add(target.Index);

            HuntRef = state.HuntRef;
            targets.Clear();
            targets.AddRange(state.Targets);

            if (state.Status == HuntStatus.Active)
            {
                remainingAtReceipt = state.RemainingSeconds;
                receivedAt = now;
                var wasActive = same && Phase == HuntPhase.Active;
                Phase = HuntPhase.Active;
                for (var i = 0; i < ticked.Length; i++)
                    ticked[i] = (wasActive && ticked[i]) || state.RemainingSeconds < TickSeconds[i];
                if (!wasActive)
                {
                    Started?.Invoke();
                    return;
                }
                foreach (var target in targets)
                    if (target.Found && !previouslyFound.Contains(target.Index)) TargetFound?.Invoke(target.Index);
                return;
            }
            if (wasEnded) return;
            Finish(state.Status == HuntStatus.FoundAll ? HuntEnd.FoundAll
                : state.Status == HuntStatus.Stopped ? HuntEnd.Stopped : HuntEnd.Timeout);
        }

        /// A Look ticked target `index`; `completes` is the Look event's found_all. Ignored when no hunt is active or
        /// the target is already found.
        public void MarkFound(int index, bool completes)
        {
            if (Phase != HuntPhase.Active) return;
            var at = targets.FindIndex(target => target.Index == index);
            if (at < 0 || targets[at].Found) return;
            targets[at] = targets[at].AsFound();
            if (completes || Found == targets.Count) Finish(HuntEnd.FoundAll);
            else TargetFound?.Invoke(index);
        }

        /// The backend says no hunt is active, so one this headset thought was running is over.
        public void NoHunt()
        {
            if (Phase == HuntPhase.Active) Finish(Remaining <= 0f ? HuntEnd.Timeout : HuntEnd.Stopped);
        }

        public void Update()
        {
            if (Phase != HuntPhase.Active) return;
            var remaining = Remaining;
            for (var i = 0; i < TickSeconds.Length; i++)
            {
                if (ticked[i] || remaining > TickSeconds[i]) continue;
                ticked[i] = true;
                if (remaining > 0f) Ticked?.Invoke(TickSeconds[i]);
            }
            if (remaining <= 0f) Finish(HuntEnd.Timeout);
        }

        void Finish(HuntEnd reason)
        {
            Phase = HuntPhase.Ended;
            Ending = reason;
            endedAt = clock();
            Ended?.Invoke(reason);
        }
    }
}
