using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Edith.Ask;
using UnityEngine;

namespace Edith.Navigation
{
    /// What GET /navigate answers. The backend leaves out what it does not know, so those fields keep -1 here.
    [Serializable]
    public sealed class NavStatus
    {
        public bool active, arrived;
        public string name = "", kind = "", compass = "";
        public float distance_m = -1f, bearing_deg = -1f, fix_age_s = -1f, accuracy_m = -1f;
        public float heading_deg = -1f, heading_accuracy_deg = -1f, course_deg = -1f, speed_mps = -1f;

        public bool HasFix => distance_m >= 0f && bearing_deg >= 0f && fix_age_s >= 0f;

        /// Null when `json` is not a status.
        public static NavStatus Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                return JsonUtility.FromJson<NavStatus>(json);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }

    /// The backend's /navigate endpoints. Every call runs off the main thread and returns JSON for Gemini or the arrow.
    public static class NavClient
    {
        public const string FindPlaceTool = "find_place";
        public const string StopTool = "stop_navigation";

        /// A little longer than the backend's own eight-second cap on a search.
        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        /// The JSON object Gemini gets when a call could not be done, with a reason it can say.
        public static string Problem(string reason) => "{\"found\":false,\"reason\":" + LiveRequest.Quote(reason) + "}";

        /// The backend's answer as Gemini gets it: the target and the other matches, or why there are none.
        public static async Task<string> FindAsync(string backendUrl, string deviceToken, string profileId,
            string category, string name, CancellationToken token)
        {
            category = string.IsNullOrWhiteSpace(category) ? "any" : category.Trim();
            var body = "{\"category\":" + LiveRequest.Quote(category) +
                       (string.IsNullOrWhiteSpace(name) ? "" : ",\"name\":" + LiveRequest.Quote(name.Trim())) + "}";
            var (status, reply) = await Send(HttpMethod.Post, backendUrl, deviceToken, profileId, body, token)
                .ConfigureAwait(false);
            if (status >= 200 && status < 300) return reply;
            Debug.Log($"[EDITH nav] /navigate answered {status}: {Clip(reply)}");
            return Problem(status switch
            {
                404 => "Nothing like that was found within about five kilometres.",
                409 => "The phone has not shared a recent position, so EDITH does not know where the person is.",
                422 => "That kind of place is not one EDITH can search for.",
                _ => "The map search did not answer. Try again in a moment.",
            });
        }

        public static async Task<string> StopAsync(string backendUrl, string deviceToken, string profileId,
            CancellationToken token)
        {
            var (status, _) = await Send(HttpMethod.Delete, backendUrl, deviceToken, profileId, null, token)
                .ConfigureAwait(false);
            return status >= 200 && status < 300 ? "{\"stopped\":true}" : Problem("The directions could not be stopped.");
        }

        /// The current target, or null when the backend could not be asked.
        public static async Task<NavStatus> StatusAsync(string backendUrl, string deviceToken, string profileId,
            CancellationToken token)
        {
            var (status, reply) = await Send(HttpMethod.Get, backendUrl, deviceToken, profileId, null, token)
                .ConfigureAwait(false);
            return status == 200 ? NavStatus.Parse(reply) : null;
        }

        static async Task<(int status, string body)> Send(HttpMethod method, string backendUrl, string deviceToken,
            string profileId, string body, CancellationToken token)
        {
            using var request = new HttpRequestMessage(method, backendUrl + "/navigate");
            if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            if (!string.IsNullOrEmpty(deviceToken)) request.Headers.Add("X-Edith-Token", deviceToken);
            request.Headers.Add("X-Edith-Profile", profileId);
            using var response = await Http.SendAsync(request, token).ConfigureAwait(false);
            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        }

        static string Clip(string text) => text == null || text.Length <= 200 ? text : text.Substring(0, 200);
    }
}
