using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TelegramAudioBot.Services;

namespace TelegramAudioBot.Migrations;

[DbContext(typeof(TelegramAudioBotDbContext))]
[Migration("20261116000000_AudioFormats")]
public class AudioFormats : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AudioFormat",
            table: "UserSettings",
            type: "text",
            nullable: false,
            defaultValue: "mp3");

        migrationBuilder.Sql("""
            UPDATE "UserSettings"
            SET "AudioFormat" = CASE WHEN "AudioCodec" = 'aac' THEN 'm4a' ELSE 'mp3' END,
                "AudioCodec" = CASE WHEN "AudioCodec" = 'aac' THEN 'aac' ELSE 'libmp3lame' END,
                "AudioBitrateKbps" = CASE
                    WHEN "AudioBitrateKbps" IN (64, 96, 128, 160, 192, 224, 256, 320) OR
                         ("AudioCodec" = 'aac' AND "AudioBitrateKbps" IN (384, 512)) THEN "AudioBitrateKbps"
                    ELSE 128
                END;

            ALTER TABLE "UserSettings" DROP CONSTRAINT IF EXISTS "CK_UserSettings_AudioOptions";
            ALTER TABLE "UserSettings" ADD CONSTRAINT "CK_UserSettings_AudioOptions" CHECK (
                ("AudioFormat" = 'mp3' AND "AudioCodec" = 'libmp3lame' AND "AudioBitrateKbps" IN (64, 96, 128, 160, 192, 224, 256, 320)) OR
                ("AudioFormat" = 'm4a' AND "AudioCodec" = 'aac' AND "AudioBitrateKbps" IN (64, 96, 128, 160, 192, 224, 256, 320, 384, 512)));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE \"UserSettings\" DROP CONSTRAINT IF EXISTS \"CK_UserSettings_AudioOptions\";");
        migrationBuilder.DropColumn(name: "AudioFormat", table: "UserSettings");
        migrationBuilder.Sql("""
            UPDATE "UserSettings" SET "AudioCodec" = CASE WHEN "AudioCodec" = 'aac' THEN 'aac' ELSE 'mp3' END;
            ALTER TABLE "UserSettings" ADD CONSTRAINT "CK_UserSettings_AudioOptions" CHECK (
                ("AudioCodec" = 'mp3' AND "AudioBitrateKbps" BETWEEN 32 AND 320) OR
                ("AudioCodec" = 'aac' AND "AudioBitrateKbps" BETWEEN 16 AND 512));
            """);
    }
}