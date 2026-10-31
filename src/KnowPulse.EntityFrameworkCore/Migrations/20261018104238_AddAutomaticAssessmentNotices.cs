using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomaticAssessmentNotices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_EduReminders_ReadTime",
                table: "AppEducationAssessmentReminders");

            migrationBuilder.AddColumn<DateTime>(
                name: "AvailableAtUtc",
                table: "AppEducationAssessmentReminders",
                type: "timestamp without time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "AppEducationAssessmentReminders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ResultReleaseId",
                table: "AppEducationAssessmentReminders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_EduResultRelease_NoticePublication",
                table: "AppEducationAssessmentResultReleases",
                columns: new[] { "Id", "TaskId", "TaskRevisionId" });

            // Existing manual notices were already available at their recorded time.
            migrationBuilder.Sql("UPDATE \"AppEducationAssessmentReminders\" SET \"AvailableAtUtc\" = \"RecordedAtUtc\"");

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentReminders_RecipientId",
                table: "AppEducationAssessmentReminders",
                column: "RecipientId",
                unique: true,
                filter: "\"Kind\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentReminders_ResultReleaseId_RecipientId",
                table: "AppEducationAssessmentReminders",
                columns: new[] { "ResultReleaseId", "RecipientId" },
                unique: true,
                filter: "\"Kind\" = 2");

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentReminders_ResultReleaseId_TaskId_Task~",
                table: "AppEducationAssessmentReminders",
                columns: new[] { "ResultReleaseId", "TaskId", "TaskRevisionId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_EduReminders_AvailableTime",
                table: "AppEducationAssessmentReminders",
                sql: "\"AvailableAtUtc\" >= \"RecordedAtUtc\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EduReminders_Kind",
                table: "AppEducationAssessmentReminders",
                sql: "(\"Kind\" IN (0, 1) AND \"ResultReleaseId\" IS NULL) OR (\"Kind\" = 2 AND \"ResultReleaseId\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EduReminders_ReadTime",
                table: "AppEducationAssessmentReminders",
                sql: "\"ReadAtUtc\" IS NULL OR \"ReadAtUtc\" >= \"AvailableAtUtc\"");

            migrationBuilder.AddForeignKey(
                name: "FK_EduReminders_ResultPublication",
                table: "AppEducationAssessmentReminders",
                columns: new[] { "ResultReleaseId", "TaskId", "TaskRevisionId" },
                principalTable: "AppEducationAssessmentResultReleases",
                principalColumns: new[] { "Id", "TaskId", "TaskRevisionId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EduReminders_ResultPublication",
                table: "AppEducationAssessmentReminders");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_EduResultRelease_NoticePublication",
                table: "AppEducationAssessmentResultReleases");

            migrationBuilder.DropIndex(
                name: "IX_AppEducationAssessmentReminders_RecipientId",
                table: "AppEducationAssessmentReminders");

            migrationBuilder.DropIndex(
                name: "IX_AppEducationAssessmentReminders_ResultReleaseId_RecipientId",
                table: "AppEducationAssessmentReminders");

            migrationBuilder.DropIndex(
                name: "IX_AppEducationAssessmentReminders_ResultReleaseId_TaskId_Task~",
                table: "AppEducationAssessmentReminders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EduReminders_AvailableTime",
                table: "AppEducationAssessmentReminders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EduReminders_Kind",
                table: "AppEducationAssessmentReminders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EduReminders_ReadTime",
                table: "AppEducationAssessmentReminders");

            migrationBuilder.DropColumn(
                name: "AvailableAtUtc",
                table: "AppEducationAssessmentReminders");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "AppEducationAssessmentReminders");

            migrationBuilder.DropColumn(
                name: "ResultReleaseId",
                table: "AppEducationAssessmentReminders");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EduReminders_ReadTime",
                table: "AppEducationAssessmentReminders",
                sql: "\"ReadAtUtc\" IS NULL OR \"ReadAtUtc\" >= \"RecordedAtUtc\"");
        }
    }
}
