using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailArchiver.Migrations
{
    /// <inheritdoc />
    public partial class MigrateV2606_1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.tables
                        WHERE table_schema = 'mail_archiver'
                        AND table_name = 'EmailAttachmentContents'
                    ) THEN
                        CREATE TABLE mail_archiver.""EmailAttachmentContents"" (
                            ""Id"" serial PRIMARY KEY,
                            ""ContentHash"" varchar(64) NOT NULL,
                            ""Content"" bytea NOT NULL,
                            ""Size"" bigint NOT NULL
                        );
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_indexes
                        WHERE schemaname = 'mail_archiver'
                        AND indexname = 'IX_EmailAttachmentContents_ContentHash'
                    ) THEN
                        CREATE UNIQUE INDEX ""IX_EmailAttachmentContents_ContentHash""
                        ON mail_archiver.""EmailAttachmentContents"" (""ContentHash"");
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'mail_archiver'
                        AND table_name = 'EmailAttachments'
                        AND column_name = 'EmailAttachmentContentId'
                    ) THEN
                        ALTER TABLE mail_archiver.""EmailAttachments""
                        ADD COLUMN ""EmailAttachmentContentId"" integer;
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_extension
                        WHERE extname = 'pgcrypto'
                    ) THEN
                        CREATE EXTENSION IF NOT EXISTS pgcrypto;
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    INSERT INTO mail_archiver.""EmailAttachmentContents"" (""ContentHash"", ""Content"", ""Size"")
                    SELECT hashed.""ContentHash"", hashed.""Content"", hashed.""Size""
                    FROM (
                            SELECT
                                encode(digest(COALESCE(""Content"", '\\x'::bytea), 'sha256'), 'hex') AS ""ContentHash"",
                                COALESCE(""Content"", '\\x'::bytea) AS ""Content"",
                                octet_length(COALESCE(""Content"", '\\x'::bytea)) AS ""Size""
                            FROM mail_archiver.""EmailAttachments""
                            GROUP BY encode(digest(COALESCE(""Content"", '\\x'::bytea), 'sha256'), 'hex'), COALESCE(""Content"", '\\x'::bytea), octet_length(COALESCE(""Content"", '\\x'::bytea))
                    ) AS hashed
                    ON CONFLICT DO NOTHING;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    UPDATE mail_archiver.""EmailAttachments"" AS a
                    SET ""EmailAttachmentContentId"" = c.""Id""
                    FROM mail_archiver.""EmailAttachmentContents"" AS c
                    WHERE encode(digest(a.""Content"", 'sha256'), 'hex') = c.""ContentHash"";
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'mail_archiver'
                        AND table_name = 'EmailAttachments'
                        AND column_name = 'EmailAttachmentContentId'
                    ) THEN
                        ALTER TABLE mail_archiver.""EmailAttachments""
                        ALTER COLUMN ""EmailAttachmentContentId"" SET NOT NULL;
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_indexes
                        WHERE schemaname = 'mail_archiver'
                        AND indexname = 'IX_EmailAttachments_EmailAttachmentContentId'
                    ) THEN
                        CREATE INDEX ""IX_EmailAttachments_EmailAttachmentContentId""
                        ON mail_archiver.""EmailAttachments"" (""EmailAttachmentContentId"");
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.table_constraints
                        WHERE constraint_type = 'FOREIGN KEY'
                        AND table_schema = 'mail_archiver'
                        AND constraint_name = 'FK_EmailAttachments_EmailAttachmentContents_EmailAttachmentContentId'
                    ) THEN
                        ALTER TABLE mail_archiver.""EmailAttachments""
                        ADD CONSTRAINT ""FK_EmailAttachments_EmailAttachmentContents_EmailAttachmentContentId""
                            FOREIGN KEY (""EmailAttachmentContentId"")
                            REFERENCES mail_archiver.""EmailAttachmentContents"" (""Id"")
                            ON DELETE RESTRICT;
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'mail_archiver'
                        AND table_name = 'EmailAttachments'
                        AND column_name = 'Content'
                    ) THEN
                        ALTER TABLE mail_archiver.""EmailAttachments""
                        DROP COLUMN ""Content"";
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_indexes
                        WHERE schemaname = 'mail_archiver'
                        AND tablename = 'EmailAttachments'
                        AND indexname = 'idx_emailattachments_filename_fulltext'
                    ) THEN
                        CREATE INDEX ""idx_emailattachments_filename_fulltext""
                        ON mail_archiver.""EmailAttachments""
                        USING GIN (to_tsvector('simple', COALESCE(""FileName"", '')));
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'mail_archiver'
                        AND table_name = 'EmailAttachments'
                        AND column_name = 'Content'
                    ) THEN
                        ALTER TABLE mail_archiver.""EmailAttachments""
                        ADD COLUMN ""Content"" bytea NOT NULL DEFAULT '\\x'::bytea;
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'mail_archiver'
                        AND table_name = 'EmailAttachments'
                        AND column_name = 'EmailAttachmentContentId'
                    ) THEN
                        UPDATE mail_archiver.""EmailAttachments"" AS a
                        SET ""Content"" = c.""Content""
                        FROM mail_archiver.""EmailAttachmentContents"" AS c
                        WHERE a.""EmailAttachmentContentId"" = c.""Id"";
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM information_schema.table_constraints
                        WHERE constraint_type = 'FOREIGN KEY'
                        AND table_schema = 'mail_archiver'
                        AND constraint_name = 'FK_EmailAttachments_EmailAttachmentContents_EmailAttachmentContentId'
                    ) THEN
                        ALTER TABLE mail_archiver.""EmailAttachments""
                        DROP CONSTRAINT ""FK_EmailAttachments_EmailAttachmentContents_EmailAttachmentContentId"";
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM pg_indexes
                        WHERE schemaname = 'mail_archiver'
                        AND indexname = 'IX_EmailAttachments_EmailAttachmentContentId'
                    ) THEN
                        DROP INDEX mail_archiver.""IX_EmailAttachments_EmailAttachmentContentId"";
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'mail_archiver'
                        AND table_name = 'EmailAttachments'
                        AND column_name = 'EmailAttachmentContentId'
                    ) THEN
                        ALTER TABLE mail_archiver.""EmailAttachments""
                        DROP COLUMN ""EmailAttachmentContentId"";
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM pg_indexes
                        WHERE schemaname = 'mail_archiver'
                        AND indexname = 'IX_EmailAttachmentContents_ContentHash'
                    ) THEN
                        DROP INDEX mail_archiver.""IX_EmailAttachmentContents_ContentHash"";
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM information_schema.tables
                        WHERE table_schema = 'mail_archiver'
                        AND table_name = 'EmailAttachmentContents'
                    ) THEN
                        DROP TABLE mail_archiver.""EmailAttachmentContents"";
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM pg_indexes
                        WHERE schemaname = 'mail_archiver'
                        AND tablename = 'EmailAttachments'
                        AND indexname = 'idx_emailattachments_filename_fulltext'
                    ) THEN
                        DROP INDEX mail_archiver.""idx_emailattachments_filename_fulltext"";
                    END IF;
                END $$;
            ");
        }
    }
}
