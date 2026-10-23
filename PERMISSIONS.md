# File Permissions

The Telegram Bot API owns uploaded source files. The bot reads those files to extract audio, but it must not modify or delete them. Generated MP3 files live in the bot's temporary volume.

## Storage and Access

| Host/container path | API service | Bot service |
| --- | --- | --- |
| `./telegram_api_data` / `/var/lib/telegram-bot-api` | Read/write bind mount | Read-only bind mount |
| Docker volume `bot_temp` / `/app/temp` | Not mounted | Read/write temporary files |

On the current server, API files are owned by UID/GID `101:101`. Token directories are mode `750`, and video files are mode `640`. The bot's Compose service has supplementary group `101`, which provides directory traversal and file read access without granting ownership or write access. The bot process otherwise runs as the non-root user configured in its image.

The API container's entrypoint may run as root while the API process creates files as UID/GID `101:101`. When checking access, inspect file ownership with `stat`; the default user for `docker exec` does not necessarily identify the API worker's user.

## First Run

From the repository root:

```bash
cp .env.example .env
# Edit .env and set TELEGRAM_API_ID, TELEGRAM_API_HASH, and TELEGRAM_BOT_TOKEN.
bash init-docker.sh
docker compose up -d
```

The initialization script creates `telegram_api_data`. When run as root, it sets owner `101:101` and mode `750`. If run as a regular user, it prints the `sudo chown`/`chmod` command to use if the API cannot write to the directory.

Do not change API file modes to `777` or make the bind mount writable inside the bot. The bot only needs read access to source video files.

## Verify Access

```bash
docker exec telegram-api stat -c '%u:%g %a %n' /var/lib/telegram-bot-api /var/lib/telegram-bot-api/* /var/lib/telegram-bot-api/*/videos/*
docker exec telegram-audio-bot id
docker exec telegram-audio-bot sh -lc 'test -r /var/lib/telegram-bot-api/*/videos/*; echo readable_status=$?'
```

Expected results:

- API files are owned by `101:101`.
- The bot's groups include `101`.
- The read test prints `readable_status=0`.

If the API cannot write to the host data directory, run this from the repository root and recreate the services:

```bash
sudo chown 101:101 telegram_api_data
sudo chmod 750 telegram_api_data
docker compose up -d --force-recreate
```

The bot removes temporary MP3 files from `/app/temp` after processing. Uploaded source files remain under API management and are not deleted by the bot.
