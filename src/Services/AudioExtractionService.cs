using FFMpegCore;

namespace TelegramAudioBot.Services;

public class AudioExtractionService(ILogger<AudioExtractionService> _logger) : IAudioExtractionService
{
    public async Task<bool> ExtractAudioAsync(string inputPath, string outputPath, TelegramAudioBot.Models.UserSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            TelegramAudioBot.Models.AudioSettingsValidator.Validate(settings.AudioCodec, settings.AudioBitrateKbps, settings.AudioSampleRateHz);

            if (!File.Exists(inputPath))
            {
                _logger.LogError("Input file not found: {FilePath}", inputPath);
                return false;
            }

            _logger.LogInformation("Starting audio extraction from: {InputPath} to: {OutputPath}", inputPath, outputPath);

            // Remove output file if it exists
            if (File.Exists(outputPath))
                File.Delete(outputPath);

            // Extract audio using FFMpeg command line
            // ffmpeg -i input.mp4 -q:a 0 -map a output.mp3
            var arguments = $"-i \"{inputPath}\" -vn -acodec {settings.AudioCodec} -ab {settings.AudioBitrateKbps}k -ar {settings.AudioSampleRateHz} \"{outputPath}\"";

            using (var process = new System.Diagnostics.Process())
            {
                process.StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                process.Start();

                var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

                await Task.WhenAll(outputTask, errorTask);

                await process.WaitForExitAsync(cancellationToken);

                if (process.ExitCode != 0)
                {
                    _logger.LogError("FFMpeg failed with exit code {ExitCode}: {Error}", process.ExitCode, errorTask.Result);
                    return false;
                }
            }

            _logger.LogInformation("Audio extraction completed successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting audio from: {InputPath}", inputPath);
            return false;
        }
    }

    public async Task<MediaInfo?> GetMediaInfoAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("File not found for media info: {FilePath}", filePath);
                return null;
            }

            var mediaInfo = await FFProbe.AnalyseAsync(filePath);

            return new MediaInfo
            {
                Duration = mediaInfo.Duration,
                AudioCodec = mediaInfo.AudioStreams.FirstOrDefault()?.CodecName,
                AudioBitrate = (int?)mediaInfo.AudioStreams.FirstOrDefault()?.BitRate,
                SampleRate = mediaInfo.AudioStreams.FirstOrDefault()?.SampleRateHz
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing media file: {FilePath}", filePath);
            return null;
        }
    }
}
