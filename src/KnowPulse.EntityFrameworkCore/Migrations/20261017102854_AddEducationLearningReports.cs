using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddEducationLearningReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_EduBooks_ReportScope",
                table: "AppTextbookVersions",
                columns: new[] { "Id", "SchoolId", "EducationSubjectId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_EduTeaching_ReportScope",
                table: "AppTeachingAssignments",
                columns: new[] { "Id", "SchoolId", "AcademicTermId", "SchoolClassId", "TeacherUserId", "EducationSubjectId" });

            migrationBuilder.CreateTable(
                name: "AppEducationLearningReportJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousJobId = table.Column<Guid>(type: "uuid", nullable: true),
                    RetryOfJobId = table.Column<Guid>(type: "uuid", nullable: true),
                    Generation = table.Column<int>(type: "integer", nullable: false),
                    SourceJson = table.Column<string>(type: "text", nullable: false),
                    CurriculumJson = table.Column<string>(type: "text", nullable: false),
                    SourceFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpectedConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    CompletedStudentCount = table.Column<int>(type: "integer", nullable: false),
                    StudentCount = table.Column<int>(type: "integer", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: true),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    HeartbeatAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    NextSourceCheckAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    FailureStage = table.Column<int>(type: "integer", nullable: false),
                    CapacityExceeded = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_AppEducationLearningReportJobs", x => x.Id);
                    table.UniqueConstraint("AK_EduReportJobs_Generation", x => new { x.Id, x.ReportId, x.OwnerUserId, x.Generation });
                    table.UniqueConstraint("AK_EduReportJobs_Owner", x => new { x.Id, x.ReportId, x.OwnerUserId });
                    table.UniqueConstraint("AK_EduReportJobs_Report", x => new { x.Id, x.ReportId });
                    table.CheckConstraint("CK_EduReportJobs_CurriculumSize", "length(\"CurriculumJson\") BETWEEN 1 AND 33554432");
                    table.CheckConstraint("CK_EduReportJobs_SourceSize", "length(\"SourceJson\") BETWEEN 1 AND 33554432");
                    table.CheckConstraint("CK_EduReportJobs_State", "\"Generation\" > 0 AND \"AttemptCount\" >= 0 AND \"StudentCount\" BETWEEN 0 AND 3000 AND \"CompletedStudentCount\" BETWEEN 0 AND \"StudentCount\" AND ((\"Status\" = 0 AND \"RunId\" IS NULL AND \"SnapshotId\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"AttemptCount\" = 0) OR (\"Status\" = 1 AND \"RunId\" IS NOT NULL AND \"LeaseExpiresAtUtc\" IS NOT NULL AND \"HeartbeatAtUtc\" IS NOT NULL AND \"SnapshotId\" IS NULL AND \"AttemptCount\" > 0) OR (\"Status\" = 2 AND \"SnapshotId\" IS NOT NULL AND \"CompletedAtUtc\" IS NOT NULL AND \"LeaseExpiresAtUtc\" IS NULL) OR (\"Status\" = 3 AND \"FailureStage\" BETWEEN 1 AND 3 AND \"LeaseExpiresAtUtc\" IS NULL AND \"SnapshotId\" IS NULL AND \"AttemptCount\" > 0) OR (\"Status\" IN (4, 5) AND \"RunId\" IS NOT NULL AND \"CompletedAtUtc\" IS NOT NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"SnapshotId\" IS NULL))");
                    table.ForeignKey(
                        name: "FK_EduReportJobs_PreviousJobId",
                        columns: x => new { x.PreviousJobId, x.ReportId },
                        principalTable: "AppEducationLearningReportJobs",
                        principalColumns: new[] { "Id", "ReportId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduReportJobs_RetryOfJobId",
                        columns: x => new { x.RetryOfJobId, x.ReportId },
                        principalTable: "AppEducationLearningReportJobs",
                        principalColumns: new[] { "Id", "ReportId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationLearningReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeachingAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademicTermId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolClassId = table.Column<Guid>(type: "uuid", nullable: false),
                    EducationSubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TextbookVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    WindowStartsAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    WindowEndsAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    SelectedTaskIdsJson = table.Column<string>(type: "character varying(32000)", maxLength: 32000, nullable: false),
                    CreateRequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Generation = table.Column<int>(type: "integer", nullable: false),
                    CurrentJobId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentSnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_AppEducationLearningReports", x => x.Id);
                    table.UniqueConstraint("AK_EduReports_Class", x => new { x.Id, x.OwnerUserId, x.SchoolId, x.SchoolClassId });
                    table.UniqueConstraint("AK_EduReports_Owner", x => new { x.Id, x.OwnerUserId });
                    table.CheckConstraint("CK_EduReports_State", "\"WindowStartsAtUtc\" < \"WindowEndsAtUtc\" AND ((\"Generation\" = 0 AND \"CurrentJobId\" IS NULL) OR (\"Generation\" > 0 AND \"CurrentJobId\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_EduReports_Book",
                        columns: x => new { x.TextbookVersionId, x.SchoolId, x.EducationSubjectId },
                        principalTable: "AppTextbookVersions",
                        principalColumns: new[] { "Id", "SchoolId", "EducationSubjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduReports_CurrentJob",
                        columns: x => new { x.CurrentJobId, x.Id, x.OwnerUserId },
                        principalTable: "AppEducationLearningReportJobs",
                        principalColumns: new[] { "Id", "ReportId", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduReports_Teaching",
                        columns: x => new { x.TeachingAssignmentId, x.SchoolId, x.AcademicTermId, x.SchoolClassId, x.OwnerUserId, x.EducationSubjectId },
                        principalTable: "AppTeachingAssignments",
                        principalColumns: new[] { "Id", "SchoolId", "AcademicTermId", "SchoolClassId", "TeacherUserId", "EducationSubjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationLearningReportSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolClassId = table.Column<Guid>(type: "uuid", nullable: false),
                    Generation = table.Column<int>(type: "integer", nullable: false),
                    SourceFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SummaryJson = table.Column<string>(type: "text", nullable: false),
                    CalculatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationLearningReportSnapshots", x => x.Id);
                    table.UniqueConstraint("AK_EduReportSnapshots_Class", x => new { x.Id, x.ReportId, x.OwnerUserId, x.SchoolId, x.SchoolClassId });
                    table.UniqueConstraint("AK_EduReportSnapshots_Job", x => new { x.Id, x.JobId, x.ReportId });
                    table.CheckConstraint("CK_EduReportSnapshots_Identity", "\"Id\" = \"JobId\" AND \"Generation\" > 0");
                    table.CheckConstraint("CK_EduReportSnapshots_SummarySize", "length(\"SummaryJson\") BETWEEN 1 AND 33554432");
                    table.ForeignKey(
                        name: "FK_EduReportSnapshots_Job",
                        columns: x => new { x.JobId, x.ReportId, x.OwnerUserId, x.Generation },
                        principalTable: "AppEducationLearningReportJobs",
                        principalColumns: new[] { "Id", "ReportId", "OwnerUserId", "Generation" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduReportSnapshots_Report",
                        columns: x => new { x.ReportId, x.OwnerUserId, x.SchoolId, x.SchoolClassId },
                        principalTable: "AppEducationLearningReports",
                        principalColumns: new[] { "Id", "OwnerUserId", "SchoolId", "SchoolClassId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationLearningReportStudentSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolClassId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResultJson = table.Column<string>(type: "text", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationLearningReportStudentSnapshots", x => x.Id);
                    table.CheckConstraint("CK_EduReportStudents_ResultSize", "length(\"ResultJson\") BETWEEN 1 AND 8388608");
                    table.ForeignKey(
                        name: "FK_EduReportStudents_Enrollment",
                        columns: x => new { x.StudentEnrollmentId, x.SchoolId, x.SchoolClassId, x.StudentUserId },
                        principalTable: "AppStudentEnrollments",
                        principalColumns: new[] { "Id", "SchoolId", "SchoolClassId", "StudentUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduReportStudents_Membership",
                        columns: x => new { x.SchoolMembershipId, x.SchoolId, x.StudentUserId },
                        principalTable: "AppSchoolMemberships",
                        principalColumns: new[] { "Id", "SchoolId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduReportStudents_Snapshot",
                        columns: x => new { x.SnapshotId, x.ReportId, x.OwnerUserId, x.SchoolId, x.SchoolClassId },
                        principalTable: "AppEducationLearningReportSnapshots",
                        principalColumns: new[] { "Id", "ReportId", "OwnerUserId", "SchoolId", "SchoolClassId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationMasteryRuleRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    RuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    EducationSubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    BasedOnRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    WindowDays = table.Column<int>(type: "integer", nullable: false),
                    MinimumIndependentQuestionCount = table.Column<int>(type: "integer", nullable: false),
                    MinimumAssessmentCount = table.Column<int>(type: "integer", nullable: false),
                    MaximumLatestEvidenceAgeDays = table.Column<int>(type: "integer", nullable: false),
                    TargetRatio = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ExpectedConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationMasteryRuleRevisions", x => x.Id);
                    table.UniqueConstraint("AK_EduMasteryVersions_Root", x => new { x.Id, x.RuleId });
                    table.CheckConstraint("CK_EduMasteryVersions_Values", "\"RevisionNumber\" > 0 AND \"WindowDays\" BETWEEN 1 AND 730 AND \"MinimumIndependentQuestionCount\" BETWEEN 1 AND 1000 AND \"MinimumAssessmentCount\" BETWEEN 1 AND 1000 AND \"MaximumLatestEvidenceAgeDays\" BETWEEN 1 AND \"WindowDays\" AND CAST(\"TargetRatio\" AS numeric) > 0 AND CAST(\"TargetRatio\" AS numeric) <= 1 AND length(trim(\"Reason\")) > 0");
                    table.ForeignKey(
                        name: "FK_EduMasteryVersions_Actor",
                        column: x => x.ActorUserId,
                        principalTable: "AbpUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduMasteryVersions_Previous",
                        columns: x => new { x.BasedOnRevisionId, x.RuleId },
                        principalTable: "AppEducationMasteryRuleRevisions",
                        principalColumns: new[] { "Id", "RuleId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationMasteryRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    EducationSubjectId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_AppEducationMasteryRules", x => x.Id);
                    table.UniqueConstraint("AK_EduMasteryRules_Scope", x => new { x.Id, x.SchoolId, x.EducationSubjectId });
                    table.CheckConstraint("CK_EduMasteryRules_Revision", "(\"RevisionNumber\" = 0 AND \"CurrentRevisionId\" IS NULL) OR (\"RevisionNumber\" > 0 AND \"CurrentRevisionId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_EduMasteryRules_Current",
                        columns: x => new { x.CurrentRevisionId, x.Id },
                        principalTable: "AppEducationMasteryRuleRevisions",
                        principalColumns: new[] { "Id", "RuleId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduMasteryRules_School",
                        column: x => x.SchoolId,
                        principalTable: "AppSchools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduMasteryRules_Subject",
                        column: x => x.EducationSubjectId,
                        principalTable: "AppEducationSubjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationLearningReportJobs_PreviousJobId_ReportId",
                table: "AppEducationLearningReportJobs",
                columns: new[] { "PreviousJobId", "ReportId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationLearningReportJobs_ReportId_OwnerUserId",
                table: "AppEducationLearningReportJobs",
                columns: new[] { "ReportId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationLearningReportJobs_RetryOfJobId_ReportId",
                table: "AppEducationLearningReportJobs",
                columns: new[] { "RetryOfJobId", "ReportId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationLearningReportJobs_SnapshotId_Id_ReportId",
                table: "AppEducationLearningReportJobs",
                columns: new[] { "SnapshotId", "Id", "ReportId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduReportJobs_Generation",
                table: "AppEducationLearningReportJobs",
                columns: new[] { "ReportId", "Generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EduReportJobs_Pending",
                table: "AppEducationLearningReportJobs",
                columns: new[] { "Status", "NextAttemptAtUtc", "LeaseExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationLearningReports_CurrentJobId_Id_OwnerUserId",
                table: "AppEducationLearningReports",
                columns: new[] { "CurrentJobId", "Id", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationLearningReports_CurrentSnapshotId_Id_OwnerUserI~",
                table: "AppEducationLearningReports",
                columns: new[] { "CurrentSnapshotId", "Id", "OwnerUserId", "SchoolId", "SchoolClassId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationLearningReports_TeachingAssignmentId_SchoolId_A~",
                table: "AppEducationLearningReports",
                columns: new[] { "TeachingAssignmentId", "SchoolId", "AcademicTermId", "SchoolClassId", "OwnerUserId", "EducationSubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationLearningReports_TextbookVersionId_SchoolId_Educ~",
                table: "AppEducationLearningReports",
                columns: new[] { "TextbookVersionId", "SchoolId", "EducationSubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduReports_Teaching",
                table: "AppEducationLearningReports",
                columns: new[] { "TenantId", "OwnerUserId", "TeachingAssignmentId", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationLearningReportSnapshots_JobId_ReportId_OwnerUse~",
                table: "AppEducationLearningReportSnapshots",
                columns: new[] { "JobId", "ReportId", "OwnerUserId", "Generation" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationLearningReportSnapshots_ReportId_OwnerUserId_Sc~",
                table: "AppEducationLearningReportSnapshots",
                columns: new[] { "ReportId", "OwnerUserId", "SchoolId", "SchoolClassId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduReportSnapshots_Generation",
                table: "AppEducationLearningReportSnapshots",
                columns: new[] { "ReportId", "Generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationLearningReportStudentSnapshots_SchoolMembership~",
                table: "AppEducationLearningReportStudentSnapshots",
                columns: new[] { "SchoolMembershipId", "SchoolId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationLearningReportStudentSnapshots_SnapshotId_Repor~",
                table: "AppEducationLearningReportStudentSnapshots",
                columns: new[] { "SnapshotId", "ReportId", "OwnerUserId", "SchoolId", "SchoolClassId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationLearningReportStudentSnapshots_StudentEnrollmen~",
                table: "AppEducationLearningReportStudentSnapshots",
                columns: new[] { "StudentEnrollmentId", "SchoolId", "SchoolClassId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduReportStudents_OnePerStudent",
                table: "AppEducationLearningReportStudentSnapshots",
                columns: new[] { "SnapshotId", "StudentUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationMasteryRuleRevisions_ActorUserId",
                table: "AppEducationMasteryRuleRevisions",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationMasteryRuleRevisions_BasedOnRevisionId_RuleId",
                table: "AppEducationMasteryRuleRevisions",
                columns: new[] { "BasedOnRevisionId", "RuleId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationMasteryRuleRevisions_RuleId_SchoolId_EducationS~",
                table: "AppEducationMasteryRuleRevisions",
                columns: new[] { "RuleId", "SchoolId", "EducationSubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduMasteryVersions_Sequence",
                table: "AppEducationMasteryRuleRevisions",
                columns: new[] { "RuleId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationMasteryRules_CurrentRevisionId_Id",
                table: "AppEducationMasteryRules",
                columns: new[] { "CurrentRevisionId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationMasteryRules_EducationSubjectId",
                table: "AppEducationMasteryRules",
                column: "EducationSubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_EduMasteryRules_Subject",
                table: "AppEducationMasteryRules",
                columns: new[] { "SchoolId", "EducationSubjectId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_EduReportJobs_Report",
                table: "AppEducationLearningReportJobs",
                columns: new[] { "ReportId", "OwnerUserId" },
                principalTable: "AppEducationLearningReports",
                principalColumns: new[] { "Id", "OwnerUserId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EduReportJobs_Snapshot",
                table: "AppEducationLearningReportJobs",
                columns: new[] { "SnapshotId", "Id", "ReportId" },
                principalTable: "AppEducationLearningReportSnapshots",
                principalColumns: new[] { "Id", "JobId", "ReportId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EduReports_CurrentSnapshot",
                table: "AppEducationLearningReports",
                columns: new[] { "CurrentSnapshotId", "Id", "OwnerUserId", "SchoolId", "SchoolClassId" },
                principalTable: "AppEducationLearningReportSnapshots",
                principalColumns: new[] { "Id", "ReportId", "OwnerUserId", "SchoolId", "SchoolClassId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EduMasteryVersions_Root",
                table: "AppEducationMasteryRuleRevisions",
                columns: new[] { "RuleId", "SchoolId", "EducationSubjectId" },
                principalTable: "AppEducationMasteryRules",
                principalColumns: new[] { "Id", "SchoolId", "EducationSubjectId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EduReportJobs_Report",
                table: "AppEducationLearningReportJobs");

            migrationBuilder.DropForeignKey(
                name: "FK_EduReportSnapshots_Report",
                table: "AppEducationLearningReportSnapshots");

            migrationBuilder.DropForeignKey(
                name: "FK_EduReportJobs_Snapshot",
                table: "AppEducationLearningReportJobs");

            migrationBuilder.DropForeignKey(
                name: "FK_EduMasteryVersions_Root",
                table: "AppEducationMasteryRuleRevisions");

            migrationBuilder.DropTable(
                name: "AppEducationLearningReportStudentSnapshots");

            migrationBuilder.DropTable(
                name: "AppEducationLearningReports");

            migrationBuilder.DropTable(
                name: "AppEducationLearningReportSnapshots");

            migrationBuilder.DropTable(
                name: "AppEducationLearningReportJobs");

            migrationBuilder.DropTable(
                name: "AppEducationMasteryRules");

            migrationBuilder.DropTable(
                name: "AppEducationMasteryRuleRevisions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_EduBooks_ReportScope",
                table: "AppTextbookVersions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_EduTeaching_ReportScope",
                table: "AppTeachingAssignments");
        }
    }
}
