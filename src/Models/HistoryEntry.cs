namespace TelegramAudioBot.Models;

public class HistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public long UserId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? Content { get; set; }
    public string? RelativeFilePath { get; set; }
}