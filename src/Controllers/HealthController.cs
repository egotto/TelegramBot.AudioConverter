using Microsoft.AspNetCore.Mvc;
using Telegram.Bot;

namespace TelegramAudioBot.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController(
    ILogger<HealthController> _logger, 
    ITelegramBotClient _botClient) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<HealthResponse>> GetHealth()
    {
        try
        {
            var botUser = await _botClient.GetMe(HttpContext.RequestAborted);
            return Ok(new HealthResponse
            {
                Status = "Healthy",
                Timestamp = DateTime.UtcNow,
                BotUsername = botUser.Username,
                Service = "Telegram Audio Bot"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return Ok(new HealthResponse
            {
                Status = "Unhealthy",
                Timestamp = DateTime.UtcNow,
                Error = ex.Message
            });
        }
    }
}

public class HealthResponse
{
    public string Status { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? BotUsername { get; set; }
    public string? Service { get; set; }
    public string? Error { get; set; }
}
