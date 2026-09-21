using LidarrLens.Domain;
using Microsoft.Extensions.Logging;

namespace LidarrLens.Application;

public sealed class ScanService(ILidarrClient lidarr, IMusicBrainzClient musicBrainz, IEnumerable<IMetadataSourceAdapter> sources, IMatchingEngine matcher, ILidarrLensStore store, ILogger<ScanService> logger) : IScanService
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<ScanRun> StartAsync(CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken)) throw new InvalidOperationException("A scan is already running.");
        var scan = new ScanRun(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, null, "running", 0, 0);
        var allFindings = new List<AuditFinding>();
        var artistsScanned = 0;
        try
        {
            var artists = await lidarr.GetArtistsAsync(cancellationToken);
            foreach (var item in artists.Where(x => !string.IsNullOrWhiteSpace(x.MusicBrainzId)))
            {
                var artist = new ArtistSnapshot(item.Name, item.MusicBrainzId!, $"https://musicbrainz.org/artist/{item.MusicBrainzId}");
                var albums = await lidarr.GetAlbumsAsync(item.Id, cancellationToken);
                var groups = await musicBrainz.GetReleaseGroupsAsync(artist.MusicBrainzId, cancellationToken);
                var sourceReleases = new List<SourceRelease>();
                foreach (var source in sources)
                {
                    try { sourceReleases.AddRange(await source.GetArtistReleasesAsync(artist, cancellationToken)); }
                    catch (Exception ex) { logger.LogWarning(ex, "Metadata source {Source} failed for {Artist}", source.Name, artist.Name); }
                }
                allFindings.AddRange(matcher.Compare(artist, albums, groups, sourceReleases, scan.Id));
                artistsScanned++;
            }
            var completed = scan with { CompletedAt = DateTimeOffset.UtcNow, Status = "completed", ArtistsScanned = artistsScanned, FindingsCreated = allFindings.Count };
            await store.SaveScanAsync(completed, allFindings, cancellationToken);
            return completed;
        }
        catch (Exception ex)
        {
            var failed = scan with { CompletedAt = DateTimeOffset.UtcNow, Status = "failed", ArtistsScanned = artistsScanned, FindingsCreated = allFindings.Count, Error = ex.Message };
            await store.SaveScanAsync(failed, allFindings, cancellationToken);
            throw;
        }
        finally { _gate.Release(); }
    }

    public Task<ScanRun?> GetAsync(string id, CancellationToken cancellationToken) => store.GetScanAsync(id, cancellationToken);
    public Task<IReadOnlyList<AuditFinding>> GetFindingsAsync(string? status, string? query, CancellationToken cancellationToken) => store.GetFindingsAsync(status, query, cancellationToken);
    public Task<bool> UpdateStatusAsync(string findingId, FindingStatus status, CancellationToken cancellationToken) => store.UpdateFindingStatusAsync(findingId, status, cancellationToken);
}
