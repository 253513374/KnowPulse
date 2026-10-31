using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddEducationEvidenceDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_EduFeedbackVersions_StudentOwner",
                table: "AppEducationStudentFeedbackRevisions",
                columns: new[] { "Id", "StudentUserId", "OwnerUserId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_EduExposures_Student",
                table: "AppEducationQuestionExposures",
                columns: new[] { "Id", "StudentUserId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_EduGrades_EvidenceOwner",
                table: "AppEducationAssessmentQuestionGrades",
                columns: new[] { "Id", "OwnerUserId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_EduGrades_EvidenceScope",
                table: "AppEducationAssessmentQuestionGrades",
                columns: new[] { "Id", "TaskId", "SubmissionId", "AttemptId", "StudentUserId", "PaperQuestionId", "OwnerUserId" });

            migrationBuilder.CreateTable(
                name: "AppEducationEvidenceQualificationRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionGradeId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionDefinitionRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    BasedOnRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Use = table.Column<int>(type: "integer", nullable: false),
                    NoHintsOrAnswersConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    NewRequirementsConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    OriginalRelationshipsChecked = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ExpectedConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationEvidenceQualificationRevisions", x => x.Id);
                    table.UniqueConstraint("AK_EduQualificationVersions_Root", x => new { x.Id, x.QuestionGradeId });
                    table.CheckConstraint("CK_EduQualificationVersions_Revision", "\"RevisionNumber\" > 0 AND length(trim(\"Reason\")) > 0 AND length(\"Reason\") <= 2048");
                    table.CheckConstraint("CK_EduQualificationVersions_Use", "\"Use\" IN (0, 1, 2, 3) AND (\"Use\" <> 1 OR (\"NoHintsOrAnswersConfirmed\" AND \"NewRequirementsConfirmed\" AND \"OriginalRelationshipsChecked\"))");
                    table.ForeignKey(
                        name: "FK_EduQualificationVersions_Previous",
                        columns: x => new { x.BasedOnRevisionId, x.QuestionGradeId },
                        principalTable: "AppEducationEvidenceQualificationRevisions",
                        principalColumns: new[] { "Id", "QuestionGradeId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationEvidenceQualifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaperQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionDefinitionRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    CurrentRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_AppEducationEvidenceQualifications", x => x.Id);
                    table.UniqueConstraint("AK_EduQualifications_Scope", x => new { x.Id, x.StudentUserId, x.OwnerUserId, x.QuestionDefinitionRevisionId });
                    table.CheckConstraint("CK_EduQualifications_Revision", "(\"RevisionNumber\" = 0 AND \"CurrentRevisionId\" IS NULL) OR (\"RevisionNumber\" > 0 AND \"CurrentRevisionId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_EduQualifications_Current",
                        columns: x => new { x.CurrentRevisionId, x.Id },
                        principalTable: "AppEducationEvidenceQualificationRevisions",
                        principalColumns: new[] { "Id", "QuestionGradeId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduQualifications_Definition",
                        columns: x => new { x.QuestionDefinitionRevisionId, x.OriginalQuestionId },
                        principalTable: "AppQuestionDefinitionRevisions",
                        principalColumns: new[] { "Id", "QuestionId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduQualifications_Grade",
                        columns: x => new { x.Id, x.TaskId, x.SubmissionId, x.AttemptId, x.StudentUserId, x.PaperQuestionId, x.OwnerUserId },
                        principalTable: "AppEducationAssessmentQuestionGrades",
                        principalColumns: new[] { "Id", "TaskId", "SubmissionId", "AttemptId", "StudentUserId", "PaperQuestionId", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationExposureHintReviewRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExposureId = table.Column<Guid>(type: "uuid", nullable: false),
                    FeedbackRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    BasedOnRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Use = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ExpectedConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationExposureHintReviewRevisions", x => x.Id);
                    table.UniqueConstraint("AK_EduHintReviewVersions_Root", x => new { x.Id, x.ExposureId });
                    table.CheckConstraint("CK_EduHintReviewVersions_Revision", "\"RevisionNumber\" > 0 AND length(trim(\"Reason\")) > 0 AND length(\"Reason\") <= 2048");
                    table.CheckConstraint("CK_EduHintReviewVersions_Use", "\"Use\" IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_EduHintReviewVersions_Previous",
                        columns: x => new { x.BasedOnRevisionId, x.ExposureId },
                        principalTable: "AppEducationExposureHintReviewRevisions",
                        principalColumns: new[] { "Id", "ExposureId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationExposureHintReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FeedbackRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    CurrentRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_AppEducationExposureHintReviews", x => x.Id);
                    table.UniqueConstraint("AK_EduHintReviews_Scope", x => new { x.Id, x.StudentUserId, x.OwnerUserId, x.FeedbackRevisionId });
                    table.CheckConstraint("CK_EduHintReviews_Revision", "(\"RevisionNumber\" = 0 AND \"CurrentRevisionId\" IS NULL) OR (\"RevisionNumber\" > 0 AND \"CurrentRevisionId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_EduHintReviews_Current",
                        columns: x => new { x.CurrentRevisionId, x.Id },
                        principalTable: "AppEducationExposureHintReviewRevisions",
                        principalColumns: new[] { "Id", "ExposureId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduHintReviews_Exposure",
                        columns: x => new { x.Id, x.StudentUserId },
                        principalTable: "AppEducationQuestionExposures",
                        principalColumns: new[] { "Id", "StudentUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduHintReviews_Feedback",
                        columns: x => new { x.FeedbackRevisionId, x.StudentUserId, x.OwnerUserId },
                        principalTable: "AppEducationStudentFeedbackRevisions",
                        principalColumns: new[] { "Id", "StudentUserId", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationQuestionOriginRelationRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstOriginalQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SecondOriginalQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstQuestionGradeId = table.Column<Guid>(type: "uuid", nullable: false),
                    SecondQuestionGradeId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstDefinitionRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SecondDefinitionRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    BasedOnRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ExpectedConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationQuestionOriginRelationRevisions", x => x.Id);
                    table.UniqueConstraint("AK_EduOriginVersions_Root", x => new { x.Id, x.RelationId });
                    table.CheckConstraint("CK_EduOriginVersions_Revision", "\"RevisionNumber\" > 0 AND length(trim(\"Reason\")) > 0 AND length(\"Reason\") <= 2048");
                    table.ForeignKey(
                        name: "FK_EduOriginVersions_FirstDefinition",
                        columns: x => new { x.FirstDefinitionRevisionId, x.FirstOriginalQuestionId },
                        principalTable: "AppQuestionDefinitionRevisions",
                        principalColumns: new[] { "Id", "QuestionId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduOriginVersions_FirstGrade",
                        columns: x => new { x.FirstQuestionGradeId, x.OwnerUserId },
                        principalTable: "AppEducationAssessmentQuestionGrades",
                        principalColumns: new[] { "Id", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduOriginVersions_Previous",
                        columns: x => new { x.BasedOnRevisionId, x.RelationId },
                        principalTable: "AppEducationQuestionOriginRelationRevisions",
                        principalColumns: new[] { "Id", "RelationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduOriginVersions_SecondDefinition",
                        columns: x => new { x.SecondDefinitionRevisionId, x.SecondOriginalQuestionId },
                        principalTable: "AppQuestionDefinitionRevisions",
                        principalColumns: new[] { "Id", "QuestionId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduOriginVersions_SecondGrade",
                        columns: x => new { x.SecondQuestionGradeId, x.OwnerUserId },
                        principalTable: "AppEducationAssessmentQuestionGrades",
                        principalColumns: new[] { "Id", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationQuestionOriginRelations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    EducationSubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstOriginalQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SecondOriginalQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    CurrentRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_AppEducationQuestionOriginRelations", x => x.Id);
                    table.UniqueConstraint("AK_EduOriginRelations_Pair", x => new { x.Id, x.FirstOriginalQuestionId, x.SecondOriginalQuestionId, x.OwnerUserId });
                    table.CheckConstraint("CK_EduOriginRelations_Pair", "\"FirstOriginalQuestionId\" < \"SecondOriginalQuestionId\"");
                    table.CheckConstraint("CK_EduOriginRelations_Revision", "(\"RevisionNumber\" = 0 AND \"CurrentRevisionId\" IS NULL) OR (\"RevisionNumber\" > 0 AND \"CurrentRevisionId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_EduOriginRelations_Current",
                        columns: x => new { x.CurrentRevisionId, x.Id },
                        principalTable: "AppEducationQuestionOriginRelationRevisions",
                        principalColumns: new[] { "Id", "RelationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduOriginRelations_School",
                        column: x => x.SchoolId,
                        principalTable: "AppSchools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduOriginRelations_Subject",
                        column: x => x.EducationSubjectId,
                        principalTable: "AppEducationSubjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationEvidenceQualificationRevisions_BasedOnRevisionI~",
                table: "AppEducationEvidenceQualificationRevisions",
                columns: new[] { "BasedOnRevisionId", "QuestionGradeId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationEvidenceQualificationRevisions_QuestionGradeId_~",
                table: "AppEducationEvidenceQualificationRevisions",
                columns: new[] { "QuestionGradeId", "StudentUserId", "OwnerUserId", "QuestionDefinitionRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduQualificationVersions_Sequence",
                table: "AppEducationEvidenceQualificationRevisions",
                columns: new[] { "QuestionGradeId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationEvidenceQualifications_CurrentRevisionId_Id",
                table: "AppEducationEvidenceQualifications",
                columns: new[] { "CurrentRevisionId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationEvidenceQualifications_Id_TaskId_SubmissionId_A~",
                table: "AppEducationEvidenceQualifications",
                columns: new[] { "Id", "TaskId", "SubmissionId", "AttemptId", "StudentUserId", "PaperQuestionId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationEvidenceQualifications_QuestionDefinitionRevisi~",
                table: "AppEducationEvidenceQualifications",
                columns: new[] { "QuestionDefinitionRevisionId", "OriginalQuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationExposureHintReviewRevisions_BasedOnRevisionId_E~",
                table: "AppEducationExposureHintReviewRevisions",
                columns: new[] { "BasedOnRevisionId", "ExposureId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationExposureHintReviewRevisions_ExposureId_StudentU~",
                table: "AppEducationExposureHintReviewRevisions",
                columns: new[] { "ExposureId", "StudentUserId", "OwnerUserId", "FeedbackRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduHintReviewVersions_Sequence",
                table: "AppEducationExposureHintReviewRevisions",
                columns: new[] { "ExposureId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationExposureHintReviews_CurrentRevisionId_Id",
                table: "AppEducationExposureHintReviews",
                columns: new[] { "CurrentRevisionId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationExposureHintReviews_FeedbackRevisionId_StudentU~",
                table: "AppEducationExposureHintReviews",
                columns: new[] { "FeedbackRevisionId", "StudentUserId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationQuestionOriginRelationRevisions_BasedOnRevision~",
                table: "AppEducationQuestionOriginRelationRevisions",
                columns: new[] { "BasedOnRevisionId", "RelationId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationQuestionOriginRelationRevisions_FirstDefinition~",
                table: "AppEducationQuestionOriginRelationRevisions",
                columns: new[] { "FirstDefinitionRevisionId", "FirstOriginalQuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationQuestionOriginRelationRevisions_FirstQuestionGr~",
                table: "AppEducationQuestionOriginRelationRevisions",
                columns: new[] { "FirstQuestionGradeId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationQuestionOriginRelationRevisions_RelationId_Firs~",
                table: "AppEducationQuestionOriginRelationRevisions",
                columns: new[] { "RelationId", "FirstOriginalQuestionId", "SecondOriginalQuestionId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationQuestionOriginRelationRevisions_SecondDefinitio~",
                table: "AppEducationQuestionOriginRelationRevisions",
                columns: new[] { "SecondDefinitionRevisionId", "SecondOriginalQuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationQuestionOriginRelationRevisions_SecondQuestionG~",
                table: "AppEducationQuestionOriginRelationRevisions",
                columns: new[] { "SecondQuestionGradeId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduOriginVersions_Sequence",
                table: "AppEducationQuestionOriginRelationRevisions",
                columns: new[] { "RelationId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationQuestionOriginRelations_CurrentRevisionId_Id",
                table: "AppEducationQuestionOriginRelations",
                columns: new[] { "CurrentRevisionId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationQuestionOriginRelations_EducationSubjectId",
                table: "AppEducationQuestionOriginRelations",
                column: "EducationSubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationQuestionOriginRelations_SchoolId",
                table: "AppEducationQuestionOriginRelations",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_EduOriginRelations_OnePerPair",
                table: "AppEducationQuestionOriginRelations",
                columns: new[] { "OwnerUserId", "SchoolId", "EducationSubjectId", "FirstOriginalQuestionId", "SecondOriginalQuestionId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_EduQualificationVersions_Root",
                table: "AppEducationEvidenceQualificationRevisions",
                columns: new[] { "QuestionGradeId", "StudentUserId", "OwnerUserId", "QuestionDefinitionRevisionId" },
                principalTable: "AppEducationEvidenceQualifications",
                principalColumns: new[] { "Id", "StudentUserId", "OwnerUserId", "QuestionDefinitionRevisionId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EduHintReviewVersions_Root",
                table: "AppEducationExposureHintReviewRevisions",
                columns: new[] { "ExposureId", "StudentUserId", "OwnerUserId", "FeedbackRevisionId" },
                principalTable: "AppEducationExposureHintReviews",
                principalColumns: new[] { "Id", "StudentUserId", "OwnerUserId", "FeedbackRevisionId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EduOriginVersions_Root",
                table: "AppEducationQuestionOriginRelationRevisions",
                columns: new[] { "RelationId", "FirstOriginalQuestionId", "SecondOriginalQuestionId", "OwnerUserId" },
                principalTable: "AppEducationQuestionOriginRelations",
                principalColumns: new[] { "Id", "FirstOriginalQuestionId", "SecondOriginalQuestionId", "OwnerUserId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EduQualificationVersions_Root",
                table: "AppEducationEvidenceQualificationRevisions");

            migrationBuilder.DropForeignKey(
                name: "FK_EduHintReviewVersions_Root",
                table: "AppEducationExposureHintReviewRevisions");

            migrationBuilder.DropForeignKey(
                name: "FK_EduOriginVersions_Root",
                table: "AppEducationQuestionOriginRelationRevisions");

            migrationBuilder.DropTable(
                name: "AppEducationEvidenceQualifications");

            migrationBuilder.DropTable(
                name: "AppEducationEvidenceQualificationRevisions");

            migrationBuilder.DropTable(
                name: "AppEducationExposureHintReviews");

            migrationBuilder.DropTable(
                name: "AppEducationExposureHintReviewRevisions");

            migrationBuilder.DropTable(
                name: "AppEducationQuestionOriginRelations");

            migrationBuilder.DropTable(
                name: "AppEducationQuestionOriginRelationRevisions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_EduFeedbackVersions_StudentOwner",
                table: "AppEducationStudentFeedbackRevisions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_EduExposures_Student",
                table: "AppEducationQuestionExposures");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_EduGrades_EvidenceOwner",
                table: "AppEducationAssessmentQuestionGrades");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_EduGrades_EvidenceScope",
                table: "AppEducationAssessmentQuestionGrades");
        }
    }
}
