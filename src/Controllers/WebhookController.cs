using Microsoft.AspNetCore.Mvc;
using Telegram.Bot.Types;
using TelegramAudioBot.Services;

namespace TelegramAudioBot.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WebhookController(
    IUpdateProcessor _updateProcessor, 
    ILogger<WebhookController> _logger) : ControllerBase
{
    /// <summary>
    /// Webhook endpoint for receiving updates from Telegram (optional, for production with webhook setup)
    /// By default, the bot uses Long Polling
    /// </summary>
    [HttpPost("update")]
    public async Task<IActionResult> Update([FromBody] Update update, CancellationToken cancellationToken)
    {
        try
        {
            if (update.Message is not { } message)
                return Ok();

            _logger.LogInformation("Received webhook update from {UserId}", message.From?.Id);

            // Process the update
            await _updateProcessor.ProcessUpdateAsync(update, cancellationToken);

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing webhook update");
            return StatusCode(500);
        }
    }
}

