using System.Text.Json;
using LidarrLens.Domain;

namespace LidarrLens.Infrastructure;

public sealed class MusicBrainzClient(HttpClient httpClient, IResponseCache cache, string userAgent, bool includeReleaseDetails = false) : IMusicBrainzClient
{
    private readonly ProviderHttpClient _client = new(httpClient, cache, "musicbrainz", userAgent, TimeSpan.FromSeconds(1));

    public async Task<IReadOnlyList<MusicBrainzReleaseGroupSnapshot>> GetReleaseGroupsAsync(string artistId, CancellationToken cancellationToken)
    {
        var groups = new List<MusicBrainzReleaseGroupSnapshot>();
        for (var offset = 0; ; offset += 100)
        {
            using var document = await _client.GetAsync($"https://musicbrainz.org/ws/2/release-group?artist={Uri.EscapeDataString(artistId)}&limit=100&offset={offset}&fmt=json", TimeSpan.FromDays(1), cancellationToken);
            var list = document.RootElement.TryGetProperty("release-groups", out var values) ? values.EnumerateArray().ToList() : [];
            foreach (var group in list)
            {
                var id = GetString(group, "id"); if (id is null) continue;
                var releases = includeReleaseDetails ? await GetReleasesAsync(id, cancellationToken) : [];
                groups.Add(new MusicBrainzReleaseGroupSnapshot(id, GetString(group, "title") ?? "Untitled", GetString(group, "primary-type") ?? "other", group.TryGetProperty("secondary-types", out var secondary) ? secondary.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : [], ParseDate(GetString(group, "first-release-date")), releases, $"https://musicbrainz.org/release-group/{id}", includeReleaseDetails));
            }
            if (list.Count < 100) break;
        }
        return groups;
    }

    private async Task<IReadOnlyList<MusicBrainzReleaseSnapshot>> GetReleasesAsync(string groupId, CancellationToken cancellationToken)
    {
        var releases = new List<MusicBrainzReleaseSnapshot>();
        for (var offset = 0; ; offset += 100)
        {
            using var document = await _client.GetAsync($"https://musicbrainz.org/ws/2/release?release-group={Uri.EscapeDataString(groupId)}&limit=100&offset={offset}&fmt=json&inc=recordings+artist-credits+labels+media+isrcs+url-rels", TimeSpan.FromDays(1), cancellationToken);
            var list = document.RootElement.TryGetProperty("releases", out var values) ? values.EnumerateArray().ToList() : [];
            releases.AddRange(list.Select(x => ParseRelease(x, groupId)));
            if (list.Count < 100) break;
        }
        return releases;
    }

    private static MusicBrainzReleaseSnapshot ParseRelease(JsonElement value, string groupId)
    {
        var tracks = new List<MusicBrainzTrack>();
        if (value.TryGetProperty("media", out var media)) foreach (var medium in media.EnumerateArray()) if (medium.TryGetProperty("tracks", out var mediumTracks)) foreach (var track in mediumTracks.EnumerateArray())
        {
            var recording = track.TryGetProperty("recording", out var r) ? r : default;
            tracks.Add(new MusicBrainzTrack(GetString(track, "title") ?? GetString(recording, "title") ?? "Untitled", track.TryGetProperty("length", out var length) && length.ValueKind == JsonValueKind.Number ? length.GetInt32() : null, GetString(recording, "id"), recording.TryGetProperty("isrcs", out var isrcs) ? isrcs.EnumerateArray().Select(x => x.GetString()).FirstOrDefault(x => x is not null) : null));
        }
        var urls = value.TryGetProperty("relations", out var relations) ? relations.EnumerateArray().Select(x => x.TryGetProperty("url", out var url) ? GetString(url, "resource") : null).Where(x => x is not null).Cast<string>().Distinct().ToList() : [];
        return new MusicBrainzReleaseSnapshot(GetString(value, "id") ?? Guid.NewGuid().ToString(), GetString(value, "title") ?? "Untitled", groupId, ParseDate(GetString(value, "date")), GetString(value, "country"), value.TryGetProperty("label-info", out var labels) ? labels.EnumerateArray().Select(x => x.TryGetProperty("label", out var l) ? GetString(l, "name") : null).FirstOrDefault(x => x is not null) : null, value.TryGetProperty("label-info", out var catalogLabels) ? catalogLabels.EnumerateArray().Select(x => GetString(x, "catalog-number")).FirstOrDefault(x => x is not null) : null, GetString(value, "barcode"), value.TryGetProperty("media", out var mediaForFormat) ? mediaForFormat.EnumerateArray().Select(x => GetString(x, "format")).FirstOrDefault(x => x is not null) : null, tracks, urls);
    }

    private static string? GetString(JsonElement value, string property) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    private static DateOnly? ParseDate(string? value) => DateOnly.TryParse(value, out var date) ? date : null;
}
