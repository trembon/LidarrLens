using LidarrLens.Application;
using LidarrLens.Domain;
using Xunit;

namespace LidarrLens.Tests;

public sealed class MatchingTests
{
    [Fact]
    public void Missing_musicbrainz_group_is_reported_when_lidarr_has_no_group_id()
    {
        var artist = new ArtistSnapshot("Example", "artist-id", "https://musicbrainz.org/artist/artist-id");
        var group = new MusicBrainzReleaseGroupSnapshot("group-id", "New EP", "EP", [], new DateOnly(2025, 1, 1), [], "https://musicbrainz.org/release-group/group-id");
        var result = new MatchingEngine().Compare(artist, [], [group], [], "scan");
        Assert.Contains(result, x => x.Type == FindingType.MissingReleaseGroup && x.Status == FindingStatus.Pending);
    }

    [Fact]
    public void Matching_source_tracks_are_reported_when_recording_is_missing()
    {
        var artist = new ArtistSnapshot("Example", "artist-id", "https://musicbrainz.org/artist/artist-id");
        var release = new MusicBrainzReleaseSnapshot("release-id", "Album", "group-id", null, null, null, null, null, null, [new MusicBrainzTrack("Known", 180000, "recording-id", null)], []);
        var group = new MusicBrainzReleaseGroupSnapshot("group-id", "Album", "Album", [], null, [release], "https://musicbrainz.org/release-group/group-id");
        var source = new SourceRelease("Deezer", "dz-id", "Album", "https://deezer.com/album/dz-id", "dz-artist", null, null, null, null, null, "Digital", [new SourceTrack("Known", 180), new SourceTrack("Missing", 200)]);
        var result = new MatchingEngine().Compare(artist, [], [group], [source], "scan");
        Assert.Contains(result, x => x.Type == FindingType.IncompleteTracklist);
    }

    [Fact]
    public void High_level_group_match_does_not_report_tracklist_findings()
    {
        var artist = new ArtistSnapshot("Example", "artist-id", "https://musicbrainz.org/artist/artist-id");
        var group = new MusicBrainzReleaseGroupSnapshot("group-id", "Album", "Album", [], null, [], "https://musicbrainz.org/release-group/group-id", false);
        var source = new SourceRelease("Deezer", "dz-id", "Album", "https://deezer.com/album/dz-id", "dz-artist", null, null, null, null, null, "Digital", [new SourceTrack("Unknown", 200)]);

        var result = new MatchingEngine().Compare(artist, [], [group], [source], "scan");

        Assert.DoesNotContain(result, x => x.Type is FindingType.IncompleteTracklist or FindingType.MissingRecording or FindingType.MissingRelease);
    }
}
