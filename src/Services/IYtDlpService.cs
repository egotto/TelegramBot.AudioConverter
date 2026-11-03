namespace TelegramAudioBot.Services;

public interface IYtDlpService
{
    Task<YtDlpDownloadResult> DownloadAudioAsync(
        string url,
        string outputDirectory,
        long maxFileSizeMb,
        Func<YtDlpProgress, CancellationToken, Task>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record YtDlpDownloadResult(
    IReadOnlyList<string> Files,
    IReadOnlyList<string> Errors,
    string? Error = null);

public sealed record YtDlpProgress(
    double Percent,
    string Speed,
    string Eta,
    int? PlaylistIndex,
    int? PlaylistCount);

public sealed class YtDlpOptions
{
    public string ExecutablePath { get; set; } = "yt-dlp";
    public string FfmpegLocation { get; set; } = "/usr/bin";
    public int MaxConcurrentDownloads { get; set; } = 2;
    public int MaxPlaylistItems { get; set; }
    public int ProgressUpdateIntervalSeconds { get; set; } = 5;
}