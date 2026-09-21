using System.Net;
using System.Text;
using LidarrLens.Application;
using LidarrLens.Domain;
using LidarrLens.Infrastructure;
using Xunit;

namespace LidarrLens.Tests;

public sealed class ArtworkTests
{
    [Theory]
    [InlineData("\"cover_xl\":\"https://cdn.example.test/large.jpg\",\"cover_big\":\"https://cdn.example.test/big.jpg\"", "https://cdn.example.test/large.jpg")]
    [InlineData("\"cover_big\":\"https://cdn.example.test/big.jpg\"", "https://cdn.example.test/big.jpg")]
    [InlineData("", null)]
    public async Task Deezer_uses_high_resolution_artwork_then_falls_back_without_failing(string artworkFields, string? expectedUrl)
    {
        var handler = new StubHandler(request =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            var json = url.Contains("/search/artist", StringComparison.Ordinal)
                ? "{\"data\":[{\"id\":\"artist-1\",\"name\":\"Example\"}]}"
                : url.Contains("/artist/artist-1/albums", StringComparison.Ordinal)
                    ? "{\"data\":[{\"id\":\"album-1\"}]}"
                    : $"{{\"id\":\"album-1\",\"title\":\"Album\",\"link\":\"https://www.deezer.com/album/album-1\"{(artworkFields.Length == 0 ? "" : $",{artworkFields}")}}}";
            return JsonResponse(json);
        });
        var adapter = new DeezerAdapter(new HttpClient(handler), new EmptyCache());

        var releases = await adapter.GetArtistReleasesAsync(new ArtistSnapshot("Example", "artist-1", "https://musicbrainz.org/artist/artist-1"), CancellationToken.None);

        Assert.Equal(expectedUrl, Assert.Single(releases).ArtworkUrl);
    }

    [Fact]
    public void Matching_carries_source_artwork_into_copy_ready_data()
    {
        const string artworkUrl = "https://cdn.example.test/cover.jpg";
        var artist = new ArtistSnapshot("Example", "artist-id", "https://musicbrainz.org/artist/artist-id");
        var source = new SourceRelease("Deezer", "dz-id", "Album", "https://deezer.com/album/dz-id", "dz-artist", null, null, null, null, null, "Digital", [], artworkUrl);

        var finding = Assert.Single(new MatchingEngine().Compare(artist, [], [], [source], "scan"));

        Assert.Equal(artworkUrl, finding.CopyReady?.ArtworkUrl);
    }

    [Fact]
    public async Task Exports_and_copy_ready_text_retain_artwork_url()
    {
        const string artworkUrl = "https://cdn.example.test/cover.jpg";
        var now = DateTimeOffset.UtcNow;
        var copy = new CopyReadyRelease("Album", "album", null, null, null, null, null, "Digital", null, [], ["1. Track"], "Verify the release.", "https://musicbrainz.org/release/add", artworkUrl);
        var finding = new AuditFinding("finding-1", "fingerprint", "Example", "artist-id", "https://musicbrainz.org/artist/artist-id", "Deezer", "dz-id", "https://deezer.com/album/dz-id", FindingType.MissingReleaseGroup, "Review", ConfidenceLevel.Medium, [], [], [], [], copy, FindingStatus.Pending, now, now);
        var exporter = new ReportExporter();

        var json = await exporter.ExportJsonAsync([finding], CancellationToken.None);
        var csv = await exporter.ExportCsvAsync([finding], CancellationToken.None);
        var html = await exporter.ExportHtmlAsync([finding], null, CancellationToken.None);
        var clipboard = CopyReadyTextFormatter.Format(copy);

        Assert.Contains(artworkUrl, json);
        Assert.Contains("artwork_url", csv);
        Assert.Contains(artworkUrl, csv);
        Assert.Contains(artworkUrl, html);
        Assert.Contains(artworkUrl, clipboard);
        Assert.Contains("Cover Art tab", clipboard);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(responseFactory(request));
    }

    private sealed class EmptyCache : IResponseCache
    {
        public Task<string?> GetAsync(string provider, string cacheKey, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task SetAsync(string provider, string cacheKey, string response, TimeSpan ttl, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
