using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddIndependentQuestionPractice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppEducationQuestionPractices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeacherUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResultReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionGradeId = table.Column<Guid>(type: "uuid", nullable: false),
                    GradeRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamPaperId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaperQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionDefinitionRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    SubmissionOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubmissionFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PreparationFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ResponseJson = table.Column<string>(type: "character varying(200000)", maxLength: 200000, nullable: true),
                    EvaluationJson = table.Column<string>(type: "character varying(64000)", maxLength: 64000, nullable: true),
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
                    table.PrimaryKey("PK_AppEducationQuestionPractices", x => x.Id);
                    table.UniqueConstraint("AK_EduPractice_Student", x => new { x.Id, x.StudentUserId });
                    table.CheckConstraint("CK_EduPractice_Submission", "length(\"StartFingerprint\") = 64 AND ((\"SubmissionOperationId\" IS NULL AND \"SubmissionFingerprint\" IS NULL AND \"PreparationFingerprint\" IS NULL AND \"SubmittedAtUtc\" IS NULL AND \"ResponseJson\" IS NULL AND \"EvaluationJson\" IS NULL) OR (\"SubmissionOperationId\" IS NOT NULL AND \"SubmissionOperationId\" <> \"Id\" AND \"SubmissionFingerprint\" IS NOT NULL AND \"PreparationFingerprint\" IS NOT NULL AND \"SubmittedAtUtc\" IS NOT NULL AND length(\"SubmissionFingerprint\") = 64 AND length(\"PreparationFingerprint\") = 64 AND \"SubmittedAtUtc\" >= \"StartedAtUtc\" AND \"ResponseJson\" IS NOT NULL AND \"EvaluationJson\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_EduPractice_Definition",
                        column: x => x.QuestionDefinitionRevisionId,
                        principalTable: "AppQuestionDefinitionRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduPractice_FrozenQuestion",
                        columns: x => new { x.PaperQuestionId, x.ExamPaperId },
                        principalTable: "AppExamPaperQuestions",
                        principalColumns: new[] { "Id", "PaperId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduPractice_Recipient",
                        columns: x => new { x.RecipientId, x.TaskId, x.TaskRevisionId, x.StudentUserId },
                        principalTable: "AppEducationAssessmentRecipients",
                        principalColumns: new[] { "Id", "TaskId", "TaskRevisionId", "StudentUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduPractice_SourceGrade",
                        columns: x => new { x.QuestionGradeId, x.TeacherUserId, x.StudentUserId, x.TaskId, x.TaskRevisionId, x.SourceSubmissionId, x.PaperQuestionId },
                        principalTable: "AppEducationAssessmentQuestionGrades",
                        principalColumns: new[] { "Id", "OwnerUserId", "StudentUserId", "TaskId", "TaskRevisionId", "SubmissionId", "PaperQuestionId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduPractice_SourceGradeRevision",
                        column: x => x.GradeRevisionId,
                        principalTable: "AppEducationAssessmentGradeRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduPractice_SourceRelease",
                        columns: x => new { x.ResultReleaseId, x.TaskId },
                        principalTable: "AppEducationAssessmentResultReleases",
                        principalColumns: new[] { "Id", "TaskId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduPractice_TaskRevision",
                        columns: x => new { x.TaskRevisionId, x.TaskId, x.ExamPaperId },
                        principalTable: "AppEducationAssessmentTaskRevisions",
                        principalColumns: new[] { "Id", "TaskId", "ExamPaperId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationPracticeImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    PracticeId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BlobName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReservedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
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
                    table.PrimaryKey("PK_AppEducationPracticeImages", x => x.Id);
                    table.CheckConstraint("CK_EduPracticeImages_Upload", "\"Size\" BETWEEN 1 AND 5242880 AND length(\"ContentHash\") = 64 AND length(\"RequestFingerprint\") = 64 AND (\"UploadedAtUtc\" IS NULL OR \"UploadedAtUtc\" >= \"ReservedAtUtc\")");
                    table.ForeignKey(
                        name: "FK_EduPracticeImages_Practice",
                        columns: x => new { x.PracticeId, x.StudentUserId },
                        principalTable: "AppEducationQuestionPractices",
                        principalColumns: new[] { "Id", "StudentUserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EduPracticeImages_Student",
                table: "AppEducationPracticeImages",
                columns: new[] { "PracticeId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationQuestionPractices_GradeRevisionId",
                table: "AppEducationQuestionPractices",
                column: "GradeRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationQuestionPractices_PaperQuestionId_ExamPaperId",
                table: "AppEducationQuestionPractices",
                columns: new[] { "PaperQuestionId", "ExamPaperId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationQuestionPractices_QuestionDefinitionRevisionId",
                table: "AppEducationQuestionPractices",
                column: "QuestionDefinitionRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationQuestionPractices_QuestionGradeId_TeacherUserId~",
                table: "AppEducationQuestionPractices",
                columns: new[] { "QuestionGradeId", "TeacherUserId", "StudentUserId", "TaskId", "TaskRevisionId", "SourceSubmissionId", "PaperQuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationQuestionPractices_RecipientId_TaskId_TaskRevisi~",
                table: "AppEducationQuestionPractices",
                columns: new[] { "RecipientId", "TaskId", "TaskRevisionId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationQuestionPractices_ResultReleaseId_TaskId",
                table: "AppEducationQuestionPractices",
                columns: new[] { "ResultReleaseId", "TaskId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationQuestionPractices_TaskRevisionId_TaskId_ExamPap~",
                table: "AppEducationQuestionPractices",
                columns: new[] { "TaskRevisionId", "TaskId", "ExamPaperId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduPractice_Student",
                table: "AppEducationQuestionPractices",
                columns: new[] { "TenantId", "StudentUserId", "RecipientId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EduPractice_SubmissionOperation",
                table: "AppEducationQuestionPractices",
                column: "SubmissionOperationId",
                unique: true,
                filter: "\"SubmissionOperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EduPractice_Teacher",
                table: "AppEducationQuestionPractices",
                columns: new[] { "TenantId", "TeacherUserId", "SourceSubmissionId", "SubmittedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "AppEducationQuestionPractices") OR EXISTS (SELECT 1 FROM "AppEducationPracticeImages") THEN
                        RAISE EXCEPTION 'Practice or original image history exists; downgrade requires an explicit compatible restore.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "AppEducationPracticeImages");

            migrationBuilder.DropTable(
                name: "AppEducationQuestionPractices");
        }
    }
}
