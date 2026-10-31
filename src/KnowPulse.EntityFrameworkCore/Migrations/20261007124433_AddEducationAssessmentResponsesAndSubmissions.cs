using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddEducationAssessmentResponsesAndSubmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastSavedAtUtc",
                table: "AppEducationAssessmentAttempts",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResponseSetVersion",
                table: "AppEducationAssessmentAttempts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "SubmissionId",
                table: "AppEducationAssessmentAttempts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ExamPapers_Snapshot",
                table: "AppExamPapers",
                columns: new[] { "Id", "SnapshotVersion" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ExamPaperQuestions_Paper",
                table: "AppExamPaperQuestions",
                columns: new[] { "Id", "PaperId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_EduAttempts_StudentPaper",
                table: "AppEducationAssessmentAttempts",
                columns: new[] { "Id", "StudentUserId", "ExamPaperId" });

            migrationBuilder.CreateTable(
                name: "AppEducationAssessmentAnswerAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamPaperId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaperQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BlobName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
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
                    table.PrimaryKey("PK_AppEducationAssessmentAnswerAttachments", x => x.Id);
                    table.CheckConstraint("CK_EduAnswerAttachments_Upload", "\"Size\" > 0 AND \"Size\" <= 20971520 AND \"ContentType\" IN ('image/jpeg', 'image/png', 'image/webp') AND (\"UploadedAtUtc\" IS NULL OR \"UploadedAtUtc\" >= \"RequestedAtUtc\")");
                    table.ForeignKey(
                        name: "FK_EduAnswerAttachments_AttemptStudentPaper",
                        columns: x => new { x.AttemptId, x.StudentUserId, x.ExamPaperId },
                        principalTable: "AppEducationAssessmentAttempts",
                        principalColumns: new[] { "Id", "StudentUserId", "ExamPaperId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduAnswerAttachments_PaperQuestion",
                        columns: x => new { x.PaperQuestionId, x.ExamPaperId },
                        principalTable: "AppExamPaperQuestions",
                        principalColumns: new[] { "Id", "PaperId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationAssessmentResponseRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamPaperId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaperQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaperSnapshotVersion = table.Column<int>(type: "integer", nullable: false),
                    ExpectedResponseSetVersion = table.Column<int>(type: "integer", nullable: false),
                    ResultResponseSetVersion = table.Column<int>(type: "integer", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResponseJson = table.Column<string>(type: "character varying(200000)", maxLength: 200000, nullable: false),
                    ResponseHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SavedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationAssessmentResponseRevisions", x => x.Id);
                    table.UniqueConstraint("AK_EduResponseRevisions_QuestionVersion", x => new { x.Id, x.AttemptId, x.ExamPaperId, x.PaperQuestionId, x.ResultResponseSetVersion });
                    table.CheckConstraint("CK_EduResponseRevisions_Version", "\"PaperSnapshotVersion\" > 0 AND \"ExpectedResponseSetVersion\" >= 0 AND \"ResultResponseSetVersion\" > 0 AND CAST(\"ResultResponseSetVersion\" AS bigint) = CAST(\"ExpectedResponseSetVersion\" AS bigint) + 1");
                    table.ForeignKey(
                        name: "FK_EduResponseRevisions_AttemptStudentPaper",
                        columns: x => new { x.AttemptId, x.StudentUserId, x.ExamPaperId },
                        principalTable: "AppEducationAssessmentAttempts",
                        principalColumns: new[] { "Id", "StudentUserId", "ExamPaperId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduResponseRevisions_PaperQuestion",
                        columns: x => new { x.PaperQuestionId, x.ExamPaperId },
                        principalTable: "AppExamPaperQuestions",
                        principalColumns: new[] { "Id", "PaperId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduResponseRevisions_PaperSnapshot",
                        columns: x => new { x.ExamPaperId, x.PaperSnapshotVersion },
                        principalTable: "AppExamPapers",
                        principalColumns: new[] { "Id", "SnapshotVersion" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationAssessmentSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamPaperId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaperSnapshotVersion = table.Column<int>(type: "integer", nullable: false),
                    ResponseSetVersion = table.Column<int>(type: "integer", nullable: false),
                    Cause = table.Column<int>(type: "integer", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConfirmedSavedOnly = table.Column<bool>(type: "boolean", nullable: false),
                    ManifestJson = table.Column<string>(type: "character varying(1000000)", maxLength: 1000000, nullable: false),
                    EffectiveAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    SealedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationAssessmentSubmissions", x => x.Id);
                    table.UniqueConstraint("AK_EduSubmissions_AttemptVersion", x => new { x.Id, x.AttemptId, x.ResponseSetVersion });
                    table.CheckConstraint("CK_EduSubmissions_Receipt", "\"PaperSnapshotVersion\" > 0 AND \"ResponseSetVersion\" >= 0 AND \"Cause\" IN (0, 1) AND \"EffectiveAtUtc\" <= \"SealedAtUtc\" AND (\"Cause\" = 0 OR NOT \"ConfirmedSavedOnly\")");
                    table.ForeignKey(
                        name: "FK_EduSubmissions_AttemptStudentPaper",
                        columns: x => new { x.AttemptId, x.StudentUserId, x.ExamPaperId },
                        principalTable: "AppEducationAssessmentAttempts",
                        principalColumns: new[] { "Id", "StudentUserId", "ExamPaperId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduSubmissions_PaperSnapshot",
                        columns: x => new { x.ExamPaperId, x.PaperSnapshotVersion },
                        principalTable: "AppExamPapers",
                        principalColumns: new[] { "Id", "SnapshotVersion" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationAssessmentCurrentResponses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamPaperId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaperQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResponseSetVersion = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_AppEducationAssessmentCurrentResponses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EduCurrentResponses_RevisionQuestionVersion",
                        columns: x => new { x.CurrentRevisionId, x.AttemptId, x.ExamPaperId, x.PaperQuestionId, x.ResponseSetVersion },
                        principalTable: "AppEducationAssessmentResponseRevisions",
                        principalColumns: new[] { "Id", "AttemptId", "ExamPaperId", "PaperQuestionId", "ResultResponseSetVersion" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentAttempts_SubmissionId_Id_ResponseSetV~",
                table: "AppEducationAssessmentAttempts",
                columns: new[] { "SubmissionId", "Id", "ResponseSetVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_EduAttempts_DueCutoff",
                table: "AppEducationAssessmentAttempts",
                columns: new[] { "SubmissionId", "ExpiresAtUtc" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_EduAttempts_ResponseVersion",
                table: "AppEducationAssessmentAttempts",
                sql: "(\"ResponseSetVersion\" = 0 AND \"LastSavedAtUtc\" IS NULL) OR (\"ResponseSetVersion\" > 0 AND \"LastSavedAtUtc\" IS NOT NULL AND \"LastSavedAtUtc\" >= \"StartedAtUtc\" AND \"LastSavedAtUtc\" < \"ExpiresAtUtc\")");

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentAnswerAttachments_AttemptId_StudentUs~",
                table: "AppEducationAssessmentAnswerAttachments",
                columns: new[] { "AttemptId", "StudentUserId", "ExamPaperId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentAnswerAttachments_PaperQuestionId_Exa~",
                table: "AppEducationAssessmentAnswerAttachments",
                columns: new[] { "PaperQuestionId", "ExamPaperId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduAnswerAttachments_AttemptQuestion",
                table: "AppEducationAssessmentAnswerAttachments",
                columns: new[] { "AttemptId", "PaperQuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentCurrentResponses_CurrentRevisionId_At~",
                table: "AppEducationAssessmentCurrentResponses",
                columns: new[] { "CurrentRevisionId", "AttemptId", "ExamPaperId", "PaperQuestionId", "ResponseSetVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_EduCurrentResponses_AttemptQuestion",
                table: "AppEducationAssessmentCurrentResponses",
                columns: new[] { "AttemptId", "PaperQuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentResponseRevisions_AttemptId_StudentUs~",
                table: "AppEducationAssessmentResponseRevisions",
                columns: new[] { "AttemptId", "StudentUserId", "ExamPaperId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentResponseRevisions_ExamPaperId_PaperSn~",
                table: "AppEducationAssessmentResponseRevisions",
                columns: new[] { "ExamPaperId", "PaperSnapshotVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentResponseRevisions_PaperQuestionId_Exa~",
                table: "AppEducationAssessmentResponseRevisions",
                columns: new[] { "PaperQuestionId", "ExamPaperId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduResponseRevisions_AttemptVersion",
                table: "AppEducationAssessmentResponseRevisions",
                columns: new[] { "AttemptId", "ResultResponseSetVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentSubmissions_AttemptId_StudentUserId_E~",
                table: "AppEducationAssessmentSubmissions",
                columns: new[] { "AttemptId", "StudentUserId", "ExamPaperId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentSubmissions_ExamPaperId_PaperSnapshot~",
                table: "AppEducationAssessmentSubmissions",
                columns: new[] { "ExamPaperId", "PaperSnapshotVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_EduSubmissions_OnePerAttempt",
                table: "AppEducationAssessmentSubmissions",
                column: "AttemptId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_EduAttempts_SubmissionVersion",
                table: "AppEducationAssessmentAttempts",
                columns: new[] { "SubmissionId", "Id", "ResponseSetVersion" },
                principalTable: "AppEducationAssessmentSubmissions",
                principalColumns: new[] { "Id", "AttemptId", "ResponseSetVersion" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EduAttempts_SubmissionVersion",
                table: "AppEducationAssessmentAttempts");

            migrationBuilder.DropTable(
                name: "AppEducationAssessmentAnswerAttachments");

            migrationBuilder.DropTable(
                name: "AppEducationAssessmentCurrentResponses");

            migrationBuilder.DropTable(
                name: "AppEducationAssessmentSubmissions");

            migrationBuilder.DropTable(
                name: "AppEducationAssessmentResponseRevisions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ExamPapers_Snapshot",
                table: "AppExamPapers");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ExamPaperQuestions_Paper",
                table: "AppExamPaperQuestions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_EduAttempts_StudentPaper",
                table: "AppEducationAssessmentAttempts");

            migrationBuilder.DropIndex(
                name: "IX_AppEducationAssessmentAttempts_SubmissionId_Id_ResponseSetV~",
                table: "AppEducationAssessmentAttempts");

            migrationBuilder.DropIndex(
                name: "IX_EduAttempts_DueCutoff",
                table: "AppEducationAssessmentAttempts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EduAttempts_ResponseVersion",
                table: "AppEducationAssessmentAttempts");

            migrationBuilder.DropColumn(
                name: "LastSavedAtUtc",
                table: "AppEducationAssessmentAttempts");

            migrationBuilder.DropColumn(
                name: "ResponseSetVersion",
                table: "AppEducationAssessmentAttempts");

            migrationBuilder.DropColumn(
                name: "SubmissionId",
                table: "AppEducationAssessmentAttempts");
        }
    }
}
