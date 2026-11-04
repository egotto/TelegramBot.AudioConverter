using System.Collections.Concurrent;

namespace TelegramAudioBot.Services;

public interface IActiveProcessingRegistry
{
    bool TryStart(long chatId, long userId, CancellationToken hostToken, out CancellationTokenSource cancellationSource);
    bool Cancel(long chatId, long userId);
    void Complete(long chatId, long userId, CancellationTokenSource cancellationSource);
}

public sealed class ActiveProcessingRegistry : IActiveProcessingRegistry
{
    private readonly ConcurrentDictionary<(long ChatId, long UserId), CancellationTokenSource> _activeJobs = new();

    public bool TryStart(long chatId, long userId, CancellationToken hostToken, out CancellationTokenSource cancellationSource)
    {
        var candidate = CancellationTokenSource.CreateLinkedTokenSource(hostToken);
        if (_activeJobs.TryAdd((chatId, userId), candidate))
        {
            cancellationSource = candidate;
            return true;
        }

        candidate.Dispose();
        cancellationSource = null!;
        return false;
    }

    public bool Cancel(long chatId, long userId)
    {
        if (!_activeJobs.TryGetValue((chatId, userId), out var cancellationSource))
            return false;

        try
        {
            cancellationSource.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    public void Complete(long chatId, long userId, CancellationTokenSource cancellationSource)
    {
        if (_activeJobs.TryRemove(new KeyValuePair<(long ChatId, long UserId), CancellationTokenSource>(
                (chatId, userId), cancellationSource)))
            cancellationSource.Dispose();
    }
}