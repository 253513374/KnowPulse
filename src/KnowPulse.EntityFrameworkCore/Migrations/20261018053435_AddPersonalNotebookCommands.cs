using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalNotebookCommands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppPersonalNotebookCommands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    NoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    VersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    VersionNumber = table.Column<int>(type: "integer", nullable: true),
                    AttachmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FileSize = table.Column<long>(type: "bigint", nullable: true),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppPersonalNotebookCommands", x => x.Id);
                    table.CheckConstraint("CK_PersonalNotebookCommands_Receipt", "length(\"RequestFingerprint\") = 64 AND ((\"Kind\" IN (0,1,2) AND \"VersionId\" IS NOT NULL AND \"VersionNumber\" IS NOT NULL AND \"VersionNumber\" > 0) OR (\"Kind\" = 3 AND \"VersionId\" IS NULL AND \"VersionNumber\" IS NULL)) AND ((\"Kind\" = 2 AND \"AttachmentId\" IS NOT NULL AND \"ContentHash\" IS NOT NULL AND length(\"ContentHash\") = 64 AND \"FileSize\" IS NOT NULL AND \"FileSize\" >= 0) OR (\"Kind\" <> 2 AND \"AttachmentId\" IS NULL AND \"ContentHash\" IS NULL AND \"FileSize\" IS NULL))");
                    table.ForeignKey(
                        name: "FK_AppPersonalNotebookCommands_AppKnowledgeAttachments_Attachm~",
                        column: x => x.AttachmentId,
                        principalTable: "AppKnowledgeAttachments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppPersonalNotebookCommands_AppKnowledgeItems_NoteId",
                        column: x => x.NoteId,
                        principalTable: "AppKnowledgeItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppPersonalNotebookCommands_AppKnowledgeVersions_VersionId",
                        column: x => x.VersionId,
                        principalTable: "AppKnowledgeVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppPersonalNotebookCommands_AttachmentId",
                table: "AppPersonalNotebookCommands",
                column: "AttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AppPersonalNotebookCommands_NoteId",
                table: "AppPersonalNotebookCommands",
                column: "NoteId");

            migrationBuilder.CreateIndex(
                name: "IX_AppPersonalNotebookCommands_VersionId",
                table: "AppPersonalNotebookCommands",
                column: "VersionId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonalNotebookCommands_Owner",
                table: "AppPersonalNotebookCommands",
                columns: new[] { "TenantId", "OwnerUserId", "NoteId", "RecordedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "AppPersonalNotebookCommands") THEN
                        RAISE EXCEPTION 'Notebook operation history exists; downgrade requires an explicit compatible restore.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "AppPersonalNotebookCommands");
        }
    }
}
