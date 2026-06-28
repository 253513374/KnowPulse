using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class Mvp3KnowledgeFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppKnowledgeFeedbacks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    KnowledgeItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubmitterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    HandlerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    HandledTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Resolution = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    LinkedKnowledgeVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClosedTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CloseReason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
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
                    table.PrimaryKey("PK_AppKnowledgeFeedbacks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppKnowledgeFeedbacks_AppKnowledgeItems_KnowledgeItemId",
                        column: x => x.KnowledgeItemId,
                        principalTable: "AppKnowledgeItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppKnowledgeFeedbacks_AppKnowledgeVersions_KnowledgeVersion~",
                        column: x => x.KnowledgeVersionId,
                        principalTable: "AppKnowledgeVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppKnowledgeFeedbacks_AppKnowledgeVersions_LinkedKnowledgeV~",
                        column: x => x.LinkedKnowledgeVersionId,
                        principalTable: "AppKnowledgeVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppKnowledgeFeedbacks_KnowledgeItemId",
                table: "AppKnowledgeFeedbacks",
                column: "KnowledgeItemId");

            migrationBuilder.CreateIndex(
                name: "IX_AppKnowledgeFeedbacks_KnowledgeVersionId",
                table: "AppKnowledgeFeedbacks",
                column: "KnowledgeVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_AppKnowledgeFeedbacks_LinkedKnowledgeVersionId",
                table: "AppKnowledgeFeedbacks",
                column: "LinkedKnowledgeVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_AppKnowledgeFeedbacks_SubmitterUserId",
                table: "AppKnowledgeFeedbacks",
                column: "SubmitterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AppKnowledgeFeedbacks_TenantId_Status_CreationTime",
                table: "AppKnowledgeFeedbacks",
                columns: new[] { "TenantId", "Status", "CreationTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppKnowledgeFeedbacks");
        }
    }
}
