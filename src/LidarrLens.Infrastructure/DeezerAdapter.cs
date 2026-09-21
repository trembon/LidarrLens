using System.Text.Json;
using LidarrLens.Domain;

namespace LidarrLens.Infrastructure;

public sealed class DeezerAdapter(HttpClient httpClient, IResponseCache cache, string userAgent = "LidarrLens/0.1") : IMetadataSourceAdapter
{
    private readonly ProviderHttpClient _client = new(httpClient, cache, "deezer", userAgent, TimeSpan.FromMilliseconds(500));
    public string Name => "Deezer";

    public async Task<IReadOnlyList<SourceRelease>> GetArtistReleasesAsync(ArtistSnapshot artist, CancellationToken cancellationToken)
    {
        var artistId = await FindArtistIdAsync(artist, cancellationToken);
        if (artistId is null) return [];
        var releases = new List<SourceRelease>();
        for (var index = 0; ; index += 25)
        {
            using var page = await _client.GetAsync($"https://api.deezer.com/artist/{artistId}/albums?index={index}&limit=25", TimeSpan.FromDays(7), cancellationToken);
            if (!page.RootElement.TryGetProperty("data", out var data)) break;
            var albums = data.EnumerateArray().ToList(); if (albums.Count == 0) break;
            foreach (var album in albums)
            {
                var id = GetString(album, "id"); if (id is null) continue;
                using var detail = await _client.GetAsync($"https://api.deezer.com/album/{id}", TimeSpan.FromDays(7), cancellationToken);
                releases.Add(ParseAlbum(detail.RootElement, artistId));
            }
            if (albums.Count < 25) break;
        }
        return releases;
    }

    private async Task<string?> FindArtistIdAsync(ArtistSnapshot artist, CancellationToken cancellationToken)
    {
        using var search = await _client.GetAsync($"https://api.deezer.com/search/artist?q={Uri.EscapeDataString(artist.Name)}&limit=10", TimeSpan.FromDays(7), cancellationToken);
        if (!search.RootElement.TryGetProperty("data", out var data)) return null;
        var candidates = data.EnumerateArray().ToList();
        return candidates.FirstOrDefault(x => string.Equals(GetString(x, "name"), artist.Name, StringComparison.OrdinalIgnoreCase)) is var exact && exact.ValueKind == JsonValueKind.Object ? GetString(exact, "id") : candidates.Select(x => GetString(x, "id")).FirstOrDefault(x => x is not null);
    }

    private static SourceRelease ParseAlbum(JsonElement value, string artistId)
    {
        var tracks = value.TryGetProperty("tracks", out var wrapper) && wrapper.TryGetProperty("data", out var data) ? data.EnumerateArray().Select(x => new SourceTrack(GetString(x, "title") ?? "Untitled", x.TryGetProperty("duration", out var duration) ? duration.GetInt32() : null, GetString(x, "isrc"))).ToList() : [];
        var artworkUrl = GetString(value, "cover_xl") ?? GetString(value, "cover_big");
        return new SourceRelease("Deezer", GetString(value, "id") ?? Guid.NewGuid().ToString(), GetString(value, "title") ?? "Untitled", GetString(value, "link") ?? "https://www.deezer.com", artistId, DateOnly.TryParse(GetString(value, "release_date"), out var date) ? date : null, null, null, null, GetString(value, "upc"), "Digital", tracks, artworkUrl);
    }

    private static string? GetString(JsonElement value, string property) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var p) && p.ValueKind is JsonValueKind.String or JsonValueKind.Number ? p.ToString() : null;
}
