using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddEducationAssessmentGradingAndResultRelease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClosureId",
                table: "AppEducationAssessmentTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConfirmedGradeSetVersion",
                table: "AppEducationAssessmentTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "CurrentResultReleaseId",
                table: "AppEducationAssessmentTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_EduSubmissions_AttemptStudent",
                table: "AppEducationAssessmentSubmissions",
                columns: new[] { "Id", "AttemptId", "StudentUserId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_EduSubmissions_AttemptStudentPaper",
                table: "AppEducationAssessmentSubmissions",
                columns: new[] { "Id", "AttemptId", "StudentUserId", "ExamPaperId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_EduResponses_AttemptPaperQuestion",
                table: "AppEducationAssessmentResponseRevisions",
                columns: new[] { "Id", "AttemptId", "ExamPaperId", "PaperQuestionId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_EduRecipients_TaskStudent",
                table: "AppEducationAssessmentRecipients",
                columns: new[] { "Id", "TaskId", "StudentUserId" });

            migrationBuilder.CreateTable(
                name: "AppEducationAssessmentClosures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpectedConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ManifestJson = table.Column<string>(type: "character varying(1000000)", maxLength: 1000000, nullable: false),
                    SubmissionSetFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ValidSubmissionCount = table.Column<int>(type: "integer", nullable: false),
                    EffectiveAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationAssessmentClosures", x => x.Id);
                    table.UniqueConstraint("AK_EduClosures_Task", x => new { x.Id, x.TaskId });
                    table.ForeignKey(
                        name: "FK_EduClosures_PublishedTask",
                        columns: x => new { x.TaskRevisionId, x.TaskId },
                        principalTable: "AppEducationAssessmentTaskRevisions",
                        principalColumns: new[] { "Id", "TaskId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationAssessmentResultReleases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClosureId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousReleaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConfirmedGradeSetVersion = table.Column<int>(type: "integer", nullable: false),
                    ExpectedConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubmissionSetFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ValidSubmissionCount = table.Column<int>(type: "integer", nullable: false),
                    ConfiguredVisibility = table.Column<int>(type: "integer", nullable: false),
                    ScoreOpensAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ReviewOpensAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ConfirmedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReleasedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationAssessmentResultReleases", x => x.Id);
                    table.UniqueConstraint("AK_EduReleases_Task", x => new { x.Id, x.TaskId });
                    table.ForeignKey(
                        name: "FK_EduReleases_Closure",
                        columns: x => new { x.ClosureId, x.TaskId },
                        principalTable: "AppEducationAssessmentClosures",
                        principalColumns: new[] { "Id", "TaskId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduReleases_Previous",
                        columns: x => new { x.PreviousReleaseId, x.TaskId },
                        principalTable: "AppEducationAssessmentResultReleases",
                        principalColumns: new[] { "Id", "TaskId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationAssessmentResultReleaseItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResultReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TotalScore = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    MaxScore = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    GradesJson = table.Column<string>(type: "character varying(1000000)", maxLength: 1000000, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationAssessmentResultReleaseItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EduReleaseItems_Recipient",
                        columns: x => new { x.RecipientId, x.TaskId, x.StudentUserId },
                        principalTable: "AppEducationAssessmentRecipients",
                        principalColumns: new[] { "Id", "TaskId", "StudentUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduReleaseItems_Release",
                        columns: x => new { x.ResultReleaseId, x.TaskId },
                        principalTable: "AppEducationAssessmentResultReleases",
                        principalColumns: new[] { "Id", "TaskId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduReleaseItems_Submission",
                        columns: x => new { x.SubmissionId, x.AttemptId, x.StudentUserId },
                        principalTable: "AppEducationAssessmentSubmissions",
                        principalColumns: new[] { "Id", "AttemptId", "StudentUserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationAssessmentGradeRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamPaperId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaperSnapshotVersion = table.Column<int>(type: "integer", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionGradeId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaperQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    BasedOnRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupersedesRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResponseRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResponseHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    QuestionDefinitionRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScoringDefinitionHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CalculationVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    ContentJson = table.Column<string>(type: "character varying(1000000)", maxLength: 1000000, nullable: false),
                    MaxScore = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ObtainedScore = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Feedback = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Reason = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpectedConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpectedGradeSetVersion = table.Column<int>(type: "integer", nullable: true),
                    ResultGradeSetVersion = table.Column<int>(type: "integer", nullable: true),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationAssessmentGradeRevisions", x => x.Id);
                    table.UniqueConstraint("AK_EduGradeRevisions_Grade", x => new { x.Id, x.QuestionGradeId });
                    table.CheckConstraint("CK_EduGradeRevisions_Marks", "\"RevisionNumber\" > 0 AND CAST(\"MaxScore\" AS numeric) > 0 AND (\"ObtainedScore\" IS NULL OR (CAST(\"ObtainedScore\" AS numeric) >= 0 AND CAST(\"ObtainedScore\" AS numeric) <= CAST(\"MaxScore\" AS numeric))) AND ((\"Kind\" = 0 AND \"ConfirmedAtUtc\" IS NULL AND \"ResultGradeSetVersion\" IS NULL AND \"ExpectedGradeSetVersion\" IS NULL) OR (\"Kind\" = 1 AND \"ConfirmedAtUtc\" IS NOT NULL AND \"ObtainedScore\" IS NOT NULL AND \"ExpectedGradeSetVersion\" IS NOT NULL AND \"ResultGradeSetVersion\" IS NOT NULL AND \"ExpectedGradeSetVersion\" >= 0 AND CAST(\"ResultGradeSetVersion\" AS bigint) = CAST(\"ExpectedGradeSetVersion\" AS bigint) + 1))");
                    table.ForeignKey(
                        name: "FK_EduGradeRevisions_Definition",
                        column: x => x.QuestionDefinitionRevisionId,
                        principalTable: "AppQuestionDefinitionRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduGradeRevisions_Previous",
                        columns: x => new { x.BasedOnRevisionId, x.QuestionGradeId },
                        principalTable: "AppEducationAssessmentGradeRevisions",
                        principalColumns: new[] { "Id", "QuestionGradeId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduGradeRevisions_Response",
                        columns: x => new { x.ResponseRevisionId, x.AttemptId, x.ExamPaperId, x.PaperQuestionId },
                        principalTable: "AppEducationAssessmentResponseRevisions",
                        principalColumns: new[] { "Id", "AttemptId", "ExamPaperId", "PaperQuestionId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduGradeRevisions_Submission",
                        columns: x => new { x.SubmissionId, x.AttemptId, x.StudentUserId, x.ExamPaperId },
                        principalTable: "AppEducationAssessmentSubmissions",
                        principalColumns: new[] { "Id", "AttemptId", "StudentUserId", "ExamPaperId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduGradeRevisions_Superseded",
                        columns: x => new { x.SupersedesRevisionId, x.QuestionGradeId },
                        principalTable: "AppEducationAssessmentGradeRevisions",
                        principalColumns: new[] { "Id", "QuestionGradeId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationAssessmentQuestionGrades",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamPaperId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaperQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    CurrentRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentConfirmedRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_AppEducationAssessmentQuestionGrades", x => x.Id);
                    table.UniqueConstraint("AK_EduGrades_SubmissionQuestion", x => new { x.Id, x.SubmissionId, x.PaperQuestionId });
                    table.ForeignKey(
                        name: "FK_EduGrades_Confirmed",
                        columns: x => new { x.CurrentConfirmedRevisionId, x.Id },
                        principalTable: "AppEducationAssessmentGradeRevisions",
                        principalColumns: new[] { "Id", "QuestionGradeId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduGrades_Current",
                        columns: x => new { x.CurrentRevisionId, x.Id },
                        principalTable: "AppEducationAssessmentGradeRevisions",
                        principalColumns: new[] { "Id", "QuestionGradeId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduGrades_PaperQuestion",
                        columns: x => new { x.PaperQuestionId, x.ExamPaperId },
                        principalTable: "AppExamPaperQuestions",
                        principalColumns: new[] { "Id", "PaperId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduGrades_Submission",
                        columns: x => new { x.SubmissionId, x.AttemptId, x.StudentUserId, x.ExamPaperId },
                        principalTable: "AppEducationAssessmentSubmissions",
                        principalColumns: new[] { "Id", "AttemptId", "StudentUserId", "ExamPaperId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduGrades_TaskOwner",
                        columns: x => new { x.TaskId, x.OwnerUserId },
                        principalTable: "AppEducationAssessmentTasks",
                        principalColumns: new[] { "Id", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduGrades_TaskRevision",
                        columns: x => new { x.TaskRevisionId, x.TaskId, x.ExamPaperId },
                        principalTable: "AppEducationAssessmentTaskRevisions",
                        principalColumns: new[] { "Id", "TaskId", "ExamPaperId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationAssessmentScoreReviewRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResultReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaperQuestionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ResolutionOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolutionRequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ExpectedResolutionConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Decision = table.Column<int>(type: "integer", nullable: true),
                    ResolutionReason = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CorrectedGradeRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResultingReleaseId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_AppEducationAssessmentScoreReviewRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EduScoreReviews_Correction",
                        column: x => x.CorrectedGradeRevisionId,
                        principalTable: "AppEducationAssessmentGradeRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduScoreReviews_Release",
                        columns: x => new { x.ResultReleaseId, x.TaskId },
                        principalTable: "AppEducationAssessmentResultReleases",
                        principalColumns: new[] { "Id", "TaskId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentTasks_ClosureId_Id",
                table: "AppEducationAssessmentTasks",
                columns: new[] { "ClosureId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentTasks_CurrentResultReleaseId_Id",
                table: "AppEducationAssessmentTasks",
                columns: new[] { "CurrentResultReleaseId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentClosures_TaskRevisionId_TaskId",
                table: "AppEducationAssessmentClosures",
                columns: new[] { "TaskRevisionId", "TaskId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduClosures_OnePerTask",
                table: "AppEducationAssessmentClosures",
                column: "TaskId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentGradeRevisions_BasedOnRevisionId_Ques~",
                table: "AppEducationAssessmentGradeRevisions",
                columns: new[] { "BasedOnRevisionId", "QuestionGradeId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentGradeRevisions_QuestionDefinitionRevi~",
                table: "AppEducationAssessmentGradeRevisions",
                column: "QuestionDefinitionRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentGradeRevisions_QuestionGradeId_Submis~",
                table: "AppEducationAssessmentGradeRevisions",
                columns: new[] { "QuestionGradeId", "SubmissionId", "PaperQuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentGradeRevisions_ResponseRevisionId_Att~",
                table: "AppEducationAssessmentGradeRevisions",
                columns: new[] { "ResponseRevisionId", "AttemptId", "ExamPaperId", "PaperQuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentGradeRevisions_SubmissionId_AttemptId~",
                table: "AppEducationAssessmentGradeRevisions",
                columns: new[] { "SubmissionId", "AttemptId", "StudentUserId", "ExamPaperId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentGradeRevisions_SupersedesRevisionId_Q~",
                table: "AppEducationAssessmentGradeRevisions",
                columns: new[] { "SupersedesRevisionId", "QuestionGradeId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduGradeRevisions_ConfirmedSet",
                table: "AppEducationAssessmentGradeRevisions",
                columns: new[] { "TaskId", "ResultGradeSetVersion" },
                unique: true,
                filter: "\"ResultGradeSetVersion\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EduGradeRevisions_Number",
                table: "AppEducationAssessmentGradeRevisions",
                columns: new[] { "QuestionGradeId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentQuestionGrades_CurrentConfirmedRevisi~",
                table: "AppEducationAssessmentQuestionGrades",
                columns: new[] { "CurrentConfirmedRevisionId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentQuestionGrades_CurrentRevisionId_Id",
                table: "AppEducationAssessmentQuestionGrades",
                columns: new[] { "CurrentRevisionId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentQuestionGrades_PaperQuestionId_ExamPa~",
                table: "AppEducationAssessmentQuestionGrades",
                columns: new[] { "PaperQuestionId", "ExamPaperId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentQuestionGrades_SubmissionId_AttemptId~",
                table: "AppEducationAssessmentQuestionGrades",
                columns: new[] { "SubmissionId", "AttemptId", "StudentUserId", "ExamPaperId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentQuestionGrades_TaskId_OwnerUserId",
                table: "AppEducationAssessmentQuestionGrades",
                columns: new[] { "TaskId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentQuestionGrades_TaskRevisionId_TaskId_~",
                table: "AppEducationAssessmentQuestionGrades",
                columns: new[] { "TaskRevisionId", "TaskId", "ExamPaperId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduGrades_OnePerQuestion",
                table: "AppEducationAssessmentQuestionGrades",
                columns: new[] { "SubmissionId", "PaperQuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentResultReleaseItems_RecipientId_TaskId~",
                table: "AppEducationAssessmentResultReleaseItems",
                columns: new[] { "RecipientId", "TaskId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentResultReleaseItems_ResultReleaseId_Ta~",
                table: "AppEducationAssessmentResultReleaseItems",
                columns: new[] { "ResultReleaseId", "TaskId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentResultReleaseItems_SubmissionId_Attem~",
                table: "AppEducationAssessmentResultReleaseItems",
                columns: new[] { "SubmissionId", "AttemptId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduReleaseItems_Recipient",
                table: "AppEducationAssessmentResultReleaseItems",
                columns: new[] { "ResultReleaseId", "RecipientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentResultReleases_ClosureId_TaskId",
                table: "AppEducationAssessmentResultReleases",
                columns: new[] { "ClosureId", "TaskId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentResultReleases_PreviousReleaseId_Task~",
                table: "AppEducationAssessmentResultReleases",
                columns: new[] { "PreviousReleaseId", "TaskId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentScoreReviewRequests_CorrectedGradeRev~",
                table: "AppEducationAssessmentScoreReviewRequests",
                column: "CorrectedGradeRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentScoreReviewRequests_ResultReleaseId_T~",
                table: "AppEducationAssessmentScoreReviewRequests",
                columns: new[] { "ResultReleaseId", "TaskId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduScoreReviews_Resolution",
                table: "AppEducationAssessmentScoreReviewRequests",
                column: "ResolutionOperationId",
                unique: true,
                filter: "\"ResolutionOperationId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_EduTasks_Closure",
                table: "AppEducationAssessmentTasks",
                columns: new[] { "ClosureId", "Id" },
                principalTable: "AppEducationAssessmentClosures",
                principalColumns: new[] { "Id", "TaskId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EduTasks_ResultRelease",
                table: "AppEducationAssessmentTasks",
                columns: new[] { "CurrentResultReleaseId", "Id" },
                principalTable: "AppEducationAssessmentResultReleases",
                principalColumns: new[] { "Id", "TaskId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EduGradeRevisions_Grade",
                table: "AppEducationAssessmentGradeRevisions",
                columns: new[] { "QuestionGradeId", "SubmissionId", "PaperQuestionId" },
                principalTable: "AppEducationAssessmentQuestionGrades",
                principalColumns: new[] { "Id", "SubmissionId", "PaperQuestionId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EduTasks_Closure",
                table: "AppEducationAssessmentTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_EduTasks_ResultRelease",
                table: "AppEducationAssessmentTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_EduGradeRevisions_Grade",
                table: "AppEducationAssessmentGradeRevisions");

            migrationBuilder.DropTable(
                name: "AppEducationAssessmentResultReleaseItems");

            migrationBuilder.DropTable(
                name: "AppEducationAssessmentScoreReviewRequests");

            migrationBuilder.DropTable(
                name: "AppEducationAssessmentResultReleases");

            migrationBuilder.DropTable(
                name: "AppEducationAssessmentClosures");

            migrationBuilder.DropTable(
                name: "AppEducationAssessmentQuestionGrades");

            migrationBuilder.DropTable(
                name: "AppEducationAssessmentGradeRevisions");

            migrationBuilder.DropIndex(
                name: "IX_AppEducationAssessmentTasks_ClosureId_Id",
                table: "AppEducationAssessmentTasks");

            migrationBuilder.DropIndex(
                name: "IX_AppEducationAssessmentTasks_CurrentResultReleaseId_Id",
                table: "AppEducationAssessmentTasks");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_EduSubmissions_AttemptStudent",
                table: "AppEducationAssessmentSubmissions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_EduSubmissions_AttemptStudentPaper",
                table: "AppEducationAssessmentSubmissions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_EduResponses_AttemptPaperQuestion",
                table: "AppEducationAssessmentResponseRevisions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_EduRecipients_TaskStudent",
                table: "AppEducationAssessmentRecipients");

            migrationBuilder.DropColumn(
                name: "ClosureId",
                table: "AppEducationAssessmentTasks");

            migrationBuilder.DropColumn(
                name: "ConfirmedGradeSetVersion",
                table: "AppEducationAssessmentTasks");

            migrationBuilder.DropColumn(
                name: "CurrentResultReleaseId",
                table: "AppEducationAssessmentTasks");
        }
    }
}
