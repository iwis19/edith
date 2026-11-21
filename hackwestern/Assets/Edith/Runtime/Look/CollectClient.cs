using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Edith.Backend;
using UnityEngine;

namespace Edith.Look
{
    /// One POST /collect, which saves a finished Look's photo on the backend as a memory. Nothing waits on it: the
    /// outcome is logged, and a failure is not retried.
    public static class CollectClient
    {
        const float TimeoutSeconds = 10f;

        /// Why `card` should not be collected, or null when it should.
        public static string SkipReason(LookCard card)
        {
            if (string.IsNullOrEmpty(card.LookRef)) return "the backend never accepted the look";
            if (card.Phase == LookPhase.Failed) return "the look failed";
            return null;
        }

        public static Task Send(string lookRef, byte[] jpeg)
        {
            var settings = BackendSettings.Load();
            if (settings == null)
            {
                Debug.Log("[EDITH collect] skipped, the backend is not set up");
                return Task.CompletedTask;
            }
            return Send(settings.Url, settings.DeviceToken, BackendSettings.ProfileId, lookRef, jpeg);
        }

        public static Task Send(string url, string deviceToken, string profileId, string lookRef, byte[] jpeg) =>
            Task.Run(() => Run(url, deviceToken, profileId, lookRef, jpeg));

        static async Task Run(string url, string token, string profile, string lookRef, byte[] jpeg)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
            try
            {
                using var request = LookClient.Build(url + "/collect", token, profile, jpeg, $"{{\"look_ref\":\"{lookRef}\"}}");
                using var response = await LookClient.Http.SendAsync(request, timeout.Token).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (response.IsSuccessStatusCode) Debug.Log($"[EDITH collect] look {lookRef} saved: {body}");
                else Debug.Log($"[EDITH collect] look {lookRef} failed with http_{(int)response.StatusCode}: {body}");
            }
            catch (Exception e)
            {
                Debug.Log($"[EDITH collect] look {lookRef} failed: {(timeout.IsCancellationRequested ? "timeout" : e.GetBaseException().Message)}");
            }
        }
    }
}
