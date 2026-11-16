using FFMpegCore;

namespace TelegramAudioBot.Services;

public class AudioExtractionService(ILogger<AudioExtractionService> _logger) : IAudioExtractionService
{
    public async Task<bool> ExtractAudioAsync(string inputPath, string outputPath, TelegramAudioBot.Models.UserSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            TelegramAudioBot.Models.AudioSettingsValidator.Validate(settings.AudioFormat, settings.AudioCodec, settings.AudioBitrateKbps, settings.AudioSampleRateHz);

            if (!File.Exists(inputPath))
            {
                _logger.LogError("Input file not found: {FilePath}", inputPath);
                return false;
            }

            _logger.LogInformation("Starting audio extraction from: {InputPath} to: {OutputPath}", inputPath, outputPath);

            // Remove output file if it exists
            if (File.Exists(outputPath))
                File.Delete(outputPath);

            using (var process = new System.Diagnostics.Process())
            {
                process.StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                foreach (var argument in new[]
                {
                    "-i", inputPath,
                    "-vn",
                    "-c:a", settings.AudioCodec,
                    "-b:a", $"{settings.AudioBitrateKbps}k",
                    "-ar", settings.AudioSampleRateHz.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    outputPath
                })
                    process.StartInfo.ArgumentList.Add(argument);

                process.Start();

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                try
                {
                    await process.WaitForExitAsync(cancellationToken);
                    await Task.WhenAll(outputTask, errorTask);
                }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                    await Task.WhenAll(outputTask, errorTask);
                    throw;
                }

                if (process.ExitCode != 0)
                {
                    _logger.LogError("FFMpeg failed with exit code {ExitCode}: {Error}", process.ExitCode, errorTask.Result);
                    return false;
                }
            }

            _logger.LogInformation("Audio extraction completed successfully");
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
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
