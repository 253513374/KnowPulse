using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddEducationQuestionCollections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppEducationQuestionCollections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamPaperId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaperQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResultReleaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreviousOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    IsCollected = table.Column<bool>(type: "boolean", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationQuestionCollections", x => x.Id);
                    table.UniqueConstraint("AK_EduCollection_StudentQuestion", x => new { x.Id, x.RecipientId, x.PaperQuestionId, x.StudentUserId });
                    table.CheckConstraint("CK_EduCollection_Revision", "length(\"RequestFingerprint\") = 64 AND ((\"RevisionNumber\" = 1 AND \"PreviousOperationId\" IS NULL AND \"IsCollected\") OR (\"RevisionNumber\" > 1 AND \"PreviousOperationId\" IS NOT NULL)) AND ((\"IsCollected\" AND \"ResultReleaseId\" IS NOT NULL) OR (NOT \"IsCollected\" AND \"ResultReleaseId\" IS NULL))");
                    table.ForeignKey(
                        name: "FK_EduCollection_FrozenQuestion",
                        columns: x => new { x.PaperQuestionId, x.ExamPaperId },
                        principalTable: "AppExamPaperQuestions",
                        principalColumns: new[] { "Id", "PaperId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduCollection_Previous",
                        columns: x => new { x.PreviousOperationId, x.RecipientId, x.PaperQuestionId, x.StudentUserId },
                        principalTable: "AppEducationQuestionCollections",
                        principalColumns: new[] { "Id", "RecipientId", "PaperQuestionId", "StudentUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduCollection_Recipient",
                        columns: x => new { x.RecipientId, x.TaskId, x.TaskRevisionId, x.StudentUserId },
                        principalTable: "AppEducationAssessmentRecipients",
                        principalColumns: new[] { "Id", "TaskId", "TaskRevisionId", "StudentUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduCollection_SourceRelease",
                        columns: x => new { x.ResultReleaseId, x.TaskId },
                        principalTable: "AppEducationAssessmentResultReleases",
                        principalColumns: new[] { "Id", "TaskId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduCollection_TaskRevision",
                        columns: x => new { x.TaskRevisionId, x.TaskId, x.ExamPaperId },
                        principalTable: "AppEducationAssessmentTaskRevisions",
                        principalColumns: new[] { "Id", "TaskId", "ExamPaperId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationQuestionCollections_PaperQuestionId_ExamPaperId",
                table: "AppEducationQuestionCollections",
                columns: new[] { "PaperQuestionId", "ExamPaperId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationQuestionCollections_PreviousOperationId_Recipie~",
                table: "AppEducationQuestionCollections",
                columns: new[] { "PreviousOperationId", "RecipientId", "PaperQuestionId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationQuestionCollections_RecipientId_TaskId_TaskRevi~",
                table: "AppEducationQuestionCollections",
                columns: new[] { "RecipientId", "TaskId", "TaskRevisionId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationQuestionCollections_ResultReleaseId_TaskId",
                table: "AppEducationQuestionCollections",
                columns: new[] { "ResultReleaseId", "TaskId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationQuestionCollections_TaskRevisionId_TaskId_ExamP~",
                table: "AppEducationQuestionCollections",
                columns: new[] { "TaskRevisionId", "TaskId", "ExamPaperId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduCollection_Sequence",
                table: "AppEducationQuestionCollections",
                columns: new[] { "RecipientId", "PaperQuestionId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EduCollection_Student",
                table: "AppEducationQuestionCollections",
                columns: new[] { "TenantId", "StudentUserId", "SchoolId", "RecordedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "AppEducationQuestionCollections") THEN
                        RAISE EXCEPTION 'Collection operation history exists; downgrade requires an explicit compatible restore.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "AppEducationQuestionCollections");
        }
    }
}
