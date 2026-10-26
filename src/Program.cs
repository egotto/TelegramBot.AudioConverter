using Telegram.Bot;
using Microsoft.EntityFrameworkCore;
using TelegramAudioBot.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Register Telegram Bot Client
var botToken = builder.Configuration["TELEGRAM_BOT_TOKEN"];
if (string.IsNullOrEmpty(botToken))
{
    throw new InvalidOperationException("TELEGRAM_BOT_TOKEN environment variable is not set.");
}

var telegramApiBaseUrl = builder.Configuration["TELEGRAM_API_BASE_URL"];
if (string.IsNullOrEmpty(telegramApiBaseUrl))
{
    throw new InvalidOperationException("TELEGRAM_API_BASE_URL environment variable is not set.");
}

// ITelegramBotClient is stateless, so Singleton is safe
builder.Services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(
    options: new TelegramBotClientOptions(
        token: botToken,
        baseUrl: telegramApiBaseUrl
    )));

// Register application services
builder.Services.AddScoped<IUpdateProcessor, UpdateProcessor>();
builder.Services.AddScoped<IAudioExtractionService, AudioExtractionService>();
builder.Services.AddScoped<ITelegramBotService, TelegramBotService>();
builder.Services.AddScoped<IBotCommandService, BotCommandService>();
var databaseConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(databaseConnectionString))
{
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");
}

builder.Services.AddDbContext<TelegramAudioBotDbContext>(options =>
    options.UseNpgsql(databaseConnectionString));
builder.Services.AddScoped<IUserDataService, UserDataService>();
builder.Services.AddHostedService<HistoryCleanupHostedService>();

// Polling service is Singleton, but it uses IServiceProvider to create scopes for Scoped services
builder.Services.AddSingleton<IBotPollingService, BotPollingService>();

// Add logging
builder.Services.AddLogging(config =>
{
    config.AddConsole();
    config.AddDebug();
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<TelegramAudioBotDbContext>();
    await dbContext.Database.MigrateAsync();
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseRouting();
app.MapControllers();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Telegram Audio Bot starting...");
logger.LogInformation("Environment: {Environment}", app.Environment.EnvironmentName);

// Setup bot commands on startup
using (var scope = app.Services.CreateScope())
{
    var commandService = scope.ServiceProvider.GetRequiredService<IBotCommandService>();
    await commandService.SetupCommandsAsync();
}

// Start Long Polling in the background
var pollingService = app.Services.GetRequiredService<IBotPollingService>();
_ = pollingService.StartPollingAsync(app.Lifetime.ApplicationStopping);

logger.LogInformation("Bot ready. Using Long Polling mode.");
logger.LogInformation("Health check: GET /api/health");

app.Run();

