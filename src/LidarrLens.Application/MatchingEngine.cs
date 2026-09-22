using System.Security.Cryptography;
using System.Text;
using LidarrLens.Domain;

namespace LidarrLens.Application;

public sealed class MatchingEngine : IMatchingEngine
{
    public IReadOnlyList<AuditFinding> Compare(ArtistSnapshot artist, IReadOnlyList<LidarrAlbumSnapshot> lidarrAlbums, IReadOnlyList<MusicBrainzReleaseGroupSnapshot> groups, IReadOnlyList<SourceRelease> sourceReleases, string scanId)
    {
        var findings = new List<AuditFinding>();
        var lidarrGroupIds = lidarrAlbums.Where(x => !string.IsNullOrWhiteSpace(x.MusicBrainzReleaseGroupId)).Select(x => x.MusicBrainzReleaseGroupId!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lidarrTitles = lidarrAlbums.Select(x => Normalization.ReleaseTitle(x.Title)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            if (lidarrGroupIds.Contains(group.Id)) continue;
            var matchingTitle = lidarrTitles.Contains(Normalization.ReleaseTitle(group.Title));
            findings.Add(Create(artist, scanId, matchingTitle ? FindingType.NotInLidarr : FindingType.MissingReleaseGroup,
                "Review the MusicBrainz release group and add or refresh it in Lidarr.", ConfidenceLevel.High,
                [$"MusicBrainz release group: {group.Id}", matchingTitle ? "Title matches a Lidarr album without the same MusicBrainz ID." : "No matching Lidarr release-group ID or title was found."],
                [group.Url, .. group.Releases.Select(x => $"https://musicbrainz.org/release/{x.Id}")], [], [], BuildCopyReady(artist, group, group.Releases.FirstOrDefault(), null), "musicbrainz", group.Id, group.Url));
        }

        foreach (var source in sourceReleases)
        {
            var matchingGroup = groups.FirstOrDefault(g => Normalization.ReleaseTitle(g.Title) == Normalization.ReleaseTitle(source.Title));
            if (matchingGroup is null)
            {
                findings.Add(Create(artist, scanId, FindingType.MissingReleaseGroup,
                    "Search MusicBrainz for the release group before creating anything.", ConfidenceLevel.Medium,
                    [$"{source.Source} title matches artist context but no MusicBrainz release group title matched."], [],
                    MissingFields(source), [], BuildCopyReady(artist, null, null, source), source.Source, source.Id, source.Url));
                continue;
            }

            // High-level scans intentionally do not fetch release/track details. The
            // release group match is still useful, but detail-based findings would be
            // false positives when the tracklist was not inspected.
            if (!matchingGroup.ReleaseDetailsLoaded) continue;

            var allMbTracks = matchingGroup.Releases.SelectMany(r => r.Tracks).ToList();
            var missingTracks = source.Tracks.Where(st => !allMbTracks.Any(mt => Normalization.TrackTitle(mt.Title) == Normalization.TrackTitle(st.Title))).ToList();
            if (missingTracks.Count > 0)
            {
                findings.Add(Create(artist, scanId, FindingType.IncompleteTracklist,
                    "Add or correct the missing MusicBrainz recordings/tracks after verifying the source evidence.", ConfidenceLevel.Medium,
                    [$"Matched MusicBrainz release group: {matchingGroup.Id}", $"{missingTracks.Count} source track(s) were not found in the group tracklists.", $"Missing: {string.Join(", ", missingTracks.Select(x => x.Title))}"],
                    matchingGroup.Releases.Select(x => $"https://musicbrainz.org/release/{x.Id}"), ["recording", "track position"], [], BuildCopyReady(artist, matchingGroup, matchingGroup.Releases.FirstOrDefault(), source), source.Source, source.Id, source.Url));
            }

            var missingRecordings = source.Tracks.Where(st => allMbTracks.Any(mt => Normalization.TrackTitle(mt.Title) == Normalization.TrackTitle(st.Title) && string.IsNullOrWhiteSpace(mt.RecordingId))).ToList();
            if (missingRecordings.Count > 0)
                findings.Add(Create(artist, scanId, FindingType.MissingRecording, "Create or link the missing MusicBrainz recordings after confirming the performance identity.", ConfidenceLevel.Medium, [$"Matched MusicBrainz release group: {matchingGroup.Id}", $"Recording IDs are missing for: {string.Join(", ", missingRecordings.Select(x => x.Title))}"], matchingGroup.Releases.Select(x => $"https://musicbrainz.org/release/{x.Id}"), ["recording MBID"], [], BuildCopyReady(artist, matchingGroup, matchingGroup.Releases.FirstOrDefault(), source), source.Source, source.Id, source.Url));

            if (!string.IsNullOrWhiteSpace(source.Barcode) && !matchingGroup.Releases.Any(r => string.Equals(r.Barcode, source.Barcode, StringComparison.OrdinalIgnoreCase)))
            {
                findings.Add(Create(artist, scanId, FindingType.MissingRelease,
                    "Check whether this is a distinct edition or only another representation of an existing release.", ConfidenceLevel.Low,
                    [$"Source barcode: {source.Barcode}", "The MusicBrainz release group exists, but no release with this barcode was found."],
                    matchingGroup.Releases.Select(x => $"https://musicbrainz.org/release/{x.Id}"), ["country", "label", "format", "barcode"], ["Do not create a second release group solely because the source is different."], BuildCopyReady(artist, matchingGroup, null, source), source.Source, source.Id, source.Url));
            }
        }

        return findings.GroupBy(x => x.Fingerprint).Select(x => x.First()).ToList();
    }

    private static AuditFinding Create(ArtistSnapshot artist, string scanId, FindingType type, string action, ConfidenceLevel confidence, IReadOnlyList<string> evidence, IEnumerable<string> existing, IReadOnlyList<string> missing, IReadOnlyList<string> warnings, CopyReadyRelease? copy, string source, string? sourceId, string? sourceUrl)
    {
        var fingerprint = Fingerprint(artist.MusicBrainzId, type.ToString(), sourceId, copy?.Title, string.Join("|", evidence));
        var now = DateTimeOffset.UtcNow;
        return new AuditFinding(Guid.NewGuid().ToString("N"), fingerprint, artist.Name, artist.MusicBrainzId, artist.MusicBrainzUrl, source, sourceId, sourceUrl, type, action, confidence, evidence, existing.ToList(), missing, warnings, copy, FindingStatus.Pending, now, now, scanId, MusicBrainzLinkBuilder.Build(artist, existing, copy));
    }

    private static string Fingerprint(params string?[] values)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\u001f", values.Select(x => x ?? string.Empty))));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static IReadOnlyList<string> MissingFields(SourceRelease source) =>
        new[] { (source.ReleaseDate is null ? "release date" : null), (string.IsNullOrWhiteSpace(source.Country) ? "country" : null), (string.IsNullOrWhiteSpace(source.Label) ? "label" : null), (string.IsNullOrWhiteSpace(source.Format) ? "format" : null) }.Where(x => x is not null).Cast<string>().ToList();

    private static CopyReadyRelease BuildCopyReady(ArtistSnapshot artist, MusicBrainzReleaseGroupSnapshot? group, MusicBrainzReleaseSnapshot? release, SourceRelease? source)
    {
        var title = release?.Title ?? source?.Title ?? group?.Title ?? "";
        var tracks = (release?.Tracks.Select((x, i) => $"{i + 1}. {x.Title}{(x.DurationMilliseconds.HasValue ? $" ({TimeSpan.FromMilliseconds(x.DurationMilliseconds.Value):m\\:ss})" : "")}") ?? source?.Tracks.Select((x, i) => $"{i + 1}. {x.Title}{(x.DurationSeconds.HasValue ? $" ({TimeSpan.FromSeconds(x.DurationSeconds.Value):m\\:ss})" : "")}") ?? []).ToList();
        var urls = new[] { source?.Url, release is null ? null : $"https://musicbrainz.org/release/{release.Id}", group?.Url }.Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().Distinct().ToList();
        var editor = release is null ? MusicBrainzLinkBuilder.BuildAddReleaseUrl(artist.MusicBrainzId) : $"https://musicbrainz.org/release/{release.Id}/edit";
        var note = $"LidarrLens audit for {artist.Name} / {title}. Evidence: {string.Join(", ", urls)}. Please verify all fields manually before submitting.";
        return new CopyReadyRelease(title, group?.PrimaryType ?? "album", (release?.Date ?? source?.ReleaseDate)?.ToString("yyyy-MM-dd"), release?.Country ?? source?.Country, release?.Label ?? source?.Label, release?.CatalogNumber ?? source?.CatalogNumber, release?.Barcode ?? source?.Barcode, release?.Format ?? source?.Format, null, urls, tracks, note, editor, source?.ArtworkUrl);
    }

}
