using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class P3KnowledgeIndexJobOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastHeartbeatTime",
                table: "AppKnowledgeIndexJobs",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProcessingStage",
                table: "AppKnowledgeIndexJobs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProgressPercent",
                table: "AppKnowledgeIndexJobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "RetryOfJobId",
                table: "AppKnowledgeIndexJobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "AppKnowledgeIndexJobs"
                SET "ProcessingStage" = CASE "Status"
                        WHEN 0 THEN 'queued'
                        WHEN 1 THEN 'starting'
                        WHEN 2 THEN 'completed'
                        WHEN 3 THEN COALESCE("FailureStage", 'failed')
                        WHEN 4 THEN 'outdated'
                        WHEN 5 THEN 'deleted'
                    END,
                    "ProgressPercent" = CASE WHEN "Status" = 2 THEN 100 ELSE 0 END,
                    "LastHeartbeatTime" = CASE
                        WHEN "Status" = 1 THEN COALESCE("StartedTime", "CreationTime")
                        WHEN "Status" IN (2, 3, 5) THEN COALESCE("CompletedTime", "LastModificationTime", "CreationTime")
                        ELSE NULL
                    END;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AppKnowledgeIndexJobs_RetryOfJobId",
                table: "AppKnowledgeIndexJobs",
                column: "RetryOfJobId");

            migrationBuilder.CreateIndex(
                name: "IX_AppKnowledgeIndexJobs_Status_LastHeartbeatTime",
                table: "AppKnowledgeIndexJobs",
                columns: new[] { "Status", "LastHeartbeatTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppKnowledgeIndexJobs_RetryOfJobId",
                table: "AppKnowledgeIndexJobs");

            migrationBuilder.DropIndex(
                name: "IX_AppKnowledgeIndexJobs_Status_LastHeartbeatTime",
                table: "AppKnowledgeIndexJobs");

            migrationBuilder.DropColumn(
                name: "LastHeartbeatTime",
                table: "AppKnowledgeIndexJobs");

            migrationBuilder.DropColumn(
                name: "ProcessingStage",
                table: "AppKnowledgeIndexJobs");

            migrationBuilder.DropColumn(
                name: "ProgressPercent",
                table: "AppKnowledgeIndexJobs");

            migrationBuilder.DropColumn(
                name: "RetryOfJobId",
                table: "AppKnowledgeIndexJobs");
        }
    }
}
