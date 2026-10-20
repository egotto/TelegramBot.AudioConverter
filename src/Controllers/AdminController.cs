using Microsoft.AspNetCore.Mvc;
using TelegramAudioBot.Services;

namespace TelegramAudioBot.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AdminController(
    IBotCommandService _commandService, 
    ILogger<AdminController> _logger) : ControllerBase
{
    /// <summary>
    /// Setup webhook for the bot
    /// </summary>
    /// <remarks>
    /// Example: POST /api/admin/webhook/setup?url=https://yourdomain.com/api/webhook/update
    /// </remarks>
    [HttpPost("webhook/setup")]
    public async Task<IActionResult> SetupWebhook([FromQuery] string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return BadRequest(new { error = "URL is required" });

        _logger.LogInformation("Setting up webhook with URL: {Url}", url);

        var success = await _commandService.SetWebhookAsync(url);
        if (success)
        {
            return Ok(new {
                success = true,
                message = "Webhook set successfully",
                url = url
            });
        }

        return BadRequest(new { error = "Failed to set webhook" });
    }

    /// <summary>
    /// Remove webhook (switch to polling)
    /// </summary>
    [HttpPost("webhook/remove")]
    public async Task<IActionResult> RemoveWebhook()
    {
        _logger.LogInformation("Removing webhook");

        var success = await _commandService.RemoveWebhookAsync();
        if (success)
        {
            return Ok(new {
                success = true,
                message = "Webhook removed successfully. Bot will use polling."
            });
        }

        return BadRequest(new { error = "Failed to remove webhook" });
    }

    /// <summary>
    /// Get webhook information
    /// </summary>
    [HttpGet("webhook/info")]
    public async Task<IActionResult> GetWebhookInfo()
    {
        var info = await _commandService.GetWebhookInfoAsync();
        return Ok(new {
            webhookInfo = info
        });
    }

    /// <summary>
    /// Setup bot commands
    /// </summary>
    [HttpPost("commands/setup")]
    public async Task<IActionResult> SetupCommands()
    {
        _logger.LogInformation("Setting up bot commands");

        try
        {
            await _commandService.SetupCommandsAsync();
            return Ok(new {
                success = true,
                message = "Bot commands set successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting up commands");
            return BadRequest(new { error = ex.Message });
        }
    }
}
