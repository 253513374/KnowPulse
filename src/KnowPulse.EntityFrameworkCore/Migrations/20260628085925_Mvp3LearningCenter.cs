using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class Mvp3LearningCenter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppLearningKnowledgeReadRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    LearningPathId = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastReadTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CompletedTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ReadCount = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_AppLearningKnowledgeReadRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppLearningKnowledgeReadRecords_AppKnowledgeItems_Knowledge~",
                        column: x => x.KnowledgeItemId,
                        principalTable: "AppKnowledgeItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppLearningKnowledgeReadRecords_AppKnowledgeVersions_Knowle~",
                        column: x => x.KnowledgeVersionId,
                        principalTable: "AppKnowledgeVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppLearningKnowledgeReadRecords_AppLearningPaths_LearningPa~",
                        column: x => x.LearningPathId,
                        principalTable: "AppLearningPaths",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppLearningKnowledgeReadRecords_CompletedTime",
                table: "AppLearningKnowledgeReadRecords",
                column: "CompletedTime");

            migrationBuilder.CreateIndex(
                name: "IX_AppLearningKnowledgeReadRecords_KnowledgeItemId",
                table: "AppLearningKnowledgeReadRecords",
                column: "KnowledgeItemId");

            migrationBuilder.CreateIndex(
                name: "IX_AppLearningKnowledgeReadRecords_KnowledgeVersionId",
                table: "AppLearningKnowledgeReadRecords",
                column: "KnowledgeVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_AppLearningKnowledgeReadRecords_LearningPathId_UserId_Knowl~",
                table: "AppLearningKnowledgeReadRecords",
                columns: new[] { "LearningPathId", "UserId", "KnowledgeItemId", "KnowledgeVersionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppLearningKnowledgeReadRecords_UserId_LastReadTime",
                table: "AppLearningKnowledgeReadRecords",
                columns: new[] { "UserId", "LastReadTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppLearningKnowledgeReadRecords");
        }
    }
}
