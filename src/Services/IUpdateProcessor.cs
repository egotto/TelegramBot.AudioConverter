using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramAudioBot.Models;
using System.Text.RegularExpressions;

namespace TelegramAudioBot.Services;

/// <summary>
/// Service for processing Telegram updates
/// </summary>
public interface IUpdateProcessor
{
    /// <summary>
    /// Process an update from Telegram
    /// </summary>
    Task ProcessUpdateAsync(Update update, CancellationToken cancellationToken);
}

public class UpdateProcessor(
    ITelegramBotClient _botClient,
    IAudioExtractionService _audioService,
    IYtDlpService _ytDlpService,
    ITelegramBotService _telegramService,
    IActiveProcessingRegistry _activeProcessingRegistry,
    ILogger<UpdateProcessor> _logger,
    IUserDataService _userDataService,
    IConfiguration configuration) : IUpdateProcessor
{
    private readonly string _tempFilesPath = configuration["TEMP_FILES_PATH"] ?? Path.Combine(Path.GetTempPath(), "audio_processing");
    private readonly string _sharedDirectory = configuration["SHARED_DIRECTORY"] ?? throw new InvalidOperationException("SHARED_DIRECTORY not configured");
    private readonly string _botToken = configuration["TELEGRAM_BOT_TOKEN"] ?? throw new InvalidOperationException("TELEGRAM_BOT_TOKEN not configured");
    private readonly long _maxFileSizeMb = configuration.GetValue<long>("MAX_FILE_SIZE_MB", 2048);

    public async Task ProcessUpdateAsync(Update update, CancellationToken cancellationToken)
    {
        try
        {
            if (update.Message is not { } message)
                return;

            if (message.From is not { } sender)
                return;

            var userId = sender.Id;
            var messageType = GetMessageType(message);
            await _userDataService.GetOrCreateSettingsAsync(userId, cancellationToken);
            await _userDataService.RecordEventAsync(
                userId,
                $"{messageType}Received",
                message.Text ?? message.Caption,
                cancellationToken: cancellationToken);
            _logger.LogInformation("Received message from {UserId} in chat {ChatId}", userId, message.Chat.Id);

            // Handle commands
            var commandText = message.Text?.Trim().Split(' ')[0].Split('@')[0].ToLowerInvariant();

            if (commandText == CommandConstants.Start)
            {
                await HandleStartCommand(message.Chat.Id, userId, cancellationToken);
                return;
            }

            if (commandText == CommandConstants.Help)
            {
                await HandleHelpCommand(message.Chat.Id, userId, cancellationToken);
                return;
            }

            if (commandText == CommandConstants.Settings)
            {
                await HandleSettingsCommand(message.Chat.Id, userId, cancellationToken);
                return;
            }

            if (commandText == CommandConstants.Cancel)
            {
                await HandleCancelCommand(message.Chat.Id, userId, cancellationToken);
                return;
            }

            if (message.Video is { } video)
            {
                await RunMediaJobAsync(
                    message,
                    userId,
                    cancellationToken,
                    jobToken => ProcessVideoAsync(message, video, userId, jobToken));
                return;
            }

            var url = ExtractUrl(message.Text) ?? ExtractUrl(message.Caption);
            if (url is not null)
            {
                await RunMediaJobAsync(
                    message,
                    userId,
                    cancellationToken,
                    jobToken => ProcessUrlAsync(message, url, userId, jobToken));
                return;
            }

            await _telegramService.SendMessageAsync(
                message.Chat.Id,
                "❌ Please send a video file or a supported media link, or enter /help for help.",
                cancellationToken,
                userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing update");
        }
    }

    private async Task RunMediaJobAsync(
        Message message,
        long userId,
        CancellationToken hostToken,
        Func<CancellationToken, Task> processJob)
    {
        if (!_activeProcessingRegistry.TryStart(message.Chat.Id, userId, hostToken, out var jobCancellation))
        {
            await _telegramService.SendMessageAsync(
                message.Chat.Id,
                "⚠️ You already have a processing task. Use /cancel before starting another one.",
                hostToken,
                userId);
            return;
        }

        try
        {
            await processJob(jobCancellation.Token);
        }
        catch (OperationCanceledException) when (jobCancellation.IsCancellationRequested)
        {
            _logger.LogInformation("Media task cancelled for user {UserId} in chat {ChatId}", userId, message.Chat.Id);
        }
        finally
        {
            _activeProcessingRegistry.Complete(message.Chat.Id, userId, jobCancellation);
        }
    }

    private async Task ProcessUrlAsync(Message message, string url, long userId, CancellationToken cancellationToken)
    {
        var chatId = message.Chat.Id;
        var downloadDirectory = Path.Combine(_tempFilesPath, $"download_{Guid.NewGuid():N}");
        Message? progressMessage = null;
        try
        {
            Directory.CreateDirectory(_tempFilesPath);
            progressMessage = await _telegramService.SendMessageAsync(
                chatId,
                "⏳ Downloading audio...",
                cancellationToken,
                userId);

            var result = await _ytDlpService.DownloadAudioAsync(
                url,
                downloadDirectory,
                _maxFileSizeMb,
                async (progress, token) =>
                {
                    if (progressMessage is null)
                        return;

                    var status = progress.PlaylistIndex.HasValue && progress.PlaylistCount.HasValue
                        ? $"⏳ Downloading track {progress.PlaylistIndex}/{progress.PlaylistCount}: {progress.Percent:0}%"
                        : $"⏳ Downloading audio: {progress.Percent:0}%";
                    if (!string.IsNullOrWhiteSpace(progress.Speed))
                        status += $"\nSpeed: {progress.Speed}";
                    if (!string.IsNullOrWhiteSpace(progress.Eta))
                        status += $" | ETA: {progress.Eta}";

                    await _telegramService.EditMessageTextAsync(
                        chatId,
                        progressMessage.MessageId,
                        status,
                        token);
                },
                cancellationToken);

            if (result.Files.Count == 0)
            {
                CleanupDirectory(downloadDirectory, recursive: true);
                var errorMessage = $"❌ Could not process this link. {result.Error ?? "yt-dlp could not download any audio."}";
                if (progressMessage is not null)
                    await _telegramService.EditMessageTextAsync(chatId, progressMessage.MessageId, errorMessage, cancellationToken);
                else
                    await _telegramService.SendMessageAsync(chatId, errorMessage, cancellationToken, userId);
                return;
            }

            var settings = await _userDataService.GetOrCreateSettingsAsync(userId, cancellationToken);
            var errors = result.Errors.ToList();
            var sentCount = 0;

            foreach (var downloadedPath in result.Files)
            {
                var outputPath = downloadedPath;
                var keepOutput = false;
                try
                {
                    var mediaInfo = await _audioService.GetMediaInfoAsync(downloadedPath);
                    if (!MatchesSettings(downloadedPath, mediaInfo, settings))
                    {
                        outputPath = Path.Combine(_tempFilesPath, $"audio_{Guid.NewGuid():N}{settings.FileExtension}");
                        if (!await _audioService.ExtractAudioAsync(downloadedPath, outputPath, settings, cancellationToken))
                        {
                            errors.Add($"{Path.GetFileName(downloadedPath)}: audio conversion failed.");
                            continue;
                        }
                    }

                    await using var audioStream = File.OpenRead(outputPath);
                    var sentMessage = await _telegramService.SendAudioAsync(
                        chatId,
                        audioStream,
                        $"{Path.GetFileNameWithoutExtension(downloadedPath)}{settings.FileExtension}",
                        cancellationToken,
                        userId);

                    if (sentMessage is null)
                    {
                        errors.Add($"{Path.GetFileName(downloadedPath)}: Telegram could not send the audio file.");
                        continue;
                    }

                    keepOutput = true;
                    sentCount++;
                    await _userDataService.RecordEventAsync(
                        userId,
                        "AudioUploaded",
                        Path.GetFileName(outputPath),
                        Path.GetRelativePath(_tempFilesPath, outputPath),
                        cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger.LogError(exception, "Failed to process downloaded audio {FilePath}", downloadedPath);
                    errors.Add($"{Path.GetFileName(downloadedPath)}: processing failed.");
                }
                finally
                {
                    if (!keepOutput)
                        CleanupFiles(outputPath);
                    if (!string.Equals(outputPath, downloadedPath, StringComparison.Ordinal))
                        CleanupFiles(downloadedPath);
                }
            }

            CleanupDirectory(downloadDirectory);
            var summary = sentCount > 0
                ? $"✅ Sent audio files: {sentCount}."
                : "❌ No audio files could be sent.";
            if (errors.Count > 0)
            {
                summary += "\n\nSkipped items or errors:\n" +
                           string.Join("\n", errors.Take(8).Select(error => $"• {error}"));
                if (errors.Count > 8)
                    summary += $"\n• And {errors.Count - 8} more.";
            }

            await _telegramService.SendMessageAsync(chatId, summary, cancellationToken, userId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CleanupDirectory(downloadDirectory, recursive: true);
            if (progressMessage is not null)
                await _telegramService.EditMessageTextAsync(chatId, progressMessage.MessageId, "⏹️ Download cancelled.", CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error processing media link for chat {ChatId}", chatId);
            const string errorMessage = "❌ An error occurred while processing this link. Please try again later.";
            if (progressMessage is not null)
                await _telegramService.EditMessageTextAsync(chatId, progressMessage.MessageId, errorMessage, cancellationToken);
            else
                await _telegramService.SendMessageAsync(chatId, errorMessage, cancellationToken, userId);
        }
    }

    private static string? ExtractUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var match = Regex.Match(text, @"https?://[^\s<>]+", RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;

        var url = match.Value.TrimEnd('.', ',', '!', '?', ')', ']', '}', '"', '\'');
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? url
            : null;
    }

    private static bool MatchesSettings(string filePath, MediaInfo? mediaInfo, UserSettings settings)
    {
        var expectedCodec = settings.AudioCodec.Equals("aac", StringComparison.OrdinalIgnoreCase) ? "aac" : "mp3";
        var expectedBitrate = settings.AudioBitrateKbps * 1000;
        var bitrateTolerance = Math.Max(2000, expectedBitrate / 20);

        return mediaInfo is not null &&
               string.Equals(mediaInfo.AudioCodec, expectedCodec, StringComparison.OrdinalIgnoreCase) &&
               Math.Abs((mediaInfo.AudioBitrate ?? 0) - expectedBitrate) <= bitrateTolerance &&
               mediaInfo.SampleRate == settings.AudioSampleRateHz &&
               string.Equals(Path.GetExtension(filePath), settings.FileExtension, StringComparison.OrdinalIgnoreCase);
    }

    private async Task ProcessVideoAsync(Message message, Video video, long userId, CancellationToken cancellationToken)
    {
        var chatId = message.Chat.Id;

        var maxFileSizeBytes = _maxFileSizeMb * 1024 * 1024;

        if (video.FileSize.HasValue && video.FileSize.Value > maxFileSizeBytes)
        {
            await _telegramService.SendMessageAsync(
                chatId,
                $"❌ Video file is too large for processing.\n\n" +
                $"Limit: {_maxFileSizeMb} MB\n" +
                $"Your file size: {video.FileSize.Value / (1024 * 1024)} MB",
                cancellationToken,
                userId);
            return;
        }

        await _telegramService.SendMessageAsync(
            chatId,
            "⏳ Processing video...",
            cancellationToken,
            userId);

        string videoFilePath = string.Empty;
        string audioFilePath = string.Empty;
        var audioUploaded = false;
        try
        {
            // Create temp directory if needed
            if (!Directory.Exists(_tempFilesPath))
                Directory.CreateDirectory(_tempFilesPath);

            var sourceFilePath = await DownloadFileAsync(video.FileId, cancellationToken);
            videoFilePath = await CopyToManagedStorageAsync(sourceFilePath, "video", Path.GetExtension(sourceFilePath), cancellationToken);
            var sourceFileName = !string.IsNullOrWhiteSpace(video.FileName)
                ? video.FileName
                : Path.GetFileName(sourceFilePath);
            await _userDataService.RecordEventAsync(
                userId,
                "VideoDownloaded",
                message.Caption,
                Path.GetFileName(videoFilePath),
                cancellationToken);

            _logger.LogInformation("Video downloaded to {FilePath}", videoFilePath);

            // Extract audio
            var settings = await _userDataService.GetOrCreateSettingsAsync(userId, cancellationToken);
            audioFilePath = Path.Combine(_tempFilesPath, $"audio_{Guid.NewGuid():N}{settings.FileExtension}");
            var success = await _audioService.ExtractAudioAsync(videoFilePath, audioFilePath, settings, cancellationToken);
            if (!success)
            {
                await _telegramService.SendMessageAsync(chatId, "❌ Error extracting audio.", cancellationToken, userId);
                return;
            }

            // Send audio file
            using var audioStream = File.OpenRead(audioFilePath);
            var audioBytes = new byte[audioStream.Length];
            _ = await audioStream.ReadAsync(audioBytes, 0, audioBytes.Length, cancellationToken);

            using var memoryStream = new MemoryStream(audioBytes);
            var sentMessage = await _telegramService.SendAudioAsync(
                chatId,
                memoryStream,
                BuildAudioFileName(sourceFileName, settings.FileExtension),
                cancellationToken,
                userId);

            if (sentMessage != null)
            {
                await _userDataService.RecordEventAsync(
                    userId,
                    "AudioUploaded",
                    Path.GetFileName(audioFilePath),
                    Path.GetFileName(audioFilePath),
                    cancellationToken: cancellationToken);
                audioUploaded = true;
                await _telegramService.SendMessageAsync(chatId, "✅ Audio sent successfully!", cancellationToken, userId);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing video");
            await _telegramService.SendMessageAsync(chatId, "❌ An error occurred while processing video.", cancellationToken, userId);
        }
        finally
        {
            if (!audioUploaded)
                CleanupFiles(audioFilePath);
        }
    }

    private async Task HandleStartCommand(long chatId, long userId, CancellationToken cancellationToken)
    {
        var message = "👋 **Welcome to Audio Bot!**\n\n" +
            "I will extract audio from videos and supported media links.\n\n" +
            "⚠️ This project is for educational purposes only, to explore Telegram bots with .NET. Only download content you have permission to download, and follow the platform's terms and the laws in your country.\n\n" +
            "**How to use:**\n" +
            "1. Send a video file or a supported media link\n" +
            "2. For playlists, I will send each available audio track\n" +
            "3. I will use your selected audio settings\n\n" +
            $"Use {CommandConstants.Help} for the list of commands.";

        await _telegramService.SendMessageAsync(chatId, message, cancellationToken, userId);
    }

    private async Task HandleHelpCommand(long chatId, long userId, CancellationToken cancellationToken)
    {
        var helpMessage = "📋 **Available commands:**\n\n" +
            $"{CommandConstants.Start} - Start using\n" +
            $"{CommandConstants.Help} - Show this help\n" +
            $"{CommandConstants.Settings} - Audio parameters\n" +
            $"{CommandConstants.Cancel} - Cancel processing\n\n" +
            "⚠️ This project is for educational purposes only, to explore Telegram bots with .NET. Only download content you have permission to download, and follow the platform's terms and the laws in your country.\n\n" +
            "**How the bot works:**\n" +
            "Send a video file or a supported media link. Playlist tracks are processed independently, and any skipped items or errors are reported.\n\n" +
            "**Supported formats:**\n" +
            "MP4, WebM, MOV, AVI, FLV, MKV, etc.";

        await _telegramService.SendMessageAsync(chatId, helpMessage, cancellationToken, userId);
    }

    private async Task HandleSettingsCommand(long chatId, long userId, CancellationToken cancellationToken)
    {
        var settings = await _userDataService.GetOrCreateSettingsAsync(userId, cancellationToken);
        var settingsMessage = "⚙️ **Current audio parameters:**\n\n" +
            $"🔊 Codec: {settings.AudioCodec.ToUpperInvariant()}\n" +
            $"📊 Bitrate: {settings.AudioBitrateKbps} kbps\n" +
            $"🎵 Frequency: {settings.AudioSampleRateHz / 1000.0:F1} kHz\n\n" +
            "💡 *These parameters are optimal for most applications and provide a good balance between quality and file size.*";

        await _telegramService.SendMessageAsync(chatId, settingsMessage, cancellationToken, userId);
    }

    private async Task HandleCancelCommand(long chatId, long userId, CancellationToken cancellationToken)
    {
        var message = _activeProcessingRegistry.Cancel(chatId, userId)
            ? "⏹️ Cancellation requested. The current task is stopping."
            : "ℹ️ There is no active task to cancel.";
        await _telegramService.SendMessageAsync(chatId, message, cancellationToken, userId);
    }

    private async Task<string> DownloadFileAsync(string fileId, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Getting file info for fileId: {FileId}", fileId);
            var file = await _botClient.GetFile(fileId, cancellationToken);

            if (file?.FilePath == null)
            {
                _logger.LogError("File path is null for fileId: {FileId}", fileId);
                throw new InvalidOperationException("Failed to get file path from Telegram");
            }

            _logger.LogInformation("File path retrieved: {FilePath}", file.FilePath);

            var downloadPath = Path.IsPathRooted(file.FilePath)
                ? file.FilePath
                : Path.Combine(_sharedDirectory, _botToken, file.FilePath);
            if (!Directory.Exists(Path.GetDirectoryName(downloadPath)) || !File.Exists(downloadPath))
            {
                throw new FileNotFoundException($"File not found in shared directory: {downloadPath}");
            }

            return downloadPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download file from fileId: {FileId}", fileId);
            throw;
        }
    }

    private void CleanupFiles(params string[] filePaths)
    {
        foreach (var path in filePaths)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error deleting file {FilePath}", path);
            }
        }
    }

    private static string BuildAudioFileName(string sourceFileName, string extension)
    {
        var normalizedFileName = sourceFileName.Replace('\\', '/');
        var name = Path.GetFileNameWithoutExtension(normalizedFileName);
        var invalidCharacters = Path.GetInvalidFileNameChars().Concat("<>:\"/\\|?*".ToCharArray()).ToHashSet();
        var safeName = new string(name.Select(character => char.IsControl(character) || invalidCharacters.Contains(character) ? '_' : character).ToArray())
            .Trim()
            .Trim('.');

        if (safeName.Length == 0)
            safeName = "audio";
        if (safeName.Length > 150)
            safeName = safeName[..150].TrimEnd();

        return $"{safeName}{extension}";
    }

    private void CleanupDirectory(string directoryPath, bool recursive = false)
    {
        try
        {
            if (Directory.Exists(directoryPath) && (recursive || !Directory.EnumerateFileSystemEntries(directoryPath).Any()))
                Directory.Delete(directoryPath, recursive);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error deleting directory {DirectoryPath}", directoryPath);
        }
    }

    private async Task<string> CopyToManagedStorageAsync(string sourcePath, string prefix, string extension, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_tempFilesPath);
        var destinationPath = Path.Combine(_tempFilesPath, $"{prefix}_{Guid.NewGuid():N}{extension}");
        try
        {
            await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            await using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await source.CopyToAsync(destination, cancellationToken);
            return destinationPath;
        }
        catch
        {
            CleanupFiles(destinationPath);
            throw;
        }
    }

    private static string GetMessageType(Message message)
    {
        if (message.Text is not null)
            return "TextMessage";
        if (message.Video is not null)
            return "VideoMessage";
        if (message.Document is not null)
            return "DocumentMessage";
        if (message.Audio is not null)
            return "AudioMessage";
        if (message.Photo is not null)
            return "PhotoMessage";
        if (message.Voice is not null)
            return "VoiceMessage";
        if (message.VideoNote is not null)
            return "VideoNoteMessage";
        if (message.Sticker is not null)
            return "StickerMessage";

        return "OtherMessage";
    }
}
