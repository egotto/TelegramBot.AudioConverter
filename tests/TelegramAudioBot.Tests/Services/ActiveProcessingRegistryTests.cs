using FluentAssertions;
using TelegramAudioBot.Services;
using Xunit;

namespace TelegramAudioBot.Tests.Services;

public sealed class ActiveProcessingRegistryTests
{
    private readonly ActiveProcessingRegistry _registry = new();

    [Fact]
    public void TryStart_ShouldRejectDuplicateTaskForSameChatAndUser()
    {
        using var hostCancellation = new CancellationTokenSource();

        _registry.TryStart(10, 20, hostCancellation.Token, out var first).Should().BeTrue();
        _registry.TryStart(10, 20, hostCancellation.Token, out _).Should().BeFalse();

        _registry.Complete(10, 20, first);
    }

    [Fact]
    public void Cancel_ShouldCancelTokenAndKeepSlotUntilTaskCompletes()
    {
        using var hostCancellation = new CancellationTokenSource();
        _registry.TryStart(10, 20, hostCancellation.Token, out var activeTask).Should().BeTrue();

        _registry.Cancel(10, 20).Should().BeTrue();
        activeTask.IsCancellationRequested.Should().BeTrue();
        _registry.TryStart(10, 20, hostCancellation.Token, out _).Should().BeFalse();

        _registry.Complete(10, 20, activeTask);
        _registry.TryStart(10, 20, hostCancellation.Token, out var nextTask).Should().BeTrue();
        _registry.Complete(10, 20, nextTask);
    }

    [Fact]
    public void Cancel_ShouldReturnFalseWhenTaskDoesNotExist()
    {
        _registry.Cancel(10, 20).Should().BeFalse();
    }

    [Fact]
    public void Complete_ShouldNotRemoveDifferentTaskRegisteredForSameKey()
    {
        using var hostCancellation = new CancellationTokenSource();
        _registry.TryStart(10, 20, hostCancellation.Token, out var firstTask).Should().BeTrue();
        _registry.Complete(10, 20, firstTask);
        _registry.TryStart(10, 20, hostCancellation.Token, out var secondTask).Should().BeTrue();

        _registry.Complete(10, 20, firstTask);

        _registry.TryStart(10, 20, hostCancellation.Token, out _).Should().BeFalse();
        _registry.Complete(10, 20, secondTask);
    }

    [Fact]
    public async Task TryStart_ShouldAllowOnlyOneConcurrentTaskForSameKey()
    {
        using var hostCancellation = new CancellationTokenSource();
        var attempts = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => _registry.TryStart(10, 20, hostCancellation.Token, out var source)
                ? source
                : null));

        var results = await Task.WhenAll(attempts);
        var successfulTasks = results.Where(source => source is not null).ToArray();

        successfulTasks.Should().ContainSingle();
        _registry.Complete(10, 20, successfulTasks[0]!);
    }
}