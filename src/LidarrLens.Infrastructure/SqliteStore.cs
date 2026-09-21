using System.Text.Json;
using LidarrLens.Domain;
using Microsoft.Data.Sqlite;

namespace LidarrLens.Infrastructure;

public sealed class SqliteStore(string dataDirectory) : ILidarrLensStore
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(dataDirectory, "lidarrlens.db"), Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(new SqliteConnectionStringBuilder(_connectionString).DataSource)!);
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS scans (id TEXT PRIMARY KEY, started_at TEXT NOT NULL, completed_at TEXT NULL, status TEXT NOT NULL, artists_scanned INTEGER NOT NULL, findings_created INTEGER NOT NULL, error TEXT NULL);
            CREATE TABLE IF NOT EXISTS findings (id TEXT PRIMARY KEY, fingerprint TEXT NOT NULL, scan_id TEXT NOT NULL, status TEXT NOT NULL, updated_at TEXT NOT NULL, payload TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_findings_status ON findings(status);
            CREATE INDEX IF NOT EXISTS ix_findings_fingerprint ON findings(fingerprint);
            CREATE TABLE IF NOT EXISTS response_cache (provider TEXT NOT NULL, cache_key TEXT NOT NULL, expires_at TEXT NOT NULL, response TEXT NOT NULL, PRIMARY KEY(provider, cache_key));
            CREATE TABLE IF NOT EXISTS artist_tracking (lidarr_id INTEGER PRIMARY KEY, name TEXT NOT NULL, musicbrainz_id TEXT NULL, foreign_artist_id TEXT NULL, is_tracked INTEGER NOT NULL, last_seen_at TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_artist_tracking_last_seen ON artist_tracking(last_seen_at);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveScanAsync(ScanRun scan, IReadOnlyList<AuditFinding> findings, IReadOnlySet<string> scannedArtistMusicBrainzIds, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var scanCommand = connection.CreateCommand();
        scanCommand.Transaction = transaction;
        scanCommand.CommandText = "INSERT OR REPLACE INTO scans(id,started_at,completed_at,status,artists_scanned,findings_created,error) VALUES ($id,$started,$completed,$status,$artists,$findings,$error)";
        scanCommand.Parameters.AddWithValue("$id", scan.Id); scanCommand.Parameters.AddWithValue("$started", scan.StartedAt.ToString("O")); scanCommand.Parameters.AddWithValue("$completed", (object?)scan.CompletedAt?.ToString("O") ?? DBNull.Value); scanCommand.Parameters.AddWithValue("$status", scan.Status); scanCommand.Parameters.AddWithValue("$artists", scan.ArtistsScanned); scanCommand.Parameters.AddWithValue("$findings", scan.FindingsCreated); scanCommand.Parameters.AddWithValue("$error", (object?)scan.Error ?? DBNull.Value);
        await scanCommand.ExecuteNonQueryAsync(cancellationToken);

        foreach (var finding in findings)
        {
            var prior = await FindByFingerprintAsync(connection, transaction, finding.Fingerprint, cancellationToken);
            var effective = prior is not null && prior.Status is FindingStatus.Ignored or FindingStatus.Rejected or FindingStatus.Submitted
                ? finding with { Status = prior.Status }
                : finding;
            var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "INSERT OR REPLACE INTO findings(id,fingerprint,scan_id,status,updated_at,payload) VALUES ($id,$fingerprint,$scan,$status,$updated,$payload)";
            command.Parameters.AddWithValue("$id", effective.Id); command.Parameters.AddWithValue("$fingerprint", effective.Fingerprint); command.Parameters.AddWithValue("$scan", scan.Id); command.Parameters.AddWithValue("$status", effective.Status.ToString()); command.Parameters.AddWithValue("$updated", effective.UpdatedAt.ToString("O")); command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(effective));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        if (scan.Status == "completed")
        {
            var fingerprints = findings.Select(x => x.Fingerprint).ToHashSet(StringComparer.Ordinal);
            var old = connection.CreateCommand(); old.Transaction = transaction; old.CommandText = "SELECT id,payload FROM findings WHERE scan_id<>$scan AND status IN ('Pending','Accepted')"; old.Parameters.AddWithValue("$scan", scan.Id);
            var stale = new List<AuditFinding>();
            await using (var reader = await old.ExecuteReaderAsync(cancellationToken))
                while (await reader.ReadAsync(cancellationToken)) { var item = JsonSerializer.Deserialize<AuditFinding>(reader.GetString(1)); if (item is not null && scannedArtistMusicBrainzIds.Contains(item.ArtistMusicBrainzId) && !fingerprints.Contains(item.Fingerprint)) stale.Add(item); }
            foreach (var item in stale)
            {
                var resolved = item with { Status = FindingStatus.Resolved, UpdatedAt = DateTimeOffset.UtcNow };
                var update = connection.CreateCommand(); update.Transaction = transaction; update.CommandText = "UPDATE findings SET status='Resolved',updated_at=$updated,payload=$payload WHERE id=$id"; update.Parameters.AddWithValue("$updated", resolved.UpdatedAt.ToString("O")); update.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(resolved)); update.Parameters.AddWithValue("$id", resolved.Id); await update.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<AuditFinding?> FindByFingerprintAsync(SqliteConnection connection, SqliteTransaction transaction, string fingerprint, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "SELECT payload FROM findings WHERE fingerprint=$fingerprint ORDER BY updated_at DESC LIMIT 1"; command.Parameters.AddWithValue("$fingerprint", fingerprint); var result = await command.ExecuteScalarAsync(cancellationToken); return result is string json ? JsonSerializer.Deserialize<AuditFinding>(json) : null;
    }

    public async Task<ScanRun?> GetScanAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken); var command = connection.CreateCommand(); command.CommandText = "SELECT id,started_at,completed_at,status,artists_scanned,findings_created,error FROM scans WHERE id=$id"; command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); return await reader.ReadAsync(cancellationToken) ? ReadScan(reader) : null;
    }

    public async Task<IReadOnlyList<ScanRun>> GetScansAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken); var command = connection.CreateCommand(); command.CommandText = "SELECT id,started_at,completed_at,status,artists_scanned,findings_created,error FROM scans ORDER BY started_at DESC";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); var list = new List<ScanRun>(); while (await reader.ReadAsync(cancellationToken)) list.Add(ReadScan(reader)); return list;
    }

    public async Task<IReadOnlyList<AuditFinding>> GetFindingsAsync(string? status, string? query, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken); var command = connection.CreateCommand(); command.CommandText = "SELECT payload FROM findings WHERE ($status IS NULL OR status=$status) ORDER BY updated_at DESC"; command.Parameters.AddWithValue("$status", (object?)status ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); var list = new List<AuditFinding>(); while (await reader.ReadAsync(cancellationToken)) { var item = JsonSerializer.Deserialize<AuditFinding>(reader.GetString(0)); if (item is not null && (string.IsNullOrWhiteSpace(query) || $"{item.ArtistName} {item.CopyReady?.Title} {item.SourceName} {item.Type}".Contains(query, StringComparison.OrdinalIgnoreCase))) list.Add(item); } return list;
    }

    public async Task<AuditFinding?> GetFindingAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken); var command = connection.CreateCommand(); command.CommandText = "SELECT payload FROM findings WHERE id=$id"; command.Parameters.AddWithValue("$id", id); var result = await command.ExecuteScalarAsync(cancellationToken); return result is string json ? JsonSerializer.Deserialize<AuditFinding>(json) : null;
    }

    public async Task<bool> UpdateFindingStatusAsync(string id, FindingStatus status, CancellationToken cancellationToken)
    {
        var current = await GetFindingAsync(id, cancellationToken); if (current is null) return false;
        var updated = current with { Status = status, UpdatedAt = DateTimeOffset.UtcNow };
        await using var connection = await OpenAsync(cancellationToken); var command = connection.CreateCommand(); command.CommandText = "UPDATE findings SET status=$status,updated_at=$updated,payload=$payload WHERE id=$id"; command.Parameters.AddWithValue("$status", status.ToString()); command.Parameters.AddWithValue("$updated", updated.UpdatedAt.ToString("O")); command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(updated)); command.Parameters.AddWithValue("$id", id); return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<IReadOnlyList<AuditFinding>> GetFindingsForScanAsync(string scanId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken); var command = connection.CreateCommand(); command.CommandText = "SELECT payload FROM findings WHERE scan_id=$scan"; command.Parameters.AddWithValue("$scan", scanId); await using var reader = await command.ExecuteReaderAsync(cancellationToken); var list = new List<AuditFinding>(); while (await reader.ReadAsync(cancellationToken)) { var item = JsonSerializer.Deserialize<AuditFinding>(reader.GetString(0)); if (item is not null) list.Add(item); } return list;
    }

    public async Task<IReadOnlyList<ArtistTracking>> SyncArtistTrackingAsync(IReadOnlyList<LidarrArtist> artists, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var countCommand = connection.CreateCommand();
        countCommand.Transaction = transaction;
        countCommand.CommandText = "SELECT COUNT(*) FROM artist_tracking";
        var isFirstSync = Convert.ToInt64(await countCommand.ExecuteScalarAsync(cancellationToken)) == 0;
        var seenAt = DateTimeOffset.UtcNow.ToString("O");

        foreach (var artist in artists)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO artist_tracking(lidarr_id,name,musicbrainz_id,foreign_artist_id,is_tracked,last_seen_at)
                VALUES ($id,$name,$mbid,$foreign,$tracked,$seen)
                ON CONFLICT(lidarr_id) DO UPDATE SET
                    name=excluded.name,
                    musicbrainz_id=excluded.musicbrainz_id,
                    foreign_artist_id=excluded.foreign_artist_id,
                    last_seen_at=excluded.last_seen_at
                """;
            command.Parameters.AddWithValue("$id", artist.Id);
            command.Parameters.AddWithValue("$name", artist.Name);
            command.Parameters.AddWithValue("$mbid", (object?)artist.MusicBrainzId ?? DBNull.Value);
            command.Parameters.AddWithValue("$foreign", (object?)artist.ForeignArtistId ?? DBNull.Value);
            command.Parameters.AddWithValue("$tracked", isFirstSync ? 1 : 0);
            command.Parameters.AddWithValue("$seen", seenAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var result = new List<ArtistTracking>();
        if (artists.Count > 0)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            var placeholders = new List<string>();
            for (var index = 0; index < artists.Count; index++)
            {
                var parameter = $"$id{index}";
                placeholders.Add(parameter);
                command.Parameters.AddWithValue(parameter, artists[index].Id);
            }
            command.CommandText = $"SELECT lidarr_id,name,musicbrainz_id,foreign_artist_id,is_tracked,last_seen_at FROM artist_tracking WHERE lidarr_id IN ({string.Join(",", placeholders)}) ORDER BY name COLLATE NOCASE";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var musicBrainzId = reader.IsDBNull(2) ? null : reader.GetString(2);
                var foreignArtistId = reader.IsDBNull(3) ? null : reader.GetString(3);
                result.Add(new ArtistTracking(new LidarrArtist(reader.GetInt32(0), reader.GetString(1), musicBrainzId, foreignArtistId), reader.GetInt32(4) == 1, DateTimeOffset.Parse(reader.GetString(5))));
            }
        }
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task SaveArtistTrackingAsync(IReadOnlyDictionary<int, bool> selections, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        foreach (var selection in selections)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE artist_tracking SET is_tracked=$tracked WHERE lidarr_id=$id";
            command.Parameters.AddWithValue("$tracked", selection.Value ? 1 : 0);
            command.Parameters.AddWithValue("$id", selection.Key);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<string?> GetAsync(string provider, string cacheKey, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken); var command = connection.CreateCommand(); command.CommandText = "SELECT response,expires_at FROM response_cache WHERE provider=$provider AND cache_key=$key"; command.Parameters.AddWithValue("$provider", provider); command.Parameters.AddWithValue("$key", cacheKey); await using var reader = await command.ExecuteReaderAsync(cancellationToken); if (!await reader.ReadAsync(cancellationToken)) return null; return DateTimeOffset.Parse(reader.GetString(1)) > DateTimeOffset.UtcNow ? reader.GetString(0) : null;
    }

    public async Task SetAsync(string provider, string cacheKey, string response, TimeSpan ttl, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken); var command = connection.CreateCommand(); command.CommandText = "INSERT OR REPLACE INTO response_cache(provider,cache_key,expires_at,response) VALUES ($provider,$key,$expires,$response)"; command.Parameters.AddWithValue("$provider", provider); command.Parameters.AddWithValue("$key", cacheKey); command.Parameters.AddWithValue("$expires", DateTimeOffset.UtcNow.Add(ttl).ToString("O")); command.Parameters.AddWithValue("$response", response); await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken) { var c = new SqliteConnection(_connectionString); await c.OpenAsync(cancellationToken); return c; }
    private static ScanRun ReadScan(SqliteDataReader reader) => new(reader.GetString(0), DateTimeOffset.Parse(reader.GetString(1)), reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2)), reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5), reader.IsDBNull(6) ? null : reader.GetString(6));
}
