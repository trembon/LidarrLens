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

    [Fact]
    public void Featured_artist_suffix_matches_lidarr_album_to_musicbrainz_group()
    {
        var artist = new ArtistSnapshot("Bolaget", "artist-id", "https://musicbrainz.org/artist/artist-id");
        var album = new LidarrAlbumSnapshot(1, "Vin & pengar (feat. William Ahlborg)", null, null, "single", [], []);
        var group = new MusicBrainzReleaseGroupSnapshot("group-id", "Vin & Pengar", "Single", [], new DateOnly(2019, 1, 1), [], "https://musicbrainz.org/release-group/group-id");

        var result = new MatchingEngine().Compare(artist, [album], [group], [], "scan");

        Assert.Contains(result, x => x.Type == FindingType.NotInLidarr);
        Assert.DoesNotContain(result, x => x.Type == FindingType.MissingReleaseGroup);
    }

    [Fact]
    public void Featured_artist_suffix_matches_source_release_to_musicbrainz_group()
    {
        var artist = new ArtistSnapshot("Bolaget", "artist-id", "https://musicbrainz.org/artist/artist-id");
        var album = new LidarrAlbumSnapshot(1, "Vin & Pengar", "group-id", null, "single", [], []);
        var group = new MusicBrainzReleaseGroupSnapshot("group-id", "Vin & Pengar", "Single", [], new DateOnly(2019, 1, 1), [], "https://musicbrainz.org/release-group/group-id", false);
        var source = new SourceRelease("Deezer", "dz-id", "Vin & pengar (feat. William Ahlborg)", "https://deezer.com/album/dz-id", "dz-artist", null, null, null, null, null, "Digital", []);

        var result = new MatchingEngine().Compare(artist, [album], [group], [source], "scan");

        Assert.Empty(result);
    }

    [Fact]
    public void Release_title_matching_remains_exact_after_feature_normalization()
    {
        var artist = new ArtistSnapshot("Example", "artist-id", "https://musicbrainz.org/artist/artist-id");
        var album = new LidarrAlbumSnapshot(1, "Vin & Pengar 2", null, null, "single", [], []);
        var group = new MusicBrainzReleaseGroupSnapshot("group-id", "Vin & Pengar", "Single", [], null, [], "https://musicbrainz.org/release-group/group-id");

        var result = new MatchingEngine().Compare(artist, [album], [group], [], "scan");

        Assert.Contains(result, x => x.Type == FindingType.MissingReleaseGroup);
    }

    [Fact]
    public void Findings_include_labeled_musicbrainz_context_and_editor_links()
    {
        var artist = new ArtistSnapshot("Example", "artist-id", "https://musicbrainz.org/artist/artist-id");
        var release = new MusicBrainzReleaseSnapshot("release-id", "Album", "group-id", null, null, null, null, null, null, [], []);
        var group = new MusicBrainzReleaseGroupSnapshot("group-id", "Album", "Album", [], null, [release], "https://musicbrainz.org/release-group/group-id");

        var finding = Assert.Single(new MatchingEngine().Compare(artist, [], [group], [], "scan"));

        Assert.Equal(
            [
                new MusicBrainzLink("Artist", "https://musicbrainz.org/artist/artist-id"),
                new MusicBrainzLink("Release group", "https://musicbrainz.org/release-group/group-id"),
                new MusicBrainzLink("Release", "https://musicbrainz.org/release/release-id"),
                new MusicBrainzLink("Edit release", "https://musicbrainz.org/release/release-id/edit")
            ],
            finding.MusicBrainzLinks);
    }

    [Fact]
    public void Findings_without_a_release_include_the_musicbrainz_add_link()
    {
        var artist = new ArtistSnapshot("Example", "artist-id", "https://musicbrainz.org/artist/artist-id");
        var source = new SourceRelease("Deezer", "dz-id", "Album", "https://deezer.com/album/dz-id", "dz-artist", null, null, null, null, null, "Digital", []);

        var finding = Assert.Single(new MatchingEngine().Compare(artist, [], [], [source], "scan"));

        Assert.Contains(new MusicBrainzLink("Add release", "https://musicbrainz.org/release/add"), finding.MusicBrainzLinks!);
    }

    [Fact]
    public void Legacy_findings_without_persisted_links_can_still_build_shortcuts()
    {
        var artist = new ArtistSnapshot("Example", "artist-id", "https://musicbrainz.org/artist/artist-id");
        var group = new MusicBrainzReleaseGroupSnapshot("group-id", "Album", "Album", [], null, [], "https://musicbrainz.org/release-group/group-id");
        var finding = Assert.Single(new MatchingEngine().Compare(artist, [], [group], [], "scan")) with { MusicBrainzLinks = null };

        var links = MusicBrainzLinkBuilder.Build(finding);

        Assert.Contains(new MusicBrainzLink("Artist", artist.MusicBrainzUrl), links);
        Assert.Contains(new MusicBrainzLink("Release group", group.Url), links);
        Assert.Contains(new MusicBrainzLink("Add release", "https://musicbrainz.org/release/add"), links);
    }
}
