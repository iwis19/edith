using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Edith.Look;

namespace Edith.Scrapbook
{
    /// The headset's reads of collected memories: GET /memories and GET /memories/{id}/image. Both throw
    /// HttpRequestException when the backend cannot be reached or refuses, and OperationCanceledException when the
    /// token fires.
    public sealed class ScrapbookClient
    {
        /// The most memories one Scrapbook holds.
        public const int Limit = 200;

        readonly string backendUrl, deviceToken, profileId;

        public ScrapbookClient(string backendUrl, string deviceToken, string profileId)
        {
            this.backendUrl = backendUrl;
            this.deviceToken = deviceToken;
            this.profileId = profileId;
        }

        /// Newest first. Also throws FormatException when the answer is not a memory list.
        public async Task<IReadOnlyList<Memory>> List(CancellationToken cancel)
        {
            using var response = await Get($"/memories?limit={Limit}", cancel).ConfigureAwait(false);
            return MemoryList.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        }

        /// The memory's photo as a JPEG.
        public async Task<byte[]> Image(int id, CancellationToken cancel)
        {
            using var response = await Get($"/memories/{id}/image", cancel).ConfigureAwait(false);
            return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }

        async Task<HttpResponseMessage> Get(string path, CancellationToken cancel)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, backendUrl + path);
            if (!string.IsNullOrEmpty(deviceToken)) request.Headers.Add("X-Edith-Token", deviceToken);
            request.Headers.Add("X-Edith-Profile", profileId);
            var response = await LookClient.Http.SendAsync(request, cancel).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) return response;
            var status = (int)response.StatusCode;
            response.Dispose();
            throw new HttpRequestException($"{path} answered {status}");
        }
    }
}
