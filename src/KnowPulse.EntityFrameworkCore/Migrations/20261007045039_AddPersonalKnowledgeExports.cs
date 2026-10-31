using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalKnowledgeExports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppPersonalKnowledgeExports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    LibraryId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SnapshotAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastHeartbeatAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastCleanupAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: true),
                    RunStopped = table.Column<bool>(type: "boolean", nullable: false),
                    SourceManifestJson = table.Column<string>(type: "text", nullable: true),
                    BlobName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    TemporaryFilePending = table.Column<bool>(type: "boolean", nullable: false),
                    RetiredArtifactsJson = table.Column<string>(type: "text", nullable: true),
                    HasRetiredArtifacts = table.Column<bool>(type: "boolean", nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: true),
                    FailureStage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppPersonalKnowledgeExports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppPersonalKnowledgeExports_AppKnowledgeLibraries_LibraryId~",
                        columns: x => new { x.LibraryId, x.OwnerUserId },
                        principalTable: "AppKnowledgeLibraries",
                        principalColumns: new[] { "Id", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppPersonalKnowledgeExports_LibraryId_OwnerUserId",
                table: "AppPersonalKnowledgeExports",
                columns: new[] { "LibraryId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppPersonalKnowledgeExports_Status_LastHeartbeatAtUtc",
                table: "AppPersonalKnowledgeExports",
                columns: new[] { "Status", "LastHeartbeatAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppPersonalKnowledgeExports_TenantId_OwnerUserId_CreationTi~",
                table: "AppPersonalKnowledgeExports",
                columns: new[] { "TenantId", "OwnerUserId", "CreationTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppPersonalKnowledgeExports");
        }
    }
}
