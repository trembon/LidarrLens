using LidarrLens.Application;
using LidarrLens.Domain;
using LidarrLens.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LidarrLens.Tests;

public sealed class ArtistTrackingTests
{
    [Fact]
    public async Task First_sync_tracks_all_artists_and_later_artists_are_untracked()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lidarrlens-tests", Guid.NewGuid().ToString("N"));
        var first = new LidarrArtist(1, "Active", "mb-active", "foreign-active");
        var second = new LidarrArtist(2, "Old", "mb-old", "foreign-old");
        var third = new LidarrArtist(3, "New", "mb-new", "foreign-new");
        var lidarr = new FakeLidarr([first, second]);
        var store = new SqliteStore(directory);
        await store.InitializeAsync(CancellationToken.None);
        await store.InitializeAsync(CancellationToken.None);
        var service = new ArtistTrackingService(lidarr, store);

        var initial = await service.SyncAsync(CancellationToken.None);
        Assert.Equal(2, initial.Count);
        Assert.All(initial, item => Assert.True(item.IsTracked));

        await service.SaveAsync(new Dictionary<int, bool> { [1] = false }, CancellationToken.None);
        lidarr.Artists = [first with { Name = "Renamed active" }, second, third];
        var refreshed = await service.SyncAsync(CancellationToken.None);

        Assert.False(refreshed.Single(x => x.Artist.Id == 1).IsTracked);
        Assert.True(refreshed.Single(x => x.Artist.Id == 2).IsTracked);
        Assert.False(refreshed.Single(x => x.Artist.Id == 3).IsTracked);
        Assert.Equal("Renamed active", refreshed.Single(x => x.Artist.Id == 1).Artist.Name);
    }

    [Fact]
    public async Task Scan_only_fetches_data_for_selected_artists_with_musicbrainz_ids()
    {
        var selected = new LidarrArtist(1, "Selected", "mb-selected", "foreign-selected");
        var untracked = new LidarrArtist(2, "Untracked", "mb-untracked", "foreign-untracked");
        var withoutMusicBrainz = new LidarrArtist(3, "No MBID", null, "foreign-no-mbid");
        var lidarr = new FakeLidarr([selected, untracked, withoutMusicBrainz]);
        var tracking = new FakeTrackingService([
            new ArtistTracking(selected, true, DateTimeOffset.UtcNow),
            new ArtistTracking(untracked, false, DateTimeOffset.UtcNow),
            new ArtistTracking(withoutMusicBrainz, true, DateTimeOffset.UtcNow)]);
        var store = new FakeStore();
        var musicBrainz = new FakeMusicBrainzClient();
        var service = new ScanService(lidarr, tracking, musicBrainz, [], new EmptyMatcher(), store, NullLogger<ScanService>.Instance);

        var result = await service.StartAsync(CancellationToken.None);

        Assert.Equal(1, result.ArtistsScanned);
        Assert.Equal([1], lidarr.AlbumRequests);
        Assert.Equal(["mb-selected"], musicBrainz.Requests);
        Assert.Equal(new HashSet<string>(["mb-selected"]), store.ScannedArtistIds);
    }

    [Fact]
    public async Task Completed_scan_only_reconciles_findings_for_artists_included_in_that_scan()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lidarrlens-tests", Guid.NewGuid().ToString("N"));
        var store = new SqliteStore(directory);
        await store.InitializeAsync(CancellationToken.None);
        var firstScan = new ScanRun("first", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "completed", 2, 2);
        var selectedFinding = Finding("selected", "mb-selected");
        var untrackedFinding = Finding("untracked", "mb-untracked");
        await store.SaveScanAsync(firstScan, [selectedFinding, untrackedFinding], new HashSet<string>(["mb-selected", "mb-untracked"]), CancellationToken.None);

        var secondScan = new ScanRun("second", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "completed", 1, 0);
        await store.SaveScanAsync(secondScan, [], new HashSet<string>(["mb-selected"]), CancellationToken.None);

        var findings = await store.GetFindingsAsync(null, null, CancellationToken.None);
        Assert.Equal(FindingStatus.Resolved, findings.Single(x => x.ArtistMusicBrainzId == "mb-selected").Status);
        Assert.Equal(FindingStatus.Pending, findings.Single(x => x.ArtistMusicBrainzId == "mb-untracked").Status);
    }

    [Fact]
    public async Task Waiting_findings_survive_rescan_and_resolve_when_the_finding_disappears()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lidarrlens-tests", Guid.NewGuid().ToString("N"));
        var store = new SqliteStore(directory);
        await store.InitializeAsync(CancellationToken.None);
        var waiting = Finding("waiting", "mb-waiting") with { Status = FindingStatus.Waiting };

        await store.SaveScanAsync(new ScanRun("first", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "completed", 1, 1), [waiting], new HashSet<string>(["mb-waiting"]), CancellationToken.None);
        await store.SaveScanAsync(new ScanRun("second", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "completed", 1, 1), [waiting], new HashSet<string>(["mb-waiting"]), CancellationToken.None);

        var afterRescan = await store.GetFindingsAsync("Waiting", null, CancellationToken.None);
        Assert.Single(afterRescan);

        await store.SaveScanAsync(new ScanRun("third", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "completed", 1, 0), [], new HashSet<string>(["mb-waiting"]), CancellationToken.None);

        var afterResolution = await store.GetFindingsAsync(null, null, CancellationToken.None);
        Assert.Equal(FindingStatus.Resolved, Assert.Single(afterResolution).Status);
    }

    private static AuditFinding Finding(string id, string artistMusicBrainzId)
    {
        var now = DateTimeOffset.UtcNow;
        return new AuditFinding(id, $"fingerprint-{id}", id, artistMusicBrainzId, $"https://musicbrainz.org/artist/{artistMusicBrainzId}", "test", null, null, FindingType.MissingReleaseGroup, "Review", ConfidenceLevel.Low, [], [], [], [], null, FindingStatus.Pending, now, now, "first");
    }

    private sealed class FakeLidarr(IReadOnlyList<LidarrArtist> artists) : ILidarrClient
    {
        public IReadOnlyList<LidarrArtist> Artists { get; set; } = artists;
        public List<int> AlbumRequests { get; } = [];
        public Task<IReadOnlyList<LidarrArtist>> GetArtistsAsync(CancellationToken cancellationToken) => Task.FromResult(Artists);
        public Task<IReadOnlyList<LidarrAlbumSnapshot>> GetAlbumsAsync(int artistId, CancellationToken cancellationToken)
        {
            AlbumRequests.Add(artistId);
            return Task.FromResult<IReadOnlyList<LidarrAlbumSnapshot>>([]);
        }
        public Task<bool> TestConnectionAsync(CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class FakeTrackingService(IReadOnlyList<ArtistTracking> artists) : IArtistTrackingService
    {
        public Task<IReadOnlyList<ArtistTracking>> SyncAsync(CancellationToken cancellationToken) => Task.FromResult(artists);
        public Task SaveAsync(IReadOnlyDictionary<int, bool> selections, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeMusicBrainzClient : IMusicBrainzClient
    {
        public List<string> Requests { get; } = [];
        public Task<IReadOnlyList<MusicBrainzReleaseGroupSnapshot>> GetReleaseGroupsAsync(string artistId, CancellationToken cancellationToken)
        {
            Requests.Add(artistId);
            return Task.FromResult<IReadOnlyList<MusicBrainzReleaseGroupSnapshot>>([]);
        }
    }

    private sealed class EmptyMatcher : IMatchingEngine
    {
        public IReadOnlyList<AuditFinding> Compare(ArtistSnapshot artist, IReadOnlyList<LidarrAlbumSnapshot> lidarrAlbums, IReadOnlyList<MusicBrainzReleaseGroupSnapshot> musicBrainzGroups, IReadOnlyList<SourceRelease> sourceReleases, string scanId) => [];
    }

    private sealed class FakeStore : ILidarrLensStore
    {
        public IReadOnlySet<string> ScannedArtistIds { get; private set; } = new HashSet<string>();
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveScanAsync(ScanRun scan, IReadOnlyList<AuditFinding> findings, IReadOnlySet<string> scannedArtistMusicBrainzIds, CancellationToken cancellationToken)
        {
            ScannedArtistIds = scannedArtistMusicBrainzIds;
            return Task.CompletedTask;
        }
        public Task<ScanRun?> GetScanAsync(string id, CancellationToken cancellationToken) => Task.FromResult<ScanRun?>(null);
        public Task<IReadOnlyList<ScanRun>> GetScansAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ScanRun>>([]);
        public Task<IReadOnlyList<AuditFinding>> GetFindingsAsync(string? status, string? query, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AuditFinding>>([]);
        public Task<AuditFinding?> GetFindingAsync(string id, CancellationToken cancellationToken) => Task.FromResult<AuditFinding?>(null);
        public Task<bool> UpdateFindingStatusAsync(string id, FindingStatus status, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<IReadOnlyList<AuditFinding>> GetFindingsForScanAsync(string scanId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AuditFinding>>([]);
        public Task<string?> GetAsync(string provider, string cacheKey, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task SetAsync(string provider, string cacheKey, string response, TimeSpan ttl, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<ArtistTracking>> SyncArtistTrackingAsync(IReadOnlyList<LidarrArtist> artists, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ArtistTracking>>([]);
        public Task SaveArtistTrackingAsync(IReadOnlyDictionary<int, bool> selections, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
