namespace LidarrLens.Domain;

public enum FindingStatus { Pending, Accepted, Rejected, Submitted, Ignored, Waiting, Resolved }
public enum FindingType { MissingReleaseGroup, MissingRelease, IncompleteTracklist, MissingRecording, NotInLidarr, LikelyDuplicate }
public enum ConfidenceLevel { Low, Medium, High }

public sealed record ArtistSnapshot(string Name, string MusicBrainzId, string MusicBrainzUrl);

public sealed record LidarrTrackSnapshot(string Title, int DurationSeconds);

public sealed record LidarrReleaseSnapshot(
    string? MusicBrainzReleaseId,
    string Title,
    int TrackCount,
    int DurationSeconds,
    string? Country,
    string? Label,
    string? Format);

public sealed record LidarrAlbumSnapshot(
    int LidarrId,
    string Title,
    string? MusicBrainzReleaseGroupId,
    DateTimeOffset? ReleaseDate,
    string AlbumType,
    IReadOnlyList<LidarrTrackSnapshot> Tracks,
    IReadOnlyList<LidarrReleaseSnapshot> Releases);

public sealed record MusicBrainzTrack(string Title, int? DurationMilliseconds, string? RecordingId, string? Isrc);

public sealed record MusicBrainzReleaseSnapshot(
    string Id,
    string Title,
    string? ReleaseGroupId,
    DateOnly? Date,
    string? Country,
    string? Label,
    string? CatalogNumber,
    string? Barcode,
    string? Format,
    IReadOnlyList<MusicBrainzTrack> Tracks,
    IReadOnlyList<string> Urls);

public sealed record MusicBrainzReleaseGroupSnapshot(
    string Id,
    string Title,
    string PrimaryType,
    IReadOnlyList<string> SecondaryTypes,
    DateOnly? FirstReleaseDate,
    IReadOnlyList<MusicBrainzReleaseSnapshot> Releases,
    string Url,
    bool ReleaseDetailsLoaded = true);

public sealed record SourceTrack(string Title, int? DurationSeconds, string? Isrc = null);

public sealed record SourceRelease(
    string Source,
    string Id,
    string Title,
    string Url,
    string? ArtistId,
    DateOnly? ReleaseDate,
    string? Country,
    string? Label,
    string? CatalogNumber,
    string? Barcode,
    string? Format,
    IReadOnlyList<SourceTrack> Tracks,
    string? ArtworkUrl = null);

public sealed record CopyReadyRelease(
    string Title,
    string ReleaseGroupType,
    string? ReleaseDate,
    string? Country,
    string? Label,
    string? CatalogNumber,
    string? Barcode,
    string? Format,
    string? Disambiguation,
    IReadOnlyList<string> SourceUrls,
    IReadOnlyList<string> Tracklist,
    string EditNote,
    string? MusicBrainzEditorUrl,
    string? ArtworkUrl = null);

public sealed record MusicBrainzLink(string Label, string Url);

public sealed record AuditFinding(
    string Id,
    string Fingerprint,
    string ArtistName,
    string ArtistMusicBrainzId,
    string ArtistMusicBrainzUrl,
    string SourceName,
    string? SourceId,
    string? SourceUrl,
    FindingType Type,
    string SuggestedAction,
    ConfidenceLevel Confidence,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> ExistingMusicBrainzEntities,
    IReadOnlyList<string> MissingFields,
    IReadOnlyList<string> DuplicateWarnings,
    CopyReadyRelease? CopyReady,
    FindingStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? ScanId = null,
    IReadOnlyList<MusicBrainzLink>? MusicBrainzLinks = null);

public sealed record ScanRun(
    string Id,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string Status,
    int ArtistsScanned,
    int FindingsCreated,
    string? Error = null);

public interface ILidarrClient
{
    Task<IReadOnlyList<LidarrArtist>> GetArtistsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<LidarrAlbumSnapshot>> GetAlbumsAsync(int artistId, CancellationToken cancellationToken);
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken);
}

public sealed record LidarrArtist(int Id, string Name, string? MusicBrainzId, string? ForeignArtistId);

public sealed record ArtistTracking(LidarrArtist Artist, bool IsTracked, DateTimeOffset LastSeenAt);

public interface IArtistTrackingService
{
    Task<IReadOnlyList<ArtistTracking>> SyncAsync(CancellationToken cancellationToken);
    Task SaveAsync(IReadOnlyDictionary<int, bool> selections, CancellationToken cancellationToken);
}

public interface IMusicBrainzClient
{
    Task<IReadOnlyList<MusicBrainzReleaseGroupSnapshot>> GetReleaseGroupsAsync(string artistId, CancellationToken cancellationToken);
}

public interface IMetadataSourceAdapter
{
    string Name { get; }
    Task<IReadOnlyList<SourceRelease>> GetArtistReleasesAsync(ArtistSnapshot artist, CancellationToken cancellationToken);
}

public interface IMatchingEngine
{
    IReadOnlyList<AuditFinding> Compare(ArtistSnapshot artist, IReadOnlyList<LidarrAlbumSnapshot> lidarrAlbums, IReadOnlyList<MusicBrainzReleaseGroupSnapshot> musicBrainzGroups, IReadOnlyList<SourceRelease> sourceReleases, string scanId);
}

public interface IScanService
{
    Task<ScanRun> StartAsync(CancellationToken cancellationToken);
    Task<ScanRun?> GetAsync(string id, CancellationToken cancellationToken);
    Task<IReadOnlyList<AuditFinding>> GetFindingsAsync(string? status, string? query, CancellationToken cancellationToken);
    Task<bool> UpdateStatusAsync(string findingId, FindingStatus status, CancellationToken cancellationToken);
}

public interface IReportExporter
{
    Task<string> ExportHtmlAsync(IReadOnlyList<AuditFinding> findings, ScanRun? scan, CancellationToken cancellationToken);
    Task<string> ExportJsonAsync(IReadOnlyList<AuditFinding> findings, CancellationToken cancellationToken);
    Task<string> ExportCsvAsync(IReadOnlyList<AuditFinding> findings, CancellationToken cancellationToken);
}

public interface IResponseCache
{
    Task<string?> GetAsync(string provider, string cacheKey, CancellationToken cancellationToken);
    Task SetAsync(string provider, string cacheKey, string response, TimeSpan ttl, CancellationToken cancellationToken);
}

public interface ILidarrLensStore : IResponseCache
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task SaveScanAsync(ScanRun scan, IReadOnlyList<AuditFinding> findings, IReadOnlySet<string> scannedArtistMusicBrainzIds, CancellationToken cancellationToken);
    Task<ScanRun?> GetScanAsync(string id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ScanRun>> GetScansAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<AuditFinding>> GetFindingsAsync(string? status, string? query, CancellationToken cancellationToken);
    Task<AuditFinding?> GetFindingAsync(string id, CancellationToken cancellationToken);
    Task<bool> UpdateFindingStatusAsync(string id, FindingStatus status, CancellationToken cancellationToken);
    Task<IReadOnlyList<AuditFinding>> GetFindingsForScanAsync(string scanId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ArtistTracking>> SyncArtistTrackingAsync(IReadOnlyList<LidarrArtist> artists, CancellationToken cancellationToken);
    Task SaveArtistTrackingAsync(IReadOnlyDictionary<int, bool> selections, CancellationToken cancellationToken);
}
