using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddEducationQuestionExposures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppEducationQuestionExposures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    EducationSubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionDefinitionRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaperQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamPaperId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResultReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    FeedbackId = table.Column<Guid>(type: "uuid", nullable: true),
                    FeedbackRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    ExposedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationQuestionExposures", x => x.Id);
                    table.CheckConstraint("CK_EduExposures_Source", "(\"Source\" = 0 AND \"FeedbackId\" IS NULL AND \"FeedbackRevisionId\" IS NULL) OR (\"Source\" = 1 AND \"FeedbackId\" IS NOT NULL AND \"FeedbackRevisionId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_EduExposures_Definition",
                        columns: x => new { x.QuestionDefinitionRevisionId, x.OriginalQuestionId },
                        principalTable: "AppQuestionDefinitionRevisions",
                        principalColumns: new[] { "Id", "QuestionId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduExposures_Feedback",
                        columns: x => new { x.FeedbackRevisionId, x.FeedbackId, x.StudentUserId },
                        principalTable: "AppEducationStudentFeedbackRevisions",
                        principalColumns: new[] { "Id", "FeedbackId", "StudentUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduExposures_PaperQuestion",
                        columns: x => new { x.PaperQuestionId, x.ExamPaperId },
                        principalTable: "AppExamPaperQuestions",
                        principalColumns: new[] { "Id", "PaperId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduExposures_PaperRevision",
                        columns: x => new { x.TaskRevisionId, x.TaskId, x.ExamPaperId },
                        principalTable: "AppEducationAssessmentTaskRevisions",
                        principalColumns: new[] { "Id", "TaskId", "ExamPaperId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduExposures_Recipient",
                        columns: x => new { x.RecipientId, x.TaskId, x.TaskRevisionId, x.StudentUserId },
                        principalTable: "AppEducationAssessmentRecipients",
                        principalColumns: new[] { "Id", "TaskId", "TaskRevisionId", "StudentUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduExposures_Release",
                        columns: x => new { x.ResultReleaseId, x.TaskId },
                        principalTable: "AppEducationAssessmentResultReleases",
                        principalColumns: new[] { "Id", "TaskId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationQuestionExposures_FeedbackRevisionId_FeedbackId~",
                table: "AppEducationQuestionExposures",
                columns: new[] { "FeedbackRevisionId", "FeedbackId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationQuestionExposures_PaperQuestionId_ExamPaperId",
                table: "AppEducationQuestionExposures",
                columns: new[] { "PaperQuestionId", "ExamPaperId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationQuestionExposures_QuestionDefinitionRevisionId_~",
                table: "AppEducationQuestionExposures",
                columns: new[] { "QuestionDefinitionRevisionId", "OriginalQuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationQuestionExposures_RecipientId_TaskId_TaskRevisi~",
                table: "AppEducationQuestionExposures",
                columns: new[] { "RecipientId", "TaskId", "TaskRevisionId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationQuestionExposures_ResultReleaseId_TaskId",
                table: "AppEducationQuestionExposures",
                columns: new[] { "ResultReleaseId", "TaskId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationQuestionExposures_TaskRevisionId_TaskId_ExamPap~",
                table: "AppEducationQuestionExposures",
                columns: new[] { "TaskRevisionId", "TaskId", "ExamPaperId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduExposures_FeedbackOnce",
                table: "AppEducationQuestionExposures",
                columns: new[] { "StudentUserId", "RecipientId", "ResultReleaseId", "FeedbackRevisionId", "PaperQuestionId" },
                unique: true,
                filter: "\"Source\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_EduExposures_ReviewOnce",
                table: "AppEducationQuestionExposures",
                columns: new[] { "StudentUserId", "RecipientId", "ResultReleaseId", "PaperQuestionId" },
                unique: true,
                filter: "\"Source\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_EduExposures_StudentOriginal",
                table: "AppEducationQuestionExposures",
                columns: new[] { "TenantId", "StudentUserId", "EducationSubjectId", "OriginalQuestionId", "ExposedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppEducationQuestionExposures");
        }
    }
}
