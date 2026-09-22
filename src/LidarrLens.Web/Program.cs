using LidarrLens.Application;
using LidarrLens.Domain;
using LidarrLens.Infrastructure;
using LidarrLens.Web;
using LidarrLens.Web.Components;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
var configuration = builder.Configuration;
var dataDirectory = configuration["DATA_DIRECTORY"] ?? "/data";
var lidarrUrl = configuration["LIDARR_URL"] ?? "http://localhost:8686";
var lidarrApiKey = configuration["LIDARR_API_KEY"] ?? string.Empty;
var musicBrainzUserAgent = configuration["MUSICBRAINZ_USER_AGENT"] ?? "LidarrLens/0.1.0";
var includeMusicBrainzReleaseDetails = string.Equals(configuration["MUSICBRAINZ_SCAN_MODE"], "detailed", StringComparison.OrdinalIgnoreCase);
var musicBrainzTaskView = string.Equals(configuration["MUSICBRAINZ_TASK_VIEW"], "external", StringComparison.OrdinalIgnoreCase)
    ? "external"
    : "iframe";

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys")));
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSingleton(new MusicBrainzTaskViewOptions(musicBrainzTaskView));
builder.Services.AddHttpClient("lidarr", client => client.BaseAddress = new Uri(lidarrUrl));
builder.Services.AddHttpClient("external");
builder.Services.AddSingleton<ILidarrLensStore>(_ => new SqliteStore(dataDirectory));
builder.Services.AddSingleton<ILidarrClient>(sp => new LidarrClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("lidarr"), lidarrApiKey));
builder.Services.AddSingleton<IMusicBrainzClient>(sp => new MusicBrainzClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("external"), sp.GetRequiredService<ILidarrLensStore>(), musicBrainzUserAgent, includeMusicBrainzReleaseDetails));
builder.Services.AddSingleton<IMetadataSourceAdapter>(sp => new DeezerAdapter(sp.GetRequiredService<IHttpClientFactory>().CreateClient("external"), sp.GetRequiredService<ILidarrLensStore>()));
builder.Services.AddSingleton<IArtistTrackingService, ArtistTrackingService>();
builder.Services.AddSingleton<IMatchingEngine, MatchingEngine>();
builder.Services.AddSingleton<IScanService, ScanService>();
builder.Services.AddSingleton<IReportExporter, ReportExporter>();
builder.Services.AddHostedService<ScanScheduler>();

var app = builder.Build();
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<ILidarrLensStore>().InitializeAsync(CancellationToken.None);

if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/error");
app.UseStaticFiles();
app.UseAntiforgery();

app.MapPost("/api/scans", async (IScanService scans, CancellationToken cancellationToken) =>
{
    try { return Results.Ok(await scans.StartAsync(cancellationToken)); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
});
app.MapGet("/api/scans/{id}", async (string id, IScanService scans, CancellationToken cancellationToken) => Results.Ok(await scans.GetAsync(id, cancellationToken)));
app.MapGet("/api/work-items", async (string? status, string? q, IScanService scans, CancellationToken cancellationToken) => Results.Ok(await scans.GetFindingsAsync(status, q, cancellationToken)));
app.MapPost("/api/work-items/{id}/status", async (string id, StatusRequest request, IScanService scans, CancellationToken cancellationToken) => Results.Ok(await scans.UpdateStatusAsync(id, request.Status, cancellationToken)));
app.MapGet("/api/exports/{scanId}/json", async (string scanId, ILidarrLensStore store, IReportExporter exporter, CancellationToken cancellationToken) => Results.Text(await exporter.ExportJsonAsync(await store.GetFindingsForScanAsync(scanId, cancellationToken), cancellationToken), "application/json"));
app.MapGet("/api/exports/{scanId}/csv", async (string scanId, ILidarrLensStore store, IReportExporter exporter, CancellationToken cancellationToken) => Results.Text(await exporter.ExportCsvAsync(await store.GetFindingsForScanAsync(scanId, cancellationToken), cancellationToken), "text/csv"));
app.MapGet("/api/exports/{scanId}/html", async (string scanId, ILidarrLensStore store, IReportExporter exporter, CancellationToken cancellationToken) => Results.Text(await exporter.ExportHtmlAsync(await store.GetFindingsForScanAsync(scanId, cancellationToken), await store.GetScanAsync(scanId, cancellationToken), cancellationToken), "text/html"));
app.MapGet("/api/settings/test-lidarr", async (ILidarrClient client, CancellationToken cancellationToken) => Results.Ok(new { connected = await client.TestConnectionAsync(cancellationToken) }));
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();

public sealed record StatusRequest(FindingStatus Status);
