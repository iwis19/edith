using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Edith.Pairing
{
    [Serializable]
    public sealed class PairIssued
    {
        public string code;
        public int expires_in_s;
        public string phone_url;
    }

    [Serializable]
    public sealed class PairStatus
    {
        public bool paired;
        public bool expired;
    }

    [Serializable]
    public sealed class PhoneSummary
    {
        public string place;
    }

    /// The headset's three pairing calls to the backend. HttpClient rather than UnityWebRequest, which buffers
    /// responses on Quest.
    public sealed class PairingClient
    {
        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        readonly string backendUrl;
        readonly string deviceToken;
        readonly string profileId;

        public PairingClient(string backendUrl, string deviceToken, string profileId)
        {
            this.backendUrl = backendUrl;
            this.deviceToken = deviceToken;
            this.profileId = profileId;
        }

        /// Whether the backend already keeps a phone position for this profile.
        public async Task<bool> HasLocationAsync(CancellationToken token)
        {
            using var response = await Send(HttpMethod.Get, "/location", token);
            if (response.StatusCode == HttpStatusCode.NotFound) return false;
            await Require(response);
            return true;
        }

        /// The place the backend resolved from the phone's position, or null when none is kept or it is still being found.
        public async Task<string> PlaceAsync(CancellationToken token)
        {
            using var response = await Send(HttpMethod.Get, "/location", token);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            var place = JsonUtility.FromJson<PhoneSummary>(await Require(response))?.place;
            return string.IsNullOrEmpty(place) ? null : place;
        }

        public async Task<PairIssued> RequestCodeAsync(CancellationToken token)
        {
            using var response = await Send(HttpMethod.Post, "/pair", token);
            var issued = ParseIssued(await Require(response));
            return issued ?? throw new InvalidOperationException("/pair sent no code");
        }

        public async Task<PairStatus> StatusAsync(string code, CancellationToken token)
        {
            using var response = await Send(HttpMethod.Get, "/pair/" + Uri.EscapeDataString(code), token);
            return JsonUtility.FromJson<PairStatus>(await Require(response)) ?? throw new InvalidOperationException("/pair status was empty");
        }

        /// The issued code, or null when the reply holds none.
        public static PairIssued ParseIssued(string json)
        {
            var issued = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<PairIssued>(json);
            return string.IsNullOrEmpty(issued?.code) ? null : issued;
        }

        Task<HttpResponseMessage> Send(HttpMethod method, string path, CancellationToken token)
        {
            var request = new HttpRequestMessage(method, backendUrl + path);
            if (method == HttpMethod.Post) request.Content = new StringContent("", Encoding.UTF8, "application/json");
            if (!string.IsNullOrEmpty(deviceToken)) request.Headers.Add("X-Edith-Token", deviceToken);
            request.Headers.Add("X-Edith-Profile", profileId);
            return Http.SendAsync(request, token);
        }

        static async Task<string> Require(HttpResponseMessage response)
        {
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"answered {(int)response.StatusCode}: {(body.Length <= 200 ? body : body.Substring(0, 200))}");
            return body;
        }
    }
}
