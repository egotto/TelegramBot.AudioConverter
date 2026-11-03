using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace TelegramAudioBot.Services;

public sealed class YtDlpService : IYtDlpService, IDisposable
{
    private static readonly Regex ProgressRegex = new(
        @"(?<percent>\d+(?:\.\d+)?)%\|(?<speed>[^|]*)\|(?<eta>.*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly YtDlpOptions _options;
    private readonly ILogger<YtDlpService> _logger;
    private readonly SemaphoreSlim _downloadSemaphore;

    public YtDlpService(IOptions<YtDlpOptions> options, ILogger<YtDlpService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _downloadSemaphore = new SemaphoreSlim(
            Math.Clamp(_options.MaxConcurrentDownloads, 1, 32),
            Math.Clamp(_options.MaxConcurrentDownloads, 1, 32));
    }

    public async Task<YtDlpDownloadResult> DownloadAudioAsync(
        string url,
        string outputDirectory,
        long maxFileSizeMb,
        Func<YtDlpProgress, CancellationToken, Task>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return new YtDlpDownloadResult([], [], "Only valid HTTP(S) links are supported.");
        }

        await _downloadSemaphore.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(outputDirectory);

            var audioResult = await RunYtDlpAsync(url, outputDirectory, maxFileSizeMb, extractAudio: true, progress, cancellationToken);
            if (audioResult.Files.Count > 0)
                return audioResult;

            var videoResult = await RunYtDlpAsync(url, outputDirectory, maxFileSizeMb, extractAudio: false, progress, cancellationToken);
            var errors = audioResult.Errors.Concat(videoResult.Errors).Distinct().Take(10).ToArray();
            return videoResult.Files.Count > 0
                ? new YtDlpDownloadResult(videoResult.Files, errors)
                : new YtDlpDownloadResult([], errors, videoResult.Error ?? audioResult.Error);
        }
        finally
        {
            _downloadSemaphore.Release();
        }
    }

    private async Task<YtDlpDownloadResult> RunYtDlpAsync(
        string url,
        string outputDirectory,
        long maxFileSizeMb,
        bool extractAudio,
        Func<YtDlpProgress, CancellationToken, Task>? progress,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _options.ExecutablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        var arguments = new List<string>
        {
            "--ignore-errors",
            "--no-abort-on-error",
            "--newline",
            "--progress",
            "--progress-template", "download:%(progress._percent_str)s|%(progress._speed_str)s|%(progress._eta_str)s",
            "--max-filesize", $"{maxFileSizeMb}M",
            "--paths", outputDirectory,
            "--output", "%(autonumber)03d-%(id)s.%(ext)s",
            "--print", "after_move:filepath"
        };

        if (!string.IsNullOrWhiteSpace(_options.FfmpegLocation))
            arguments.AddRange(["--ffmpeg-location", _options.FfmpegLocation]);

        if (_options.MaxPlaylistItems > 0)
            arguments.AddRange(["--playlist-end", _options.MaxPlaylistItems.ToString(CultureInfo.InvariantCulture)]);

        if (extractAudio)
        {
            arguments.Add("--extract-audio");
            arguments.AddRange(["--audio-format", "best"]);
        }
        else
        {
            arguments.AddRange(["--format", "bestvideo*[acodec!=none]/best"]);
        }

        arguments.Add(url);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        var started = false;
        try
        {
            process.Start();
            started = true;
            var outputLines = new List<string>();
            var errors = new Queue<string>();
            var outputTask = ReadOutputAsync(process, outputLines);
            var errorTask = ReadErrorsAsync(process, errors, progress, cancellationToken);

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                throw;
            }

            await Task.WhenAll(outputTask, errorTask);

            var files = outputLines
                .Select(path => path.Trim())
                .Where(path => path.Length > 0)
                .Select(path => Path.IsPathRooted(path) || File.Exists(path) ? path : Path.Combine(outputDirectory, path))
                .Where(File.Exists)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var errorMessages = errors.ToArray();

            return files.Length > 0
                ? new YtDlpDownloadResult(files, errorMessages)
                : new YtDlpDownloadResult([], errorMessages, errorMessages.FirstOrDefault() ?? "yt-dlp could not download this link.");
        }
        catch (OperationCanceledException)
        {
            if (started && !process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "yt-dlp failed for {Url}", url);
            if (started && !process.HasExited)
                process.Kill(entireProcessTree: true);
            return new YtDlpDownloadResult([], [], "The link could not be downloaded.");
        }
    }

    private async Task ReadOutputAsync(Process process, List<string> outputLines)
    {
        while (await process.StandardOutput.ReadLineAsync() is { } line)
        {
            if (File.Exists(line) || Path.IsPathRooted(line))
                outputLines.Add(line);
        }
    }

    private async Task ReadErrorsAsync(
        Process process,
        Queue<string> errors,
        Func<YtDlpProgress, CancellationToken, Task>? progress,
        CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.ProgressUpdateIntervalSeconds));
        var lastUpdate = DateTimeOffset.MinValue;

        while (await process.StandardError.ReadLineAsync() is { } line)
        {
            var match = ProgressRegex.Match(line);
            if (match.Success &&
                double.TryParse(match.Groups["percent"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var percent))
            {
                if (progress is not null &&
                    (lastUpdate == DateTimeOffset.MinValue || DateTimeOffset.UtcNow - lastUpdate >= interval))
                {
                    await progress(new YtDlpProgress(
                        percent,
                        match.Groups["speed"].Value.Trim(),
                        match.Groups["eta"].Value.Trim()), cancellationToken);
                    lastUpdate = DateTimeOffset.UtcNow;
                }
            }

            if (line.Contains("ERROR:", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("WARNING:", StringComparison.OrdinalIgnoreCase))
            {
                if (errors.Count == 10)
                    errors.Dequeue();
                errors.Enqueue(line);
            }
        }
    }

    public void Dispose() => _downloadSemaphore.Dispose();
}