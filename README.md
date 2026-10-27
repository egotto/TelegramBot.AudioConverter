[![Validate and build](https://github.com/egotto/TelegramBot.AudioConverter/actions/workflows/validate.yml/badge.svg)](https://github.com/egotto/TelegramBot.AudioConverter/actions/workflows/validate.yml)

# Telegram Audio Bot

A bot for extracting audio from video files sent via Telegram.

## Features

- ✅ Extract audio from video files using FFmpeg
- ✅ Send processed audio back to the user
- ✅ **Long Polling by default** (ideal for personal use)
- ✅ Optional Webhook support for production
- ✅ Built-in bot commands
- 📋 Planned: Support for downloading videos from links (YouTube)

## Bot Commands

- `/start` — Start working (greeting and description)
- `/help` — Show available commands
- `/settings` — Audio processing parameters
- `/cancel` — Cancel current processing

Simply send a video file to the bot, and it will automatically convert it to audio.

## Long Polling vs Webhook

### Long Polling (default) ✅
- **Simplicity:** No need for public domain and SSL
- **Ideal for:** Personal use, home servers, development
- **How it works:** Bot regularly requests new messages from Telegram
- **Downside:** Small delay (a few seconds between requests)

### Webhook (optional)
- **Performance:** Telegram sends updates immediately
- **Requirements:** Public domain with SSL certificate
- **Ideal for:** High-load production systems
- **Complexity:** Requires infrastructure (Nginx, SSL, etc.)

## Requirements

- .NET 10.0+
- FFmpeg
- Telegram Bot Token (from @BotFather)
- **Local Telegram API** (for files > 20 MB) - API ID and Hash from [my.telegram.org](https://my.telegram.org)
- Docker & Docker Compose (for containerized setup)

## ⚠️ Limitations

| Parameter | Limit | Note |
|----------|-------|------|
| Maximum video size | **2 GB** | Local Telegram API |
| Video formats | MP4, WebM, MOV, AVI, FLV, MKV | Depends on FFmpeg |
| Output audio format | MP3 (128 kbps, 44.1 kHz) | Optimal for quality and size |

💡 **Using local Telegram Server** to support large files

## Local Telegram API Architecture

The bot uses a **local Telegram API server** to handle large files (>20 MB) that exceed the official Telegram Bot API's 20 MB limit.

### Why Local Telegram API?

| Feature | Official API | Local API |
|---------|--------------|-----------|
| File size limit | 20 MB | Up to 2 GB |
| Download method | Download files | Direct filesystem access |
| Setup complexity | Simple | Requires configuration |
| Performance | Slower (network bound) | Faster (local access) |

### Architecture

```
┌─────────────────────────────────────┐
│     Telegram Client (User)          │
└─────────────┬───────────────────────┘
              │ (Messages & Video Files)
              ↓
┌─────────────────────────────────────┐
│   Local Telegram API Server         │
│   (Port 8081)                       │
│   - Receives uploaded files         │
│   - Stores files in API data folder │
└─────────────┬───────────────────────┘
              │ (File path references)
              ↓
┌─────────────────────────────────────┐
│   Telegram Audio Bot                │
│   (Port 8080)                       │
│   - Reads video from API data folder│
│   - Extracts audio via FFmpeg       │
│   - Saves temporary MP3 in /app/temp│
└─────────────┬───────────────────────┘
              │ (Audio file)
              ↓
┌─────────────────────────────────────┐
│   Local Telegram API Server         │
│   (Sends back to user)              │
└─────────────────────────────────────┘
```

The bot reads uploaded videos from the Telegram API data directory through a read-only mount. Temporary MP3 files are stored in the bot's `/app/temp` volume.

## Quick Start (3 minutes)

### Step 1: Get bot token
1. Open Telegram and find **@BotFather**
2. Write `/newbot`
3. Follow the instructions and get the token

### Step 2: Run the bot

```bash
# Clone the project
git clone <repo-url>
cd TelegramAudioBot

# Install FFmpeg
sudo apt-get install ffmpeg  # Ubuntu/Debian
# or
brew install ffmpeg          # macOS

# Create .env file
cp .env.example .env

# Edit .env and add token
nano .env
# TELEGRAM_BOT_TOKEN=123456789:ABCDefGhIjKlMnOpQrStUvWxYz1234567890

# Export environment variables
export $(cat .env | xargs)

# Run the application (from src/ folder)
cd src
dotnet run
```

✅ **Done!** The bot is now running on Long Polling and ready to process messages.

### Step 3: Test the bot
1. Open Telegram
2. Find your bot (by username)
3. Send `/start`
4. Send a video file

## Docker Installation

For the simplest deployment, copy `docker-compose.yml` and `.env.example` from
the repository into the same directory on your server. You do not need to clone
the source code or install .NET; Compose pulls the published bot image.

```bash
# Run this from the directory containing docker-compose.yml and .env.example
cp .env.example .env
nano .env

# Set TELEGRAM_API_ID, TELEGRAM_API_HASH, and TELEGRAM_BOT_TOKEN in .env

docker compose up -d

# Check status and logs
docker compose ps
docker compose logs -f telegram-audio-bot
```

Keep `.env` beside `docker-compose.yml`. Compose creates `telegram_api_data/`
beside them for API-owned files; the bot mounts that directory read-only and
uses supplementary group `101` to read uploaded videos. The bot stores and
deletes only temporary MP3 files in its own volume.

Docker Compose automatically reads `.env` from that directory for variable
substitution. The Compose file passes only API credentials to `telegram-api`
and the bot token and application settings to `telegram-audio-bot`.

## CI/CD and published Docker image

GitHub Actions validates pull requests by building the .NET solution and Docker
image. Pushes to `main` and tags matching `v*` publish the image to GHCR. The
`latest` tag tracks `main`; version tags (for example, `v1.2.3`) are published
as-is. No registry secrets are required because the workflow uses
`GITHUB_TOKEN`.

After the first successful publish, make the package public in the repository
owner's GitHub Packages settings if it is not already public. Then pull the
image with:

```bash
docker pull ghcr.io/egotto/telegrambot.audioconverter:latest
```

The root `docker-compose.yml` already uses this published image.

To build from source, use the Dockerfile in `src/` and update the bot service in
the root Compose file.

## API Endpoints

### Health Check
```
GET /api/health
```
Checks the application status and bot information.

**Example:**
```bash
curl http://localhost:8080/api/health
```

**Response:**
```json
{
  "status": "Healthy",
  "timestamp": "2026-04-15T12:00:00Z",
  "botUsername": "@your_bot_username",
  "service": "Telegram Audio Bot"
}
```

### Webhook Endpoint (optional)
```
POST /api/webhook/update
```
Used only if you configure webhook instead of Long Polling.

## Admin API (optional for Webhook)

If you want to switch to Webhook in the future:

**Setup webhook:**
```bash
curl -X POST "http://localhost:8080/api/admin/webhook/setup?url=https://yourdomain.com/api/webhook/update"
```

**Remove webhook (return to Long Polling):**
```bash
curl -X POST "http://localhost:8080/api/admin/webhook/remove"
```

**Get webhook information:**
```bash
curl http://localhost:8080/api/admin/webhook/info
```

**Setup bot commands:**
```bash
curl -X POST "http://localhost:8080/api/admin/commands/setup"
```

## Configuration

Environment variables (in `.env`):

- `TELEGRAM_BOT_TOKEN` - Telegram bot token from @BotFather (required)
- `TELEGRAM_API_ID` - API ID from my.telegram.org (required for local API)
- `TELEGRAM_API_HASH` - API Hash from my.telegram.org (required for local API)
- `TELEGRAM_API_BASE_URL` - Local Telegram API URL, default http://telegram-api:8081
- `ASPNETCORE_ENVIRONMENT` - Environment (Development/Production), default Production
- `ASPNETCORE_URLS` - Application URL, default http://+:8080
- `SHARED_DIRECTORY` - Base directory used for relative API file paths, default `/var/lib/telegram-bot-api`
- `TEMP_FILES_PATH` - Path for temporary audio files, default `/tmp/audio_processing` (Docker sets `/app/temp`)
- `MAX_FILE_SIZE_MB` - Maximum video size in MB, default 2048
- `TELEGRAM_API_PORT` - Local Telegram API port, default 8081

## Project Structure

```
TelegramAudioBot/
├── src/                  # Main source code
│   ├── Controllers/           # API controllers
│   │   ├── HealthController.cs       # Application health check
│   │   ├── WebhookController.cs      # Processing updates from Telegram
│   │   └── AdminController.cs        # Webhook and command management
│   ├── Services/             # Business logic
│   │   ├── IAudioExtractionService.cs
│   │   ├── AudioExtractionService.cs    # Extract audio via FFmpeg
│   │   ├── ITelegramBotService.cs
│   │   ├── TelegramBotService.cs        # Telegram API interaction
│   │   ├── IBotCommandService.cs
│   │   ├── BotCommandService.cs         # Bot command management
│   │   ├── IBotPollingService.cs
│   │   └── IUpdateProcessor.cs
│   ├── Models/               # Data models
│   │   └── CommandConstants.cs
│   ├── Program.cs            # Entry point and DI configuration
│   ├── appsettings.json      # Application configuration
│   ├── appsettings.Development.json  # Dev configuration
│   ├── TelegramAudioBot.csproj        # C# project
│   ├── TelegramAudioBot.sln           # Visual Studio solution
│   ├── Dockerfile            # Multi-stage Docker image
│   ├── Properties/           # Project properties
│   └── TelegramAudioBot.http # HTTP tests
├── README.md             # Main documentation
├── docker-compose.yml    # Root Docker Compose configuration
├── SETUP.md             # Installation guide
├── PERMISSIONS.md       # Security & permissions details
├── init-docker.sh       # Docker initialization script
├── .env.example         # Environment variables template
└── Claude.md            # Claude AI integration
```

## Security & Permissions

The bot uses supplementary group `101` to read Telegram API files owned by UID/GID `101:101`. The API data is mounted read-write in the API container and read-only in the bot. Converted audio is written to the bot's temporary volume.

### User and Group Setup

```
Service                Access
────────────────────────────────────────────────────────────
telegram-api          Owns and writes /var/lib/telegram-bot-api
telegram-audio-bot    UID from image + supplementary GID 101
```

### File Permissions Model

```
API token directories: typically 750, owned by 101:101
Uploaded files:       typically 640, owned by 101:101
Bot API mount:        read-only, with supplementary group 101
Bot temporary files:  /app/temp, removed after processing
```

### Benefits

✅ **Security**: Non-root bot process limits damage if compromised
✅ **Access Control**: The bot can read API files but cannot alter them
✅ **No Data Leaks**: Files not readable by other system processes
✅ **Restricted Permissions**: No world-writable directories

For detailed information, see [PERMISSIONS.md](PERMISSIONS.md)

### Run locally (Long Polling)
```bash
# Copy .env.example
cp .env.example .env

# Edit .env and set token
# TELEGRAM_BOT_TOKEN=your_token

# Export variables
export $(cat .env | xargs)

# Change to src folder
cd src

# Run the application
dotnet run

# In a separate terminal, disable webhook
curl -X POST "http://localhost:8080/api/admin/webhook/remove"
```

### Run in Docker with Webhook
```bash
# Create and configure the environment file
cp .env.example .env
nano .env

# Start both services from the project root
docker compose up -d

# Setup webhook
curl -X POST "https://yourdomain.com:8443/api/admin/webhook/setup?url=https://yourdomain.com:8443/api/webhook/update"

# Check status
curl "https://yourdomain.com:8443/api/admin/webhook/info"
```

## Logging

By default logging is enabled with Information level.

Log examples:
- Receiving message from user
- Video upload
- Audio extraction
- Processing errors

## Development

### Adding new dependencies
```bash
dotnet add package <PackageName>
```

### Building the project
```bash
dotnet build
```

### Running tests (when added)
```bash
dotnet test
```

## Security

- Bot token is stored in environment variables (not in code)
- Temporary files are deleted after processing
- Validation of incoming updates from Telegram
- **Read-only group-based access** to Telegram API files (see [PERMISSIONS.md](PERMISSIONS.md))
- Bot process runs as non-root user (appuser) with restricted privileges
- Group-based access control for inter-service file exchange

## License

MIT

## Roadmap

- [ ] Support for YouTube links
- [ ] Configurable audio parameters (bitrate, codec)
- [ ] Batch processing of multiple files
- [ ] Caching for repeated requests
- [ ] Web UI for management
- [ ] Unit tests
- [ ] Database integration for statistics
