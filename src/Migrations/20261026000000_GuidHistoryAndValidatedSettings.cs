using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TelegramAudioBot.Services;

namespace TelegramAudioBot.Migrations;

[DbContext(typeof(TelegramAudioBotDbContext))]
[Migration("20261026000000_GuidHistoryAndValidatedSettings")]
public class GuidHistoryAndValidatedSettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $migration$
            DECLARE constraint_name text;
            id_type text;
            BEGIN
                IF to_regclass('"UserSettings"') IS NULL THEN
                    CREATE TABLE "UserSettings" (
                        "Id" uuid NOT NULL,
                        "UserId" bigint NOT NULL,
                        "AudioCodec" text NOT NULL,
                        "AudioBitrateKbps" integer NOT NULL,
                        "AudioSampleRateHz" integer NOT NULL,
                        "UpdatedAt" timestamp with time zone NOT NULL,
                        CONSTRAINT "PK_UserSettings" PRIMARY KEY ("Id"),
                        CONSTRAINT "CK_UserSettings_AudioOptions" CHECK (
                            ("AudioCodec" = 'mp3' AND "AudioBitrateKbps" BETWEEN 32 AND 320) OR
                            ("AudioCodec" = 'aac' AND "AudioBitrateKbps" BETWEEN 16 AND 512)),
                        CONSTRAINT "CK_UserSettings_AudioSampleRateHz" CHECK (
                            "AudioSampleRateHz" IN (22050, 32000, 44100, 48000))
                    );
                ELSE
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = current_schema()
                          AND table_name = 'UserSettings'
                          AND column_name = 'Id') THEN
                        ALTER TABLE "UserSettings" ADD COLUMN "Id" uuid;
                        UPDATE "UserSettings" SET "Id" = gen_random_uuid() WHERE "Id" IS NULL;
                        ALTER TABLE "UserSettings" ALTER COLUMN "Id" SET NOT NULL;
                    END IF;

                    FOR constraint_name IN
                        SELECT conname FROM pg_constraint
                        WHERE conrelid = '"UserSettings"'::regclass AND contype = 'p'
                    LOOP
                        EXECUTE format('ALTER TABLE "UserSettings" DROP CONSTRAINT %I', constraint_name);
                    END LOOP;

                    ALTER TABLE "UserSettings" ADD CONSTRAINT "PK_UserSettings" PRIMARY KEY ("Id");
                    ALTER TABLE "UserSettings" ADD CONSTRAINT "CK_UserSettings_AudioOptions" CHECK (
                        ("AudioCodec" = 'mp3' AND "AudioBitrateKbps" BETWEEN 32 AND 320) OR
                        ("AudioCodec" = 'aac' AND "AudioBitrateKbps" BETWEEN 16 AND 512));
                    ALTER TABLE "UserSettings" ADD CONSTRAINT "CK_UserSettings_AudioSampleRateHz" CHECK (
                        "AudioSampleRateHz" IN (22050, 32000, 44100, 48000));
                END IF;

                CREATE UNIQUE INDEX IF NOT EXISTS "IX_UserSettings_UserId"
                    ON "UserSettings" ("UserId");

                IF to_regclass('"HistoryEntries"') IS NULL THEN
                    CREATE TABLE "HistoryEntries" (
                        "Id" uuid NOT NULL,
                        "OccurredAt" timestamp with time zone NOT NULL,
                        "UserId" bigint NOT NULL,
                        "Type" text NOT NULL,
                        "Content" text,
                        "RelativeFilePath" text,
                        CONSTRAINT "PK_HistoryEntries" PRIMARY KEY ("Id")
                    );
                ELSE
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = current_schema()
                          AND table_name = 'HistoryEntries'
                          AND column_name = 'Content') THEN
                        ALTER TABLE "HistoryEntries" ADD COLUMN "Content" text;
                    END IF;

                    SELECT data_type INTO id_type
                    FROM information_schema.columns
                    WHERE table_schema = current_schema()
                      AND table_name = 'HistoryEntries'
                      AND column_name = 'Id';

                    IF id_type <> 'uuid' THEN
                        FOR constraint_name IN
                            SELECT conname FROM pg_constraint
                            WHERE conrelid = '"HistoryEntries"'::regclass AND contype = 'p'
                        LOOP
                            EXECUTE format('ALTER TABLE "HistoryEntries" DROP CONSTRAINT %I', constraint_name);
                        END LOOP;

                        ALTER TABLE "HistoryEntries" ALTER COLUMN "Id" DROP IDENTITY IF EXISTS;
                        ALTER TABLE "HistoryEntries" ALTER COLUMN "Id" TYPE uuid USING gen_random_uuid();
                        ALTER TABLE "HistoryEntries" ADD CONSTRAINT "PK_HistoryEntries" PRIMARY KEY ("Id");
                    END IF;
                END IF;

                CREATE INDEX IF NOT EXISTS "IX_HistoryEntries_OccurredAt"
                    ON "HistoryEntries" ("OccurredAt");
                CREATE INDEX IF NOT EXISTS "IX_HistoryEntries_UserId"
                    ON "HistoryEntries" ("UserId");
            END
            $migration$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("The GUID history schema migration is irreversible.");
    }
}