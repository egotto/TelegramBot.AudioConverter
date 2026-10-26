using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramAudioBot.Models;

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
    ITelegramBotService _telegramService,
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
            var commandText = message.Text?.Trim().Split(' ')[0].ToLower();

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

            // Handle video files
            if (message.Video is not { } video)
            {
                await _telegramService.SendMessageAsync(
                    message.Chat.Id,
                    "❌ Please send a video file or enter /help command for help.",
                    cancellationToken,
                    userId);
                return;
            }

            await ProcessVideoAsync(message, video, userId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing update");
        }
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
                $"audio_{message.MessageId}{settings.FileExtension}",
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
            "I will help you extract audio from video in just a few seconds.\n\n" +
            "**How to use:**\n" +
            "1. Send me a video file (MP4, WebM, MOV, etc.)\n" +
            "2. I will convert it to audio in your selected format\n" +
            "3. You will get the ready audio\n\n" +
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
            "**How the bot works:**\n" +
            "Just send a video file, and the bot will automatically:\n" +
            "✅ Upload the video\n" +
            "✅ Extract audio (AAC, 128 kbps, 44.1 kHz)\n" +
            "✅ Send you the ready file\n\n" +
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
        var message = "⏹️ Cancel current processing command.";
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
