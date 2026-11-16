namespace TelegramAudioBot.Models;

public class UserSettings
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long UserId { get; set; }
    public string AudioFormat { get; set; } = "mp3";
    public string AudioCodec { get; set; } = "libmp3lame";
    public int AudioBitrateKbps { get; set; } = 128;
    public int AudioSampleRateHz { get; set; } = 44100;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string FileExtension => AudioFormat.ToLowerInvariant() switch
    {
        "mp3" => ".mp3",
        "m4a" => ".m4a",
        _ => throw new InvalidOperationException($"Unsupported audio format: {AudioFormat}")
    };
}