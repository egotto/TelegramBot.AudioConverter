using FluentAssertions;
using TelegramAudioBot.Models;
using Xunit;

namespace TelegramAudioBot.Tests.Models;

public sealed class AudioSettingsValidatorTests
{
    [Theory]
    [InlineData("mp3", "libmp3lame", 320)]
    [InlineData("m4a", "aac", 512)]
    public void Validate_ShouldAcceptSupportedFormatCodecBitrate(string format, string codec, int bitrate)
    {
        var act = () => AudioSettingsValidator.Validate(format, codec, bitrate, 44100);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("mp3", "aac", 128)]
    [InlineData("mp3", "libmp3lame", 300)]
    [InlineData("m4a", "aac", 48)]
    [InlineData("wav", "pcm_s16le", 128)]
    public void Validate_ShouldRejectUnsupportedFormatCodecOrBitrate(string format, string codec, int bitrate)
    {
        var act = () => AudioSettingsValidator.Validate(format, codec, bitrate, 44100);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("mp3", ".mp3")]
    [InlineData("m4a", ".m4a")]
    public void FileExtension_ShouldMatchSelectedFormat(string format, string extension)
    {
        var settings = new UserSettings { AudioFormat = format };

        settings.FileExtension.Should().Be(extension);
    }
}