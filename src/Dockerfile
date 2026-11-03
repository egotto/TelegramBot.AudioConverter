# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

# Copy project file
COPY ["TelegramAudioBot.csproj", "./"]

# Restore dependencies
RUN dotnet restore "TelegramAudioBot.csproj"

# Copy source code
COPY . .

# Build the application
RUN dotnet build "TelegramAudioBot.csproj" -c Release -o /app/build

# Publish the application
RUN dotnet publish "TelegramAudioBot.csproj" -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble

# Install FFmpeg and create necessary groups/users
RUN apt-get update && apt-get install -y --no-install-recommends \
    ffmpeg \
    python3 \
    python3-venv \
    curl \
    ca-certificates \
    unzip \
    && rm -rf /var/lib/apt/lists/*

RUN python3 -m venv /opt/yt-dlp && \
    /opt/yt-dlp/bin/pip install --no-cache-dir "yt-dlp[default]" && \
    curl -fsSL https://deno.land/install.sh | DENO_INSTALL=/opt/deno sh

ENV PATH="/opt/yt-dlp/bin:/opt/deno/bin:${PATH}"

# Create a shared group for file access between services
# Both telegram-api and telegram-audio-bot will use this group
 RUN groupadd --non-unique --gid 1000 tg-shared

# Create application user with shared group membership
RUN useradd -m -u 1001 -g tg-shared -s /bin/bash appuser

WORKDIR /app

# Copy published application from build stage
COPY --from=build /app/publish .

# Create necessary directories with correct permissions
RUN mkdir -p /app/temp /shared && \
    chown -R appuser:tg-shared /app && \
    chmod 770 /app/temp && \
    chmod 770 /shared

RUN yt-dlp --version && deno --version && ffmpeg -version

# Set umask so new files have proper permissions (rw-rw----)
# This ensures files created by this app are readable/writable by group
RUN echo "umask 0007" >> /home/appuser/.bashrc

# Expose ports
EXPOSE 8080

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/api/health || exit 1

# Run as non-root user with proper group access
USER appuser:tg-shared

# Run the application
ENTRYPOINT ["dotnet", "TelegramAudioBot.dll"]
