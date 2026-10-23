# Telegram Audio Bot Setup

## Docker Compose

The simplest server deployment uses the root `docker-compose.yml` and `.env.example` from the same directory. No source checkout or .NET installation is needed; Compose pulls the published bot image.

1. Place `docker-compose.yml` and `.env.example` together in a directory on the server.
2. Copy the example file to `.env` and edit it:

   ```bash
   cp .env.example .env
   nano .env
   ```

3. Set `TELEGRAM_API_ID`, `TELEGRAM_API_HASH`, and `TELEGRAM_BOT_TOKEN`. Keep `TELEGRAM_API_BASE_URL=http://telegram-api:8081` for the default Compose network.
4. Start the services from the directory containing both files:

   ```bash
   docker compose up -d
   ```

5. Check status and logs:

   ```bash
   docker compose ps
   docker compose logs -f
   ```

The bot health endpoint is `http://localhost:8080/api/health`. Telegram API status is available at `http://localhost:8081/test`.

Compose stores Telegram API data in the sibling `telegram_api_data/` directory. It mounts this data read-write into `telegram-api` and read-only into the bot. The bot joins supplementary group `101` to read API-owned files (typically owner `101:101`, directories mode `750`, files mode `640`). Temporary MP3 output is kept in the `bot_temp` Docker volume and deleted after processing. Uploaded source videos remain managed by the Telegram API.

If the API cannot write to its data directory, stop the services and correct the directory ownership from the Compose directory:

```bash
sudo chown 101:101 telegram_api_data
sudo chmod 750 telegram_api_data
docker compose up -d
```

Do not make API files world-readable or writable, and do not change the bot's API-data mount to read-write.

## Configuration

`.env.example` documents all supported settings. The Compose file passes API credentials only to `telegram-api` and the bot token and application settings only to `telegram-audio-bot`.

Required values:

- `TELEGRAM_API_ID` and `TELEGRAM_API_HASH` from [my.telegram.org](https://my.telegram.org)
- `TELEGRAM_BOT_TOKEN` from [@BotFather](https://t.me/BotFather)

`MAX_FILE_SIZE_MB` defaults to `2048`. `TELEGRAM_API_PORT` defaults to `8081`; the bot's HTTP health endpoint is published on port `8080`.

## Troubleshooting File Access

Check the owner and mode reported by the API container, then verify the bot has group `101` and can read the file:

```bash
docker exec telegram-api stat -c '%u:%g %a %n' /var/lib/telegram-bot-api /var/lib/telegram-bot-api/* /var/lib/telegram-bot-api/*/videos/*
docker exec telegram-audio-bot id
docker exec telegram-audio-bot sh -lc 'test -r /var/lib/telegram-bot-api/*/videos/*; echo readable_status=$?'
```

The bot's `id` output should include group `101`, and the test should print `readable_status=0`.

## Run from Source

For local development, install .NET 10 and FFmpeg, copy `.env.example` to `.env`, and set the required credentials. Then run:

```bash
cd src
dotnet run
```
