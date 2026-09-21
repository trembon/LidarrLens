using System.Net;
using System.Text;
using System.Text.Json;
using LidarrLens.Domain;

namespace LidarrLens.Application;

public sealed class ReportExporter : IReportExporter
{
    public Task<string> ExportJsonAsync(IReadOnlyList<AuditFinding> findings, CancellationToken cancellationToken) => Task.FromResult(JsonSerializer.Serialize(findings, new JsonSerializerOptions { WriteIndented = true }));

    public Task<string> ExportCsvAsync(IReadOnlyList<AuditFinding> findings, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder("id,artist,source,type,status,confidence,title,source_url,action\n");
        foreach (var f in findings) sb.AppendLine(string.Join(',', new[] { f.Id, f.ArtistName, f.SourceName, f.Type.ToString(), f.Status.ToString(), f.Confidence.ToString(), f.CopyReady?.Title, f.SourceUrl, f.SuggestedAction }.Select(Csv)));
        return Task.FromResult(sb.ToString());
    }

    public Task<string> ExportHtmlAsync(IReadOnlyList<AuditFinding> findings, ScanRun? scan, CancellationToken cancellationToken)
    {
        var cards = string.Join("\n", findings.Select(f => $"<article class='card' data-search='{WebUtility.HtmlEncode((f.ArtistName + " " + f.CopyReady?.Title + " " + f.SourceName).ToLowerInvariant())}'><div class='row'><h2>{E(f.CopyReady?.Title ?? f.Type.ToString())}</h2><span class='badge'>{E(f.Status.ToString())}</span></div><p><strong>{E(f.ArtistName)}</strong> · {E(f.Type.ToString())} · {E(f.Confidence.ToString())}</p><p>{E(f.SuggestedAction)}</p><details><summary>Evidence and copy-ready data</summary><pre>{E(f.CopyReady is null ? string.Join("\n", f.Evidence) : string.Join("\n", f.CopyReady.Tracklist))}</pre><button onclick=\"copyText(this)\">Copy visible data</button>{(f.SourceUrl is null ? "" : $" <a href='{E(f.SourceUrl)}'>Source</a>")}</details></article>"));
        var html = $"<!doctype html><html><head><meta charset='utf-8'><title>LidarrLens report</title><style>body{{font:16px system-ui;max-width:1100px;margin:2rem auto;padding:0 1rem;background:#f5f7fb;color:#172033}}input{{width:100%;padding:.8rem;margin-bottom:1rem}}.card{{background:#fff;border:1px solid #d9e0ec;border-radius:12px;padding:1rem;margin:1rem 0;box-shadow:0 2px 8px #17203312}}.row{{display:flex;justify-content:space-between;gap:1rem}}.badge{{padding:.2rem .6rem;border-radius:999px;background:#e3ecff}}pre{{white-space:pre-wrap;background:#f0f3f8;padding:1rem;border-radius:8px}}button{{padding:.5rem .8rem}}</style></head><body><h1>LidarrLens report</h1><p>Scan: {E(scan?.Id ?? "unknown")} · Generated: {DateTimeOffset.UtcNow:u}</p><input id='q' placeholder='Search artist, title, or source' oninput='filter()'>{cards}<script>function filter(){{const q=document.getElementById('q').value.toLowerCase();document.querySelectorAll('.card').forEach(x=>x.hidden=!x.dataset.search.includes(q));}}async function copyText(b){{await navigator.clipboard.writeText(b.closest('details').querySelector('pre').innerText);b.innerText='Copied';}}</script></body></html>";
        return Task.FromResult(html);
    }

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
    private static string Csv(string? value) => $"\"{(value ?? "").Replace("\"", "\"\"")}\"";
}
