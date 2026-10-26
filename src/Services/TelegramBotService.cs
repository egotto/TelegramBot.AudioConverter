using Telegram.Bot;
using Telegram.Bot.Types;

namespace TelegramAudioBot.Services;

/// <summary>
/// Service for handling Telegram bot interactions
/// </summary>
public interface ITelegramBotService
{
    /// <summary>
    /// Sends an audio file to a Telegram user
    /// </summary>
    Task<Message?> SendAudioAsync(long chatId, Stream audioStream, string fileName, CancellationToken cancellationToken = default, long? userId = null);

    /// <summary>
    /// Sends a text message to a Telegram user
    /// </summary>
    Task<Message?> SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default, long? userId = null);

    /// <summary>
    /// Gets the current bot info
    /// </summary>
    Task<User?> GetMeAsync();
}

public class TelegramBotService(
    ITelegramBotClient _botClient, 
    ILogger<TelegramBotService> _logger,
    IUserDataService _userDataService) : ITelegramBotService
{
    public async Task<Message?> SendAudioAsync(long chatId, Stream audioStream, string fileName, CancellationToken cancellationToken = default, long? userId = null)
    {
        try
        {
            var inputFile = InputFile.FromStream(audioStream, fileName);
            var message = await _botClient.SendAudio(chatId, inputFile, cancellationToken: cancellationToken);
            _logger.LogInformation("Audio sent successfully to chat {ChatId}", chatId);
            return message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending audio to chat {ChatId}", chatId);
            return null;
        }
    }

    public async Task<Message?> SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default, long? userId = null)
    {
        try
        {
            var message = await _botClient.SendMessage(chatId, text, cancellationToken: cancellationToken);
            if (userId.HasValue)
            {
                await _userDataService.RecordEventAsync(
                    userId.Value,
                    "TextMessageSent",
                    text,
                    cancellationToken: cancellationToken);
            }
            return message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message to chat {ChatId}", chatId);
            return null;
        }
    }

    public async Task<User?> GetMeAsync()
    {
        try
        {
            return await _botClient.GetMe();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting bot info");
            return null;
        }
    }
}
