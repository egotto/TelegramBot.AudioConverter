using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using System.Threading.Channels;

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
    IConfiguration _configuration,
    ILogger<BotPollingService> _logger) : IBotPollingService
{
    private CancellationTokenSource? _cts;

    public async Task StartPollingAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var workerCount = Math.Clamp(_configuration.GetValue("UpdateProcessing:WorkerCount", 4), 1, 16);
        var queueCapacity = Math.Clamp(_configuration.GetValue("UpdateProcessing:QueueCapacity", 100), workerCount, 1000);
        var updateChannel = Channel.CreateBounded<Update>(new BoundedChannelOptions(queueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = workerCount == 1,
            SingleWriter = true
        });
        var workers = Enumerable.Range(0, workerCount)
            .Select(_ => ProcessQueuedUpdatesAsync(updateChannel.Reader, _cts.Token))
            .ToArray();

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
                (botClient, update, token) => HandleUpdateAsync(botClient, update, token, updateChannel.Writer),
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
        finally
        {
            updateChannel.Writer.TryComplete();
            try
            {
                await Task.WhenAll(workers);
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
            }

            _cts.Dispose();
            _cts = null;
        }
    }

    public async Task StopPollingAsync()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            _logger.LogInformation("Long Polling stopped");
        }

        await Task.CompletedTask;
    }

    private async Task HandleUpdateAsync(
        ITelegramBotClient botClient,
        Update update,
        CancellationToken cancellationToken,
        ChannelWriter<Update> updateWriter)
    {
        if (IsCommand(update))
        {
            await ProcessUpdateInScopeAsync(update, cancellationToken);
            return;
        }

        if (!IsLongRunningUpdate(update))
        {
            await ProcessUpdateInScopeAsync(update, cancellationToken);
            return;
        }

        if (updateWriter.TryWrite(update))
            return;

        if (update.Message is { } message)
        {
            await botClient.SendMessage(
                message.Chat.Id,
                "⚠️ Processing queue is full. Please try again later.",
                cancellationToken: cancellationToken);
        }
    }

    private async Task ProcessQueuedUpdatesAsync(ChannelReader<Update> reader, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var update in reader.ReadAllAsync(cancellationToken))
                await ProcessUpdateInScopeAsync(update, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task ProcessUpdateInScopeAsync(Update update, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var updateProcessor = scope.ServiceProvider.GetRequiredService<IUpdateProcessor>();
        await updateProcessor.ProcessUpdateAsync(update, cancellationToken);
    }

    private static bool IsCommand(Update update) =>
        update.Message?.Text?.TrimStart().StartsWith('/') == true;

    private static bool IsLongRunningUpdate(Update update)
    {
        if (update.Message is not { } message)
            return false;

        return message.Video is not null ||
               ContainsUrl(message.Text) ||
               ContainsUrl(message.Caption);
    }

    private static bool ContainsUrl(string? text) =>
        text?.Contains("http://", StringComparison.OrdinalIgnoreCase) == true ||
        text?.Contains("https://", StringComparison.OrdinalIgnoreCase) == true;

    private async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Telegram polling error");
        await Task.CompletedTask;
    }
}



