using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;

namespace TelegramAudioBot.Services;

/// <summary>
/// Service for handling Telegram bot updates via Long Polling
/// </summary>
public interface IBotPollingService
{
    /// <summary>
    /// Start receiving updates from Telegram using Long Polling
    /// </summary>
    Task StartPollingAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stop polling
    /// </summary>
    Task StopPollingAsync();
}

public class BotPollingService(
    ITelegramBotClient _botClient,
    IServiceProvider _serviceProvider,
    ILogger<BotPollingService> _logger) : IBotPollingService
{
    private CancellationTokenSource? _cts;

    public async Task StartPollingAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            _logger.LogInformation("Starting Telegram bot Long Polling...");

            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = []
            };

            // Start receiving updates from Telegram
            // Signature: ReceiveAsync(Func<ITelegramBotClient, Update, CancellationToken, Task>, Func<ITelegramBotClient, Exception, CancellationToken, Task>, ReceiverOptions, CancellationToken)
            await _botClient.ReceiveAsync(
                HandleUpdateAsync,
                HandleErrorAsync,
                receiverOptions,
                _cts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Long Polling was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Long Polling");
            throw;
        }
    }

    public async Task StopPollingAsync()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
            _logger.LogInformation("Long Polling stopped");
            await Task.CompletedTask;
        }
    }

    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        // Create a new scope for each update to resolve Scoped services
        using var scope = _serviceProvider.CreateScope();
        var updateProcessor = scope.ServiceProvider.GetRequiredService<IUpdateProcessor>();
        await updateProcessor.ProcessUpdateAsync(update, cancellationToken);
    }

    private async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Telegram polling error");
        await Task.CompletedTask;
    }
}



