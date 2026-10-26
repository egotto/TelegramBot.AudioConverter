using Microsoft.EntityFrameworkCore;
using TelegramAudioBot.Models;

namespace TelegramAudioBot.Services;

public class TelegramAudioBotDbContext(DbContextOptions<TelegramAudioBotDbContext> options) : DbContext(options)
{
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<HistoryEntry> HistoryEntries => Set<HistoryEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserSettings>()
            .HasKey(settings => settings.Id);
        modelBuilder.Entity<UserSettings>()
            .HasIndex(settings => settings.UserId)
            .IsUnique();
        modelBuilder.Entity<UserSettings>()
            .ToTable(table => table.HasCheckConstraint(
                "CK_UserSettings_AudioOptions",
                "(\"AudioCodec\" = 'mp3' AND \"AudioBitrateKbps\" BETWEEN 32 AND 320) OR " +
                "(\"AudioCodec\" = 'aac' AND \"AudioBitrateKbps\" BETWEEN 16 AND 512)"));
        modelBuilder.Entity<UserSettings>()
            .ToTable(table => table.HasCheckConstraint(
                "CK_UserSettings_AudioSampleRateHz",
                "\"AudioSampleRateHz\" IN (22050, 32000, 44100, 48000)"));
        modelBuilder.Entity<HistoryEntry>().HasKey(entry => entry.Id);
        modelBuilder.Entity<HistoryEntry>().HasIndex(entry => entry.OccurredAt);
        modelBuilder.Entity<HistoryEntry>().HasIndex(entry => entry.UserId);
    }
}

public interface IUserDataService
{
    Task<UserSettings> GetOrCreateSettingsAsync(long telegramUserId, CancellationToken cancellationToken = default);
    Task<UserSettings> UpdateSettingsAsync(long telegramUserId, string audioCodec, int bitrateKbps, int sampleRateHz, CancellationToken cancellationToken = default);
    Task RecordEventAsync(long telegramUserId, string type, string? content = null, string? relativeFilePath = null, CancellationToken cancellationToken = default);
    Task<int> DeleteExpiredHistoryAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
}

public class UserDataService(TelegramAudioBotDbContext dbContext) : IUserDataService
{
    public async Task<UserSettings> GetOrCreateSettingsAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        var settings = await dbContext.UserSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == telegramUserId, cancellationToken);

        if (settings is not null)
            return settings;

        settings = new UserSettings { UserId = telegramUserId };
        dbContext.UserSettings.Add(settings);
        await dbContext.SaveChangesAsync(cancellationToken);
        return settings;
    }

    public async Task<UserSettings> UpdateSettingsAsync(long telegramUserId, string audioCodec, int bitrateKbps, int sampleRateHz, CancellationToken cancellationToken = default)
    {
        AudioSettingsValidator.Validate(audioCodec, bitrateKbps, sampleRateHz);
        var settings = await dbContext.UserSettings
            .SingleOrDefaultAsync(item => item.UserId == telegramUserId, cancellationToken);

        if (settings is null)
        {
            settings = new UserSettings { UserId = telegramUserId };
            dbContext.UserSettings.Add(settings);
        }

        settings.AudioCodec = audioCodec.Trim().ToLowerInvariant();
        settings.AudioBitrateKbps = bitrateKbps;
        settings.AudioSampleRateHz = sampleRateHz;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return settings;
    }

    public async Task RecordEventAsync(long telegramUserId, string type, string? content = null, string? relativeFilePath = null, CancellationToken cancellationToken = default)
    {
        dbContext.HistoryEntries.Add(new HistoryEntry
        {
            UserId = telegramUserId,
            Type = type,
            Content = content,
            RelativeFilePath = relativeFilePath
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> DeleteExpiredHistoryAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        return await dbContext.HistoryEntries
            .Where(entry => entry.OccurredAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

}