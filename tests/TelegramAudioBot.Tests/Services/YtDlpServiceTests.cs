using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TelegramAudioBot.Services;
using Xunit;

namespace TelegramAudioBot.Tests.Services;

public sealed class YtDlpServiceTests
{
    [Theory]
    [InlineData("file:///tmp/video.mp4")]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com/audio")]
    public async Task DownloadAudioAsync_ShouldRejectNonHttpUrlWithoutStartingDownloader(string url)
    {
        using var service = new YtDlpService(
            Options.Create(new YtDlpOptions { ExecutablePath = "missing-yt-dlp" }),
            NullLogger<YtDlpService>.Instance);

        var result = await service.DownloadAudioAsync(url, Path.GetTempPath(), 100);

        result.Files.Should().BeEmpty();
        result.Error.Should().Be("Only valid HTTP(S) links are supported.");
    }
}