using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LidarrLens.Domain;

namespace LidarrLens.Infrastructure;

public sealed class LidarrClient(HttpClient httpClient, string apiKey) : ILidarrClient
{
    private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    public async Task<IReadOnlyList<LidarrArtist>> GetArtistsAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/artist"); request.Headers.Add("X-Api-Key", apiKey);
        using var response = await httpClient.SendAsync(request, cancellationToken); response.EnsureSuccessStatusCode();
        var items = await response.Content.ReadFromJsonAsync<List<ArtistDto>>(_options, cancellationToken) ?? [];
        return items.Select(x => new LidarrArtist(x.Id, x.ArtistName ?? "Unknown", x.MbId ?? x.ForeignArtistId, x.ForeignArtistId)).ToList();
    }

    public async Task<IReadOnlyList<LidarrAlbumSnapshot>> GetAlbumsAsync(int artistId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/album?artistId={artistId}"); request.Headers.Add("X-Api-Key", apiKey);
        using var response = await httpClient.SendAsync(request, cancellationToken); response.EnsureSuccessStatusCode();
        var items = await response.Content.ReadFromJsonAsync<List<AlbumDto>>(_options, cancellationToken) ?? [];
        return items.Select(x => new LidarrAlbumSnapshot(x.Id, x.Title ?? "Untitled", x.ForeignAlbumId, x.ReleaseDate, x.AlbumType ?? "unknown", (x.Media ?? []).SelectMany(m => m.Tracks ?? []).Select(t => new LidarrTrackSnapshot(t.Title ?? "Untitled", t.Duration ?? 0)).ToList(), (x.Releases ?? []).Select(r => new LidarrReleaseSnapshot(r.ForeignReleaseId, r.Title ?? x.Title ?? "Untitled", r.TrackCount, r.Duration / 1000, r.Country?.FirstOrDefault(), r.Label?.FirstOrDefault(), r.Format)).ToList())).ToList();
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api"); request.Headers.Add("X-Api-Key", apiKey); using var response = await httpClient.SendAsync(request, cancellationToken); return response.IsSuccessStatusCode;
    }

    private sealed record ArtistDto(int Id, string? ArtistName, string? ForeignArtistId, string? MbId);
    private sealed record AlbumDto(int Id, string? Title, string? ForeignAlbumId, DateTimeOffset? ReleaseDate, string? AlbumType, List<MediaDto>? Media, List<ReleaseDto>? Releases);
    private sealed record MediaDto(List<TrackDto>? Tracks);
    private sealed record TrackDto(string? Title, int? Duration);
    private sealed record ReleaseDto(string? ForeignReleaseId, string? Title, int TrackCount, int Duration, List<string>? Country, List<string>? Label, string? Format);
}
