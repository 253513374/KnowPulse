using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddIndependentCorrectionTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_EduGrades_CorrectionSource",
                table: "AppEducationAssessmentQuestionGrades",
                columns: new[] { "Id", "OwnerUserId", "StudentUserId", "TaskId", "TaskRevisionId", "SubmissionId", "PaperQuestionId" });

            migrationBuilder.CreateTable(
                name: "AppEducationCorrectionDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicationKind = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    FirstProvidedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    SubmissionNumber = table.Column<int>(type: "integer", nullable: false),
                    CurrentSubmissionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewNumber = table.Column<int>(type: "integer", nullable: false),
                    CurrentReviewId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_AppEducationCorrectionDeliveries", x => x.Id);
                    table.UniqueConstraint("AK_EduCorrectionDeliveries_Actors", x => new { x.Id, x.StudentUserId, x.OwnerUserId });
                    table.UniqueConstraint("AK_EduCorrectionDeliveries_Student", x => new { x.Id, x.StudentUserId });
                    table.CheckConstraint("CK_EduCorrectionDeliveries_State", "\"PublicationKind\" = 1 AND (\"FirstProvidedAtUtc\" IS NULL OR \"FirstProvidedAtUtc\" >= \"AssignedAtUtc\") AND ((\"SubmissionNumber\" = 0 AND \"CurrentSubmissionId\" IS NULL) OR (\"SubmissionNumber\" > 0 AND \"CurrentSubmissionId\" IS NOT NULL AND \"FirstProvidedAtUtc\" IS NOT NULL)) AND ((\"ReviewNumber\" = 0 AND \"CurrentReviewId\" IS NULL) OR (\"ReviewNumber\" > 0 AND \"CurrentReviewId\" IS NOT NULL AND \"CurrentSubmissionId\" IS NOT NULL))");
                });

            migrationBuilder.CreateTable(
                name: "AppEducationCorrectionImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeliveryId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BlobName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
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
                    table.PrimaryKey("PK_AppEducationCorrectionImages", x => x.Id);
                    table.CheckConstraint("CK_EduCorrectionImages_State", "\"Size\" BETWEEN 1 AND 5242880 AND \"ContentType\" IN ('image/png','image/jpeg','image/webp') AND length(\"ContentHash\") = 64 AND length(\"RequestFingerprint\") = 64 AND (\"UploadedAtUtc\" IS NULL OR \"UploadedAtUtc\" >= \"ReservedAtUtc\")");
                    table.ForeignKey(
                        name: "FK_EduCorrectionImages_Student",
                        columns: x => new { x.DeliveryId, x.StudentUserId },
                        principalTable: "AppEducationCorrectionDeliveries",
                        principalColumns: new[] { "Id", "StudentUserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationCorrectionSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeliveryId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionNumber = table.Column<int>(type: "integer", nullable: false),
                    BasedOnSubmissionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContentJson = table.Column<string>(type: "character varying(131072)", maxLength: 131072, nullable: false),
                    PreviewFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpectedConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationCorrectionSubmissions", x => x.Id);
                    table.UniqueConstraint("AK_EduCorrectionSubmissions_Delivery", x => new { x.Id, x.DeliveryId });
                    table.UniqueConstraint("AK_EduCorrectionSubmissions_Sequence", x => new { x.Id, x.DeliveryId, x.SubmissionNumber });
                    table.CheckConstraint("CK_EduCorrectionSubmissions_State", "\"SubmissionNumber\" > 0 AND length(\"PreviewFingerprint\") = 64 AND length(\"RequestFingerprint\") = 64 AND ((\"SubmissionNumber\" = 1 AND \"BasedOnSubmissionId\" IS NULL) OR (\"SubmissionNumber\" > 1 AND \"BasedOnSubmissionId\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_EduCorrectionSubmissions_Delivery",
                        columns: x => new { x.DeliveryId, x.StudentUserId, x.OwnerUserId },
                        principalTable: "AppEducationCorrectionDeliveries",
                        principalColumns: new[] { "Id", "StudentUserId", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduCorrectionSubmissions_Previous",
                        columns: x => new { x.BasedOnSubmissionId, x.DeliveryId },
                        principalTable: "AppEducationCorrectionSubmissions",
                        principalColumns: new[] { "Id", "DeliveryId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationCorrectionReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeliveryId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewNumber = table.Column<int>(type: "integer", nullable: false),
                    BasedOnReviewId = table.Column<Guid>(type: "uuid", nullable: true),
                    Decision = table.Column<int>(type: "integer", nullable: false),
                    StudentFeedback = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpectedConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationCorrectionReviews", x => x.Id);
                    table.UniqueConstraint("AK_EduCorrectionReviews_Delivery", x => new { x.Id, x.DeliveryId });
                    table.UniqueConstraint("AK_EduCorrectionReviews_Sequence", x => new { x.Id, x.DeliveryId, x.ReviewNumber });
                    table.CheckConstraint("CK_EduCorrectionReviews_State", "\"ReviewNumber\" > 0 AND \"Decision\" IN (0,1) AND length(trim(\"StudentFeedback\")) BETWEEN 1 AND 2000 AND length(\"RequestFingerprint\") = 64 AND ((\"ReviewNumber\" = 1 AND \"BasedOnReviewId\" IS NULL) OR (\"ReviewNumber\" > 1 AND \"BasedOnReviewId\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_EduCorrectionReviews_Delivery",
                        columns: x => new { x.DeliveryId, x.StudentUserId, x.OwnerUserId },
                        principalTable: "AppEducationCorrectionDeliveries",
                        principalColumns: new[] { "Id", "StudentUserId", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduCorrectionReviews_Previous",
                        columns: x => new { x.BasedOnReviewId, x.DeliveryId },
                        principalTable: "AppEducationCorrectionReviews",
                        principalColumns: new[] { "Id", "DeliveryId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduCorrectionReviews_Submission",
                        columns: x => new { x.SubmissionId, x.DeliveryId },
                        principalTable: "AppEducationCorrectionSubmissions",
                        principalColumns: new[] { "Id", "DeliveryId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationCorrectionTaskRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    BasedOnRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    QuestionGradeId = table.Column<Guid>(type: "uuid", nullable: false),
                    GradeRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResultReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentJson = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: true),
                    PublicReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    StudentName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ClassName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SubjectName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TeacherName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpectedConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationCorrectionTaskRevisions", x => x.Id);
                    table.UniqueConstraint("AK_EduCorrectionVersions_Delivery", x => new { x.Id, x.TaskId, x.OwnerUserId, x.StudentUserId, x.Kind });
                    table.UniqueConstraint("AK_EduCorrectionVersions_Kind", x => new { x.Id, x.TaskId, x.Kind });
                    table.UniqueConstraint("AK_EduCorrectionVersions_Root", x => new { x.Id, x.TaskId });
                    table.UniqueConstraint("AK_EduCorrectionVersions_Sequence", x => new { x.Id, x.TaskId, x.RevisionNumber });
                    table.CheckConstraint("CK_EduCorrectionVersions_State", "\"RevisionNumber\" > 0 AND length(\"RequestFingerprint\") = 64 AND ((\"Kind\" = 0 AND \"SourceRevisionId\" IS NULL AND \"ContentJson\" IS NOT NULL) OR (\"Kind\" = 1 AND \"SourceRevisionId\" IS NOT NULL AND \"ContentJson\" IS NOT NULL) OR (\"Kind\" = 2 AND \"SourceRevisionId\" IS NOT NULL AND \"ContentJson\" IS NULL AND \"PublicReason\" IS NOT NULL AND length(trim(\"PublicReason\")) > 0))");
                    table.ForeignKey(
                        name: "FK_EduCorrectionVersions_BasedOnRevisionId",
                        columns: x => new { x.BasedOnRevisionId, x.TaskId },
                        principalTable: "AppEducationCorrectionTaskRevisions",
                        principalColumns: new[] { "Id", "TaskId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduCorrectionVersions_Grade",
                        columns: x => new { x.GradeRevisionId, x.QuestionGradeId },
                        principalTable: "AppEducationAssessmentGradeRevisions",
                        principalColumns: new[] { "Id", "QuestionGradeId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduCorrectionVersions_Release",
                        column: x => x.ResultReleaseId,
                        principalTable: "AppEducationAssessmentResultReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduCorrectionVersions_SourceRevisionId",
                        columns: x => new { x.SourceRevisionId, x.TaskId },
                        principalTable: "AppEducationCorrectionTaskRevisions",
                        principalColumns: new[] { "Id", "TaskId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationCorrectionTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionGradeId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceTaskRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssessmentRecipientId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaperQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademicTermId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolClassId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeachingAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    EducationSubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    CurrentRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PublishedRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastPublishedRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PublicationKind = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
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
                    table.PrimaryKey("PK_AppEducationCorrectionTasks", x => x.Id);
                    table.UniqueConstraint("AK_EduCorrections_Source", x => new { x.Id, x.OwnerUserId, x.StudentUserId, x.QuestionGradeId });
                    table.CheckConstraint("CK_EduCorrections_State", "\"PublicationKind\" = 1 AND ((\"RevisionNumber\" = 0 AND \"CurrentRevisionId\" IS NULL) OR (\"RevisionNumber\" > 0 AND \"CurrentRevisionId\" IS NOT NULL)) AND (\"PublishedRevisionId\" IS NULL OR (\"LastPublishedRevisionId\" IS NOT NULL AND \"PublishedRevisionId\" = \"LastPublishedRevisionId\"))");
                    table.ForeignKey(
                        name: "FK_EduCorrections_Current",
                        columns: x => new { x.CurrentRevisionId, x.Id, x.RevisionNumber },
                        principalTable: "AppEducationCorrectionTaskRevisions",
                        principalColumns: new[] { "Id", "TaskId", "RevisionNumber" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduCorrections_Enrollment",
                        columns: x => new { x.StudentEnrollmentId, x.SchoolId, x.SchoolClassId, x.StudentUserId },
                        principalTable: "AppStudentEnrollments",
                        principalColumns: new[] { "Id", "SchoolId", "SchoolClassId", "StudentUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduCorrections_Grade",
                        columns: x => new { x.QuestionGradeId, x.OwnerUserId, x.StudentUserId, x.SourceTaskId, x.SourceTaskRevisionId, x.SubmissionId, x.PaperQuestionId },
                        principalTable: "AppEducationAssessmentQuestionGrades",
                        principalColumns: new[] { "Id", "OwnerUserId", "StudentUserId", "TaskId", "TaskRevisionId", "SubmissionId", "PaperQuestionId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduCorrections_LastPublishedRevisionId",
                        columns: x => new { x.LastPublishedRevisionId, x.Id, x.PublicationKind },
                        principalTable: "AppEducationCorrectionTaskRevisions",
                        principalColumns: new[] { "Id", "TaskId", "Kind" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduCorrections_Member",
                        columns: x => new { x.SchoolMembershipId, x.SchoolId, x.StudentUserId },
                        principalTable: "AppSchoolMemberships",
                        principalColumns: new[] { "Id", "SchoolId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduCorrections_PublishedRevisionId",
                        columns: x => new { x.PublishedRevisionId, x.Id, x.PublicationKind },
                        principalTable: "AppEducationCorrectionTaskRevisions",
                        principalColumns: new[] { "Id", "TaskId", "Kind" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduCorrections_Recipient",
                        columns: x => new { x.AssessmentRecipientId, x.SourceTaskId, x.SourceTaskRevisionId, x.StudentUserId },
                        principalTable: "AppEducationAssessmentRecipients",
                        principalColumns: new[] { "Id", "TaskId", "TaskRevisionId", "StudentUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduCorrections_Teaching",
                        columns: x => new { x.TeachingAssignmentId, x.SchoolId, x.AcademicTermId, x.SchoolClassId, x.OwnerUserId, x.EducationSubjectId },
                        principalTable: "AppTeachingAssignments",
                        principalColumns: new[] { "Id", "SchoolId", "AcademicTermId", "SchoolClassId", "TeacherUserId", "EducationSubjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationCorrectionDeliveries_CurrentReviewId_Id_ReviewN~",
                table: "AppEducationCorrectionDeliveries",
                columns: new[] { "CurrentReviewId", "Id", "ReviewNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationCorrectionDeliveries_CurrentSubmissionId_Id_Sub~",
                table: "AppEducationCorrectionDeliveries",
                columns: new[] { "CurrentSubmissionId", "Id", "SubmissionNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationCorrectionDeliveries_PublicationId_TaskId_Owner~",
                table: "AppEducationCorrectionDeliveries",
                columns: new[] { "PublicationId", "TaskId", "OwnerUserId", "StudentUserId", "PublicationKind" });

            migrationBuilder.CreateIndex(
                name: "IX_EduCorrectionDeliveries_Publication",
                table: "AppEducationCorrectionDeliveries",
                columns: new[] { "PublicationId", "StudentUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EduCorrectionDeliveries_Student",
                table: "AppEducationCorrectionDeliveries",
                columns: new[] { "TenantId", "StudentUserId", "AssignedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationCorrectionImages_DeliveryId_StudentUserId",
                table: "AppEducationCorrectionImages",
                columns: new[] { "DeliveryId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduCorrectionImages_Blob",
                table: "AppEducationCorrectionImages",
                column: "BlobName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EduCorrectionImages_Delivery",
                table: "AppEducationCorrectionImages",
                column: "DeliveryId");

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationCorrectionReviews_BasedOnReviewId_DeliveryId",
                table: "AppEducationCorrectionReviews",
                columns: new[] { "BasedOnReviewId", "DeliveryId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationCorrectionReviews_DeliveryId_StudentUserId_Owne~",
                table: "AppEducationCorrectionReviews",
                columns: new[] { "DeliveryId", "StudentUserId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationCorrectionReviews_SubmissionId_DeliveryId",
                table: "AppEducationCorrectionReviews",
                columns: new[] { "SubmissionId", "DeliveryId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduCorrectionReviews_Sequence",
                table: "AppEducationCorrectionReviews",
                columns: new[] { "DeliveryId", "ReviewNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationCorrectionSubmissions_BasedOnSubmissionId_Deliv~",
                table: "AppEducationCorrectionSubmissions",
                columns: new[] { "BasedOnSubmissionId", "DeliveryId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationCorrectionSubmissions_DeliveryId_StudentUserId_~",
                table: "AppEducationCorrectionSubmissions",
                columns: new[] { "DeliveryId", "StudentUserId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduCorrectionSubmissions_Sequence",
                table: "AppEducationCorrectionSubmissions",
                columns: new[] { "DeliveryId", "SubmissionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationCorrectionTaskRevisions_BasedOnRevisionId_TaskId",
                table: "AppEducationCorrectionTaskRevisions",
                columns: new[] { "BasedOnRevisionId", "TaskId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationCorrectionTaskRevisions_GradeRevisionId_Questio~",
                table: "AppEducationCorrectionTaskRevisions",
                columns: new[] { "GradeRevisionId", "QuestionGradeId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationCorrectionTaskRevisions_ResultReleaseId",
                table: "AppEducationCorrectionTaskRevisions",
                column: "ResultReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationCorrectionTaskRevisions_SourceRevisionId_TaskId",
                table: "AppEducationCorrectionTaskRevisions",
                columns: new[] { "SourceRevisionId", "TaskId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationCorrectionTaskRevisions_TaskId_OwnerUserId_Stud~",
                table: "AppEducationCorrectionTaskRevisions",
                columns: new[] { "TaskId", "OwnerUserId", "StudentUserId", "QuestionGradeId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduCorrectionVersions_Sequence",
                table: "AppEducationCorrectionTaskRevisions",
                columns: new[] { "TaskId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationCorrectionTasks_AssessmentRecipientId_SourceTas~",
                table: "AppEducationCorrectionTasks",
                columns: new[] { "AssessmentRecipientId", "SourceTaskId", "SourceTaskRevisionId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationCorrectionTasks_CurrentRevisionId_Id_RevisionNu~",
                table: "AppEducationCorrectionTasks",
                columns: new[] { "CurrentRevisionId", "Id", "RevisionNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationCorrectionTasks_LastPublishedRevisionId_Id_Publ~",
                table: "AppEducationCorrectionTasks",
                columns: new[] { "LastPublishedRevisionId", "Id", "PublicationKind" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationCorrectionTasks_PublishedRevisionId_Id_Publicat~",
                table: "AppEducationCorrectionTasks",
                columns: new[] { "PublishedRevisionId", "Id", "PublicationKind" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationCorrectionTasks_QuestionGradeId_OwnerUserId_Stu~",
                table: "AppEducationCorrectionTasks",
                columns: new[] { "QuestionGradeId", "OwnerUserId", "StudentUserId", "SourceTaskId", "SourceTaskRevisionId", "SubmissionId", "PaperQuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationCorrectionTasks_SchoolMembershipId_SchoolId_Stu~",
                table: "AppEducationCorrectionTasks",
                columns: new[] { "SchoolMembershipId", "SchoolId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationCorrectionTasks_StudentEnrollmentId_SchoolId_Sc~",
                table: "AppEducationCorrectionTasks",
                columns: new[] { "StudentEnrollmentId", "SchoolId", "SchoolClassId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationCorrectionTasks_TeachingAssignmentId_SchoolId_A~",
                table: "AppEducationCorrectionTasks",
                columns: new[] { "TeachingAssignmentId", "SchoolId", "AcademicTermId", "SchoolClassId", "OwnerUserId", "EducationSubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduCorrections_Owner",
                table: "AppEducationCorrectionTasks",
                columns: new[] { "TenantId", "OwnerUserId", "CreationTime" });

            migrationBuilder.AddForeignKey(
                name: "FK_EduCorrectionDeliveries_Publication",
                table: "AppEducationCorrectionDeliveries",
                columns: new[] { "PublicationId", "TaskId", "OwnerUserId", "StudentUserId", "PublicationKind" },
                principalTable: "AppEducationCorrectionTaskRevisions",
                principalColumns: new[] { "Id", "TaskId", "OwnerUserId", "StudentUserId", "Kind" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EduCorrectionDeliveries_Review",
                table: "AppEducationCorrectionDeliveries",
                columns: new[] { "CurrentReviewId", "Id", "ReviewNumber" },
                principalTable: "AppEducationCorrectionReviews",
                principalColumns: new[] { "Id", "DeliveryId", "ReviewNumber" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EduCorrectionDeliveries_Submission",
                table: "AppEducationCorrectionDeliveries",
                columns: new[] { "CurrentSubmissionId", "Id", "SubmissionNumber" },
                principalTable: "AppEducationCorrectionSubmissions",
                principalColumns: new[] { "Id", "DeliveryId", "SubmissionNumber" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EduCorrectionVersions_Root",
                table: "AppEducationCorrectionTaskRevisions",
                columns: new[] { "TaskId", "OwnerUserId", "StudentUserId", "QuestionGradeId" },
                principalTable: "AppEducationCorrectionTasks",
                principalColumns: new[] { "Id", "OwnerUserId", "StudentUserId", "QuestionGradeId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "AppEducationCorrectionTasks") OR EXISTS (SELECT 1 FROM "AppEducationCorrectionTaskRevisions")
                        OR EXISTS (SELECT 1 FROM "AppEducationCorrectionDeliveries") OR EXISTS (SELECT 1 FROM "AppEducationCorrectionSubmissions")
                        OR EXISTS (SELECT 1 FROM "AppEducationCorrectionReviews") OR EXISTS (SELECT 1 FROM "AppEducationCorrectionImages") THEN
                        RAISE EXCEPTION 'Correction task or submission history exists; downgrade requires an explicit compatible restore.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_EduCorrectionDeliveries_Publication",
                table: "AppEducationCorrectionDeliveries");

            migrationBuilder.DropForeignKey(
                name: "FK_EduCorrections_Current",
                table: "AppEducationCorrectionTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_EduCorrections_LastPublishedRevisionId",
                table: "AppEducationCorrectionTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_EduCorrections_PublishedRevisionId",
                table: "AppEducationCorrectionTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_EduCorrectionDeliveries_Review",
                table: "AppEducationCorrectionDeliveries");

            migrationBuilder.DropForeignKey(
                name: "FK_EduCorrectionDeliveries_Submission",
                table: "AppEducationCorrectionDeliveries");

            migrationBuilder.DropTable(
                name: "AppEducationCorrectionImages");

            migrationBuilder.DropTable(
                name: "AppEducationCorrectionTaskRevisions");

            migrationBuilder.DropTable(
                name: "AppEducationCorrectionTasks");

            migrationBuilder.DropTable(
                name: "AppEducationCorrectionReviews");

            migrationBuilder.DropTable(
                name: "AppEducationCorrectionSubmissions");

            migrationBuilder.DropTable(
                name: "AppEducationCorrectionDeliveries");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_EduGrades_CorrectionSource",
                table: "AppEducationAssessmentQuestionGrades");
        }
    }
}
