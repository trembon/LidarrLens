using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using LidarrLens.Domain;

namespace LidarrLens.Infrastructure;

internal sealed class ProviderHttpClient(HttpClient httpClient, IResponseCache cache, string provider, string userAgent, TimeSpan minimumInterval)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<JsonDocument> GetAsync(string url, TimeSpan cacheTtl, CancellationToken cancellationToken)
    {
        var cached = await cache.GetAsync(provider, url, cancellationToken);
        if (cached is not null) return JsonDocument.Parse(cached);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                var wait = minimumInterval - (DateTimeOffset.UtcNow - _lastRequest);
                if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellationToken);
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.Clear(); request.Headers.UserAgent.Add(ProductInfoHeaderValue.Parse(userAgent));
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                _lastRequest = DateTimeOffset.UtcNow;
                if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                {
                    if (attempt == 3) response.EnsureSuccessStatusCode();
                    await Task.Delay(TimeSpan.FromMilliseconds(400 * Math.Pow(2, attempt)), cancellationToken);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                await cache.SetAsync(provider, url, content, cacheTtl, cancellationToken);
                return JsonDocument.Parse(content);
            }
            finally { _gate.Release(); }
        }
        throw new HttpRequestException($"Unable to fetch {url}");
    }
}
