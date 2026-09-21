# LidarrLens

LidarrLens is a local, read-only audit queue for improving a Lidarr library and preparing careful MusicBrainz contributions. It compares Lidarr’s known MusicBrainz entities with direct MusicBrainz data and Deezer’s public catalog. It never writes to Lidarr or submits MusicBrainz edits.

## Quick start with Docker

1. Copy `.env.example` to `.env`.
2. Set `LIDARR_URL`, `LIDARR_API_KEY`, and a contact-bearing `MUSICBRAINZ_USER_AGENT`.
3. Run:

```bash
docker compose up --build
```

4. Open http://localhost:8080.
5. Start a scan from the dashboard.

Use the Artists page to control scan scope. The first artist sync selects all artists currently in Lidarr. Artists added later are shown as untracked until selected. Only selected artists with a MusicBrainz ID are scanned, so untracking inactive artists prevents their per-artist Lidarr, MusicBrainz, and Deezer requests on future manual or scheduled scans. Existing findings for untracked artists are preserved.

Deezer requires no API key. It is used as optional evidence and discovery data. If it is unavailable, MusicBrainz/Lidarr scanning still works.

MusicBrainz scans default to high-level mode (`MUSICBRAINZ_SCAN_MODE=high-level`), which fetches release groups and assumes a matched group's tracklist is acceptable. This avoids the per-group `/release` requests used for track, recording, barcode, and edition checks. Set `MUSICBRAINZ_SCAN_MODE=detailed` when those checks are needed.

## Local development

Requires the .NET 10 SDK.

```bash
dotnet restore LidarrLens.sln
dotnet run --project src/LidarrLens.Web/LidarrLens.Web.csproj
dotnet test LidarrLens.sln
```

For local development, use `LIDARR_URL=http://localhost:8686`, set `LIDARR_API_KEY`, and set `DATA_DIRECTORY` to a writable directory such as `./data`.

## Visual Studio

1. Copy `src/LidarrLens.Web/appsettings.Local.json.example` to `src/LidarrLens.Web/appsettings.Local.json`.
2. Replace the placeholder with your Lidarr API key and, optionally, a contact-bearing MusicBrainz User-Agent.
3. Set `LidarrLens.Web` as the startup project.
4. Press F5. Visual Studio uses `Properties/launchSettings.json` and opens `http://localhost:5298`.

`appsettings.Local.json` is ignored by Git so the Lidarr API key stays local.

## Safety model

- Lidarr calls are GET-only.
- No MusicBrainz submission endpoint is implemented.
- External provider responses are cached in SQLite.
- Reports omit API keys and local filesystem paths.
- Findings are suggestions for manual review, not automatic edits.

## Local endpoints

- `POST /api/scans`
- `GET /api/work-items`
- `POST /api/work-items/{id}/status`
- `GET /api/exports/{scanId}/html`
- `GET /api/exports/{scanId}/json`
- `GET /api/exports/{scanId}/csv`

Set `SCAN_INTERVAL_MINUTES` to a positive number to enable optional recurring scans; leave it unset or set it to `0` for manual scans only.

`MUSICBRAINZ_SCAN_MODE` accepts `high-level` (the default) or `detailed`.

## Statuses

`pending`, `accepted`, `rejected`, `submitted`, `ignored`, and `resolved`. `ignored` means “won’t do.” `resolved` is assigned when a later scan finds the corresponding MusicBrainz entity.
