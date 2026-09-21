using LidarrLens.Domain;

namespace LidarrLens.Application;

public sealed class ArtistTrackingService(ILidarrClient lidarr, ILidarrLensStore store) : IArtistTrackingService
{
    public async Task<IReadOnlyList<ArtistTracking>> SyncAsync(CancellationToken cancellationToken)
    {
        var artists = await lidarr.GetArtistsAsync(cancellationToken);
        return await store.SyncArtistTrackingAsync(artists, cancellationToken);
    }

    public Task SaveAsync(IReadOnlyDictionary<int, bool> selections, CancellationToken cancellationToken) =>
        store.SaveArtistTrackingAsync(selections, cancellationToken);
}
