using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddEducationAssessmentReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_EduAssessmentRevision_ReminderOwner",
                table: "AppEducationAssessmentTaskRevisions",
                columns: new[] { "Id", "TaskId", "SchoolId", "OwnerUserId" });

            migrationBuilder.CreateTable(
                name: "AppEducationAssessmentReminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeacherUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationAssessmentReminders", x => x.Id);
                    table.CheckConstraint("CK_EduReminders_Fingerprint", "length(\"RequestFingerprint\") = 64");
                    table.CheckConstraint("CK_EduReminders_ReadTime", "\"ReadAtUtc\" IS NULL OR \"ReadAtUtc\" >= \"RecordedAtUtc\"");
                    table.ForeignKey(
                        name: "FK_EduReminders_OwnerRevision",
                        columns: x => new { x.TaskRevisionId, x.TaskId, x.SchoolId, x.TeacherUserId },
                        principalTable: "AppEducationAssessmentTaskRevisions",
                        principalColumns: new[] { "Id", "TaskId", "SchoolId", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduReminders_Recipient",
                        columns: x => new { x.RecipientId, x.TaskId, x.TaskRevisionId, x.StudentUserId },
                        principalTable: "AppEducationAssessmentRecipients",
                        principalColumns: new[] { "Id", "TaskId", "TaskRevisionId", "StudentUserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentReminders_RecipientId_RecordedAtUtc",
                table: "AppEducationAssessmentReminders",
                columns: new[] { "RecipientId", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentReminders_RecipientId_TaskId_TaskRevi~",
                table: "AppEducationAssessmentReminders",
                columns: new[] { "RecipientId", "TaskId", "TaskRevisionId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentReminders_TaskRevisionId_TaskId_Schoo~",
                table: "AppEducationAssessmentReminders",
                columns: new[] { "TaskRevisionId", "TaskId", "SchoolId", "TeacherUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentReminders_TenantId_StudentUserId_Scho~",
                table: "AppEducationAssessmentReminders",
                columns: new[] { "TenantId", "StudentUserId", "SchoolId", "RecordedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppEducationAssessmentReminders");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_EduAssessmentRevision_ReminderOwner",
                table: "AppEducationAssessmentTaskRevisions");
        }
    }
}
