namespace TelegramAudioBot.Models;

public static class AudioSettingsValidator
{
    private static readonly int[] Mp3BitratesKbps = [64, 96, 128, 160, 192, 224, 256, 320];
    private static readonly int[] M4aBitratesKbps = [64, 96, 128, 160, 192, 224, 256, 320, 384, 512];
    private static readonly HashSet<int> SupportedSampleRates = [22050, 32000, 44100, 48000];

    public static IReadOnlyList<int> GetSupportedBitrates(string format)
    {
        ArgumentNullException.ThrowIfNull(format);
        return format.Trim().ToLowerInvariant() switch
        {
            "mp3" => Mp3BitratesKbps,
            "m4a" => M4aBitratesKbps,
            _ => []
        };
    }

    public static string GetCodec(string format)
    {
        ArgumentNullException.ThrowIfNull(format);
        return format.Trim().ToLowerInvariant() switch
        {
            "mp3" => "libmp3lame",
            "m4a" => "aac",
            _ => throw new ArgumentException("Supported formats are mp3 and m4a.", nameof(format))
        };
    }

    public static void Validate(string format, string codec, int bitrateKbps, int sampleRateHz)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(codec);
        var normalizedFormat = format.Trim().ToLowerInvariant();
        var normalizedCodec = codec.Trim().ToLowerInvariant();
        var validCodec = normalizedFormat switch
        {
            "mp3" => normalizedCodec == "libmp3lame",
            "m4a" => normalizedCodec == "aac",
            _ => false
        };

        if (!validCodec)
            throw new ArgumentException("Supported codec/format combinations are MP3/libmp3lame and M4A/AAC.", nameof(codec));

        if (!GetSupportedBitrates(normalizedFormat).Contains(bitrateKbps))
            throw new ArgumentOutOfRangeException(nameof(bitrateKbps), $"Unsupported bitrate for {normalizedFormat}.");

        if (!SupportedSampleRates.Contains(sampleRateHz))
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz), "Sample rate must be 22050, 32000, 44100, or 48000 Hz.");
    }
}