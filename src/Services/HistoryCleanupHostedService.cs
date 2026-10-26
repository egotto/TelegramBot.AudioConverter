using Microsoft.EntityFrameworkCore;

namespace TelegramAudioBot.Services;

public class HistoryCleanupHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<HistoryCleanupHostedService> logger) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromHours(configuration.GetValue("HistoryCleanup:IntervalHours", 4));
    private readonly TimeSpan _retention = TimeSpan.FromDays(configuration.GetValue("HistoryCleanup:RetentionDays", 7));
    private readonly string _filesRoot = Path.GetFullPath(configuration["TEMP_FILES_PATH"] ?? Path.Combine(Path.GetTempPath(), "audio_processing"));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to clean expired history");
            }
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TelegramAudioBotDbContext>();
        var userDataService = scope.ServiceProvider.GetRequiredService<IUserDataService>();
        var cutoff = DateTimeOffset.UtcNow - _retention;
        var expiredPaths = await dbContext.HistoryEntries
            .AsNoTracking()
            .Where(entry => entry.OccurredAt < cutoff && entry.RelativeFilePath != null)
            .Select(entry => entry.RelativeFilePath!)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var relativePath in expiredPaths)
        {
            var fullPath = Path.GetFullPath(Path.Combine(_filesRoot, relativePath));
            if (fullPath.StartsWith(_filesRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) && File.Exists(fullPath))
                File.Delete(fullPath);
        }

        var deletedEntries = await userDataService.DeleteExpiredHistoryAsync(cutoff, cancellationToken);
        logger.LogInformation("Deleted {Count} expired history records", deletedEntries);
    }
}