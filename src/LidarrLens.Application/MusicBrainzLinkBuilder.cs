using LidarrLens.Domain;

namespace LidarrLens.Application;

public static class MusicBrainzLinkBuilder
{
    public static IReadOnlyList<MusicBrainzLink> Build(AuditFinding finding)
    {
        if (finding.MusicBrainzLinks is { Count: > 0 })
        {
            var addReleaseUrl = BuildAddReleaseUrl(finding.ArtistMusicBrainzId);
            return finding.MusicBrainzLinks
                .Select(link => string.Equals(link.Label, "Add release", StringComparison.OrdinalIgnoreCase)
                    ? new MusicBrainzLink(link.Label, addReleaseUrl)
                    : link)
                .ToList();
        }

        return Build(
            new ArtistSnapshot(finding.ArtistName, finding.ArtistMusicBrainzId, finding.ArtistMusicBrainzUrl),
            finding.ExistingMusicBrainzEntities,
            finding.CopyReady);
    }

    public static string BuildAddReleaseUrl(string artistMusicBrainzId) =>
        $"https://musicbrainz.org/release/add?artist={Uri.EscapeDataString(artistMusicBrainzId)}";

    public static IReadOnlyList<MusicBrainzLink> Build(ArtistSnapshot artist, IEnumerable<string> existing, CopyReadyRelease? copy)
    {
        var links = new List<MusicBrainzLink> { new("Artist", artist.MusicBrainzUrl) };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { artist.MusicBrainzUrl };

        foreach (var url in existing.Concat(copy?.SourceUrls ?? []).Where(x => x.StartsWith("https://musicbrainz.org/", StringComparison.OrdinalIgnoreCase)))
        {
            if (!seen.Add(url)) continue;
            var label = url.Contains("/release-group/", StringComparison.OrdinalIgnoreCase)
                ? "Release group"
                : url.Contains("/release/", StringComparison.OrdinalIgnoreCase)
                    ? "Release"
                    : "MusicBrainz entity";
            links.Add(new MusicBrainzLink(label, url));
        }

        if (!string.IsNullOrWhiteSpace(copy?.MusicBrainzEditorUrl))
        {
            var editorUrl = copy.MusicBrainzEditorUrl.Contains("/edit", StringComparison.OrdinalIgnoreCase)
                ? copy.MusicBrainzEditorUrl
                : BuildAddReleaseUrl(artist.MusicBrainzId);
            if (seen.Add(editorUrl))
            {
                var label = editorUrl.Contains("/edit", StringComparison.OrdinalIgnoreCase) ? "Edit release" : "Add release";
                links.Add(new MusicBrainzLink(label, editorUrl));
            }
        }

        return links;
    }
}
