using System.Collections.Generic;
using System.Threading.Tasks;
using Edith.Ask;
using Edith.Backend;
using Edith.Look;
using Edith.Scan;
using UnityEngine;

namespace Edith.Bounty
{
    /// Bounty on the headset: the profile's hunt, its card and its sounds. EDITH starts and stops a hunt through a
    /// tool call in Ask, which runs it here; a Look that ticks a target reports it here; both leave the tracker in
    /// step with the backend's own record, which the app also reads at launch. The card shows while a hunt runs and
    /// for a few seconds after it ends.
    public sealed class BountyController : MonoBehaviour
    {
        enum Call { Start, Stop, Get }

        const float ResultSeconds = 6f;

        readonly List<(Call call, Task<HuntReply> reply)> calls = new List<(Call, Task<HuntReply>)>();
        HuntTracker tracker;
        HuntClient client;
        HuntCard card;

        public HuntTracker Tracker => tracker;

        /// Puts Bounty into the scene after it loads, beside the controllers it listens to. A scene without Ask has no
        /// hunt to run.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var ask = FindAnyObjectByType<AskController>();
            if (ask == null) return;
            var bounty = new GameObject("Bounty").AddComponent<BountyController>();
            bounty.Setup(ask, FindAnyObjectByType<LookController>(), FindAnyObjectByType<ScanController>());
        }

        void Setup(AskController ask, LookController look, ScanController scan)
        {
            tracker = new HuntTracker(() => Time.realtimeSinceStartup);
            tracker.TargetFound += _ => BountySounds.Play(BountyCue.Find);
            tracker.Ticked += _ => BountySounds.Play(BountyCue.Tick);
            tracker.Ended += OnEnded;
            var settings = BackendSettings.Load();
            if (settings != null) client = new HuntClient(settings.Url, settings.DeviceToken, BackendSettings.ProfileId);
            card = HuntCard.Create(ask.Eye, ask.Font, ask.Material);
            ask.BindBounty(this);
            if (look != null) look.BindBounty(this);
            if (scan != null) scan.BindBounty(tracker);
        }

        void Start() => Refresh();

        void OnDestroy()
        {
            if (card != null) Destroy(card.gameObject);
        }

        static void OnEnded(HuntEnd reason)
        {
            if (reason == HuntEnd.FoundAll) BountySounds.Play(BountyCue.Fanfare);
            else if (reason == HuntEnd.Timeout) BountySounds.Play(BountyCue.TimeUp);
        }

        /// Starts a hunt, as EDITH's `start_hunt` call asks. The task never faults. Null when the backend is not set up.
        public Task<HuntReply> StartHunt(IReadOnlyList<string> interests) => client == null ? null : Track(Call.Start, client.Start(interests));

        /// Stops the hunt, as EDITH's `stop_hunt` call asks. Null when the backend is not set up.
        public Task<HuntReply> StopHunt() => client == null ? null : Track(Call.Stop, client.Stop());

        /// Reads the backend's hunt, which settles what this headset believes.
        public void Refresh()
        {
            if (client != null) Track(Call.Get, client.Get());
        }

        /// A Look ticked a target. The headset ticks it at once and then reads the backend's hunt to confirm.
        public void OnLookTick(in LookEvent e)
        {
            tracker.MarkFound(e.HuntIndex, e.HuntStatus == "found_all");
            if (tracker.Phase == HuntPhase.Active) Refresh();
        }

        Task<HuntReply> Track(Call call, Task<HuntReply> reply)
        {
            calls.Add((call, reply));
            return reply;
        }

        void Update()
        {
            for (var i = calls.Count - 1; i >= 0; i--)
            {
                var (call, reply) = calls[i];
                if (!reply.IsCompleted) continue;
                calls.RemoveAt(i);
                Take(call, reply.Result);
            }
            tracker.Update();
            var show = tracker.Phase == HuntPhase.Active || (tracker.Phase == HuntPhase.Ended && tracker.SinceEnded < ResultSeconds);
            if (show && !card.Visible) card.Snap();
            card.SetVisible(show);
            if (show) card.Show(tracker);
        }

        void Take(Call call, HuntReply reply)
        {
            if (reply.State is { } state)
            {
                tracker.Apply(state);
                return;
            }
            if (reply.NoHunt)
            {
                tracker.NoHunt();
                return;
            }
            // A hunt this headset knows of stays as it was; the next find or launch reads it again.
            Debug.Log($"[EDITH bounty] {call} answered {reply.Status}: {reply.Body}");
        }
    }
}
