namespace TelegramAudioBot.Models;

public static class AudioSettingsValidator
{
    private static readonly HashSet<int> SupportedSampleRates = [22050, 32000, 44100, 48000];

    public static void Validate(string codec, int bitrateKbps, int sampleRateHz)
    {
        ArgumentNullException.ThrowIfNull(codec);
        var normalizedCodec = codec.Trim().ToLowerInvariant();
        var bitrateIsValid = normalizedCodec switch
        {
            "mp3" => bitrateKbps is >= 32 and <= 320,
            "aac" => bitrateKbps is >= 16 and <= 512,
            _ => false
        };

        if (!bitrateIsValid)
            throw new ArgumentException("Supported codecs are mp3 (32-320 kbps) and aac (16-512 kbps).", nameof(codec));

        if (!SupportedSampleRates.Contains(sampleRateHz))
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz), "Sample rate must be 22050, 32000, 44100, or 48000 Hz.");
    }
}