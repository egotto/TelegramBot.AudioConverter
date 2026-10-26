namespace TelegramAudioBot.Models;

public class UserSettings
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long UserId { get; set; }
    public string AudioCodec { get; set; } = "mp3";
    public int AudioBitrateKbps { get; set; } = 128;
    public int AudioSampleRateHz { get; set; } = 44100;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string FileExtension => AudioCodec.ToLowerInvariant() switch
    {
        "mp3" => ".mp3",
        "aac" => ".m4a",
        _ => throw new InvalidOperationException($"Unsupported audio codec: {AudioCodec}")
    };
}