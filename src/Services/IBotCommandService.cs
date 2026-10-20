using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace TelegramAudioBot.Services;

/// <summary>
/// Service for bot commands management
/// </summary>
public interface IBotCommandService
{
    /// <summary>
    /// Sets up bot commands
    /// </summary>
    Task SetupCommandsAsync();

    /// <summary>
    /// Sets webhook for the bot
    /// </summary>
    Task<bool> SetWebhookAsync(string url);

    /// <summary>
    /// Removes webhook (enables polling)
    /// </summary>
    Task<bool> RemoveWebhookAsync();

    /// <summary>
    /// Gets webhook info
    /// </summary>
    Task<string> GetWebhookInfoAsync();
}

public class BotCommandService(
    ITelegramBotClient _botClient, 
    ILogger<BotCommandService> _logger) : IBotCommandService
{
    public async Task SetupCommandsAsync()
    {
        try
        {
            var commands = new BotCommand[]
            {
                new() { Command = "start", Description = "Start using the bot" },
                new() { Command = "help", Description = "Show commands help" },
                new() { Command = "settings", Description = "Audio processing parameters" },
                new() { Command = "cancel", Description = "Cancel current processing" }
            };

            await _botClient.SetMyCommands(commands);
            _logger.LogInformation("Bot commands set successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting up bot commands");
        }
    }

    public async Task<bool> SetWebhookAsync(string url)
    {
        try
        {
            _logger.LogInformation("Setting webhook to: {Url}", url);

            var allowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery };
            await _botClient.SetWebhook(
                url,
                maxConnections: 40,
                allowedUpdates: allowedUpdates,
                dropPendingUpdates: true);
            _logger.LogInformation("Webhook set successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting webhook");
            return false;
        }
    }

    public async Task<bool> RemoveWebhookAsync()
    {
        try
        {
            _logger.LogInformation("Removing webhook");
            await _botClient.DeleteWebhook(dropPendingUpdates: true);
            _logger.LogInformation("Webhook removed successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing webhook");
            return false;
        }
    }

    public async Task<string> GetWebhookInfoAsync()
    {
        try
        {
            var info = await _botClient.GetWebhookInfo();

            return $"Webhook: {info.Url ?? "Not set"}\n" +
                   $"Pending updates: {info.PendingUpdateCount}\n" +
                   $"Last error: {info.LastErrorMessage ?? "None"}\n" +
                   $"Last error date: {(info.LastErrorDate.HasValue ? info.LastErrorDate.Value : "None")}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting webhook info");
            return "Error getting webhook info";
        }
    }
}
