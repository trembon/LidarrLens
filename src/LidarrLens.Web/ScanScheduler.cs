using LidarrLens.Domain;

namespace LidarrLens.Web;

public sealed class ScanScheduler(IConfiguration configuration, IScanService scans, ILogger<ScanScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!int.TryParse(configuration["SCAN_INTERVAL_MINUTES"], out var minutes) || minutes <= 0) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await scans.StartAsync(stoppingToken); }
            catch (InvalidOperationException) { }
            catch (Exception ex) { logger.LogError(ex, "Scheduled LidarrLens scan failed"); }
        }
    }
}
