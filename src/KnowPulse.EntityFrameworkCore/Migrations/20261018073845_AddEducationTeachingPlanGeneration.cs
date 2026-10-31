using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddEducationTeachingPlanGeneration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GenerationJobId",
                table: "AppEducationTeachingPlanRevisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppEducationTeachingPlanGenerationJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromptVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SubjectName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TextbookTitle = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    EducationSubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TextbookVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademicTermId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolClassId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeachingAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    RetryOfJobId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestJson = table.Column<string>(type: "text", nullable: false),
                    BasisJson = table.Column<string>(type: "text", nullable: false),
                    ContentJson = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Failure = table.Column<int>(type: "integer", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    RunId = table.Column<Guid>(type: "uuid", nullable: true),
                    HeartbeatAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ModelAlias = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ModelProvider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ModelVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OutputTokenLimit = table.Column<int>(type: "integer", nullable: true),
                    Temperature = table.Column<double>(type: "double precision", nullable: true),
                    DeploymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    PromptHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OutputHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PromptTokens = table.Column<int>(type: "integer", nullable: true),
                    CompletionTokens = table.Column<int>(type: "integer", nullable: true),
                    DurationMilliseconds = table.Column<long>(type: "bigint", nullable: true),
                    EstimatedCostUsd = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: true),
                    UsageMayBeIncomplete = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_AppEducationTeachingPlanGenerationJobs", x => x.Id);
                    table.UniqueConstraint("AK_EduPlanGeneration_Recipient", x => new { x.Id, x.PlanId, x.StudentUserId, x.OwnerUserId });
                    table.CheckConstraint("CK_EduPlanGeneration_Source", "length(\"BasisJson\") BETWEEN 1 AND 8388608 AND length(\"RequestJson\") BETWEEN 1 AND 32000 AND length(\"RequestFingerprint\") = 64 AND (\"RetryOfJobId\" IS NULL OR \"RetryOfJobId\" <> \"Id\")");
                    table.CheckConstraint("CK_EduPlanGeneration_State", "\"Status\" IN (0,1,2,3) AND \"Failure\" IN (0,1,2,3,4,5,6) AND ((\"Status\" IN (0,1,2) AND \"Failure\" = 0) OR (\"Status\" = 3 AND \"Failure\" <> 0)) AND ((\"Status\" = 0 AND \"RunId\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"CompletedAtUtc\" IS NULL) OR (\"Status\" = 1 AND \"RunId\" IS NOT NULL AND \"HeartbeatAtUtc\" IS NOT NULL AND \"LeaseExpiresAtUtc\" IS NOT NULL AND \"CompletedAtUtc\" IS NULL) OR (\"Status\" IN (2,3) AND \"RunId\" IS NOT NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"CompletedAtUtc\" IS NOT NULL)) AND ((\"Status\" = 2 AND \"ContentJson\" IS NOT NULL AND length(\"ContentJson\") BETWEEN 1 AND 262144 AND \"ModelAlias\" IS NOT NULL AND \"OutputHash\" IS NOT NULL) OR (\"Status\" <> 2 AND \"ContentJson\" IS NULL))");
                    table.CheckConstraint("CK_EduPlanGeneration_Usage", "(\"PromptTokens\" IS NULL OR \"PromptTokens\" >= 0) AND (\"CompletionTokens\" IS NULL OR \"CompletionTokens\" >= 0) AND (\"DurationMilliseconds\" IS NULL OR \"DurationMilliseconds\" >= 0) AND (\"EstimatedCostUsd\" IS NULL OR \"EstimatedCostUsd\" >= 0) AND ((\"ModelAlias\" IS NULL AND \"ModelVersion\" IS NULL AND \"ModelProvider\" IS NULL AND \"PromptHash\" IS NULL AND \"OutputTokenLimit\" IS NULL AND \"Temperature\" IS NULL) OR (\"ModelAlias\" IS NOT NULL AND \"ModelVersion\" IS NOT NULL AND \"ModelProvider\" IS NOT NULL AND \"PromptHash\" IS NOT NULL AND \"OutputTokenLimit\" IS NOT NULL AND \"Temperature\" IS NOT NULL AND length(\"PromptHash\") = 64 AND \"OutputTokenLimit\" BETWEEN 1 AND 8192 AND \"Temperature\" BETWEEN 0 AND 2))");
                    table.ForeignKey(
                        name: "FK_EduPlanGeneration_Book",
                        columns: x => new { x.TextbookVersionId, x.SchoolId, x.EducationSubjectId },
                        principalTable: "AppTextbookVersions",
                        principalColumns: new[] { "Id", "SchoolId", "EducationSubjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduPlanGeneration_Enrollment",
                        columns: x => new { x.StudentEnrollmentId, x.SchoolId, x.SchoolClassId, x.StudentUserId },
                        principalTable: "AppStudentEnrollments",
                        principalColumns: new[] { "Id", "SchoolId", "SchoolClassId", "StudentUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduPlanGeneration_Member",
                        columns: x => new { x.SchoolMembershipId, x.SchoolId, x.StudentUserId },
                        principalTable: "AppSchoolMemberships",
                        principalColumns: new[] { "Id", "SchoolId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduPlanGeneration_Retry",
                        columns: x => new { x.RetryOfJobId, x.PlanId, x.StudentUserId, x.OwnerUserId },
                        principalTable: "AppEducationTeachingPlanGenerationJobs",
                        principalColumns: new[] { "Id", "PlanId", "StudentUserId", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduPlanGeneration_Snapshot",
                        columns: x => new { x.ReportSnapshotId, x.ReportId, x.OwnerUserId, x.SchoolId, x.SchoolClassId },
                        principalTable: "AppEducationLearningReportSnapshots",
                        principalColumns: new[] { "Id", "ReportId", "OwnerUserId", "SchoolId", "SchoolClassId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduPlanGeneration_Teaching",
                        columns: x => new { x.TeachingAssignmentId, x.SchoolId, x.AcademicTermId, x.SchoolClassId, x.OwnerUserId, x.EducationSubjectId },
                        principalTable: "AppTeachingAssignments",
                        principalColumns: new[] { "Id", "SchoolId", "AcademicTermId", "SchoolClassId", "TeacherUserId", "EducationSubjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlanRevisions_GenerationJobId_PlanId_St~",
                table: "AppEducationTeachingPlanRevisions",
                columns: new[] { "GenerationJobId", "PlanId", "StudentUserId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlanGenerationJobs_ReportSnapshotId_Rep~",
                table: "AppEducationTeachingPlanGenerationJobs",
                columns: new[] { "ReportSnapshotId", "ReportId", "OwnerUserId", "SchoolId", "SchoolClassId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlanGenerationJobs_RetryOfJobId_PlanId_~",
                table: "AppEducationTeachingPlanGenerationJobs",
                columns: new[] { "RetryOfJobId", "PlanId", "StudentUserId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlanGenerationJobs_SchoolMembershipId_S~",
                table: "AppEducationTeachingPlanGenerationJobs",
                columns: new[] { "SchoolMembershipId", "SchoolId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlanGenerationJobs_StudentEnrollmentId_~",
                table: "AppEducationTeachingPlanGenerationJobs",
                columns: new[] { "StudentEnrollmentId", "SchoolId", "SchoolClassId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlanGenerationJobs_TeachingAssignmentId~",
                table: "AppEducationTeachingPlanGenerationJobs",
                columns: new[] { "TeachingAssignmentId", "SchoolId", "AcademicTermId", "SchoolClassId", "OwnerUserId", "EducationSubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlanGenerationJobs_TextbookVersionId_Sc~",
                table: "AppEducationTeachingPlanGenerationJobs",
                columns: new[] { "TextbookVersionId", "SchoolId", "EducationSubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduPlanGeneration_Due",
                table: "AppEducationTeachingPlanGenerationJobs",
                columns: new[] { "Status", "LeaseExpiresAtUtc", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EduPlanGeneration_OneActiveOwner",
                table: "AppEducationTeachingPlanGenerationJobs",
                column: "OwnerUserId",
                unique: true,
                filter: "\"IsDeleted\" = false AND \"Status\" IN (0,1)");

            migrationBuilder.CreateIndex(
                name: "IX_EduPlanGeneration_OwnerTime",
                table: "AppEducationTeachingPlanGenerationJobs",
                columns: new[] { "TenantId", "OwnerUserId", "RequestedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_EduPlanVersions_Generation",
                table: "AppEducationTeachingPlanRevisions",
                columns: new[] { "GenerationJobId", "PlanId", "StudentUserId", "OwnerUserId" },
                principalTable: "AppEducationTeachingPlanGenerationJobs",
                principalColumns: new[] { "Id", "PlanId", "StudentUserId", "OwnerUserId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EduPlanVersions_Generation",
                table: "AppEducationTeachingPlanRevisions");

            migrationBuilder.DropTable(
                name: "AppEducationTeachingPlanGenerationJobs");

            migrationBuilder.DropIndex(
                name: "IX_AppEducationTeachingPlanRevisions_GenerationJobId_PlanId_St~",
                table: "AppEducationTeachingPlanRevisions");

            migrationBuilder.DropColumn(
                name: "GenerationJobId",
                table: "AppEducationTeachingPlanRevisions");
        }
    }
}
