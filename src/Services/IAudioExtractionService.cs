namespace TelegramAudioBot.Services;

/// <summary>
/// Service for extracting audio from video files using FFmpeg
/// </summary>
public interface IAudioExtractionService
{
    /// <summary>
    /// Extracts audio from a video file
    /// </summary>
    /// <param name="inputPath">Path to input video file</param>
    /// <param name="outputPath">Path to output audio file</param>
    /// <param name="settings">Per-user audio conversion settings</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if extraction was successful</returns>
    Task<bool> ExtractAudioAsync(string inputPath, string outputPath, TelegramAudioBot.Models.UserSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about a media file
    /// </summary>
    /// <param name="filePath">Path to media file</param>
    /// <returns>Media information</returns>
    Task<MediaInfo?> GetMediaInfoAsync(string filePath);
}

public class MediaInfo
{
    public TimeSpan Duration { get; set; }
    public string? AudioCodec { get; set; }
    public int? AudioBitrate { get; set; }
    public int? SampleRate { get; set; }
}
