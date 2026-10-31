using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddEducationTeachingActivities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppEducationTeachingActivities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepKind = table.Column<int>(type: "integer", nullable: false),
                    StepOrder = table.Column<int>(type: "integer", nullable: false),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DueAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    FirstProvidedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    CurrentRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    StudentRevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    CurrentStudentRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentFollowUpRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_AppEducationTeachingActivities", x => x.Id);
                    table.UniqueConstraint("AK_EduActivities_Recipient", x => new { x.Id, x.StudentUserId, x.OwnerUserId, x.StepKind });
                    table.CheckConstraint("CK_EduActivities_State", "((\"RevisionNumber\" = 0 AND \"CurrentRevisionId\" IS NULL) OR (\"RevisionNumber\" > 0 AND \"CurrentRevisionId\" IS NOT NULL)) AND ((\"StudentRevisionNumber\" = 0 AND \"CurrentStudentRevisionId\" IS NULL) OR (\"StudentRevisionNumber\" > 0 AND \"CurrentStudentRevisionId\" IS NOT NULL)) AND \"StudentRevisionNumber\" <= \"RevisionNumber\" AND \"StepKind\" IN (0,1,2,3,4,5) AND \"StepOrder\" BETWEEN 1 AND 50 AND \"EstimatedMinutes\" BETWEEN 1 AND 1440 AND \"StartsAtUtc\" <= \"DueAtUtc\" AND (\"FirstProvidedAtUtc\" IS NULL OR \"FirstProvidedAtUtc\" >= \"AssignedAtUtc\")");
                    table.ForeignKey(
                        name: "FK_EduActivities_Publication",
                        columns: x => new { x.PlanRevisionId, x.PlanId, x.StudentUserId, x.OwnerUserId },
                        principalTable: "AppEducationTeachingPlanRevisions",
                        principalColumns: new[] { "Id", "PlanId", "StudentUserId", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationTeachingActivityRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActivityId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepKind = table.Column<int>(type: "integer", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    StudentRevisionNumber = table.Column<int>(type: "integer", nullable: true),
                    BasedOnRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReplyToStudentRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    Difficulty = table.Column<int>(type: "integer", nullable: true),
                    SharedText = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    InternalTeacherNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ReportedMinutes = table.Column<int>(type: "integer", nullable: true),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ExpectedConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationTeachingActivityRevisions", x => x.Id);
                    table.UniqueConstraint("AK_EduActivityVersions_Root", x => new { x.Id, x.ActivityId });
                    table.CheckConstraint("CK_EduActivityVersions_Action", "\"RevisionNumber\" > 0 AND \"Action\" IN (0,1,2,3) AND ((\"Action\" = 3 AND \"ActorUserId\" = \"OwnerUserId\" AND \"StudentRevisionNumber\" IS NULL AND \"ReplyToStudentRevisionId\" IS NOT NULL AND \"InternalTeacherNote\" IS NOT NULL AND length(trim(\"InternalTeacherNote\")) BETWEEN 1 AND 2000 AND \"SharedText\" IS NULL AND \"Difficulty\" IS NULL AND \"ReportedMinutes\" IS NULL) OR (\"Action\" IN (0,1,2) AND \"ActorUserId\" = \"StudentUserId\" AND \"StudentRevisionNumber\" IS NOT NULL AND \"StudentRevisionNumber\" > 0 AND \"InternalTeacherNote\" IS NULL AND \"ReplyToStudentRevisionId\" IS NULL AND (\"SharedText\" IS NULL OR length(\"SharedText\") <= 2000) AND ((\"Action\" = 2 AND \"Difficulty\" IS NOT NULL AND \"Difficulty\" IN (0,1,2,3) AND \"SharedText\" IS NOT NULL AND length(trim(\"SharedText\")) > 0) OR (\"Action\" IN (0,1) AND \"Difficulty\" IS NULL)) AND ((\"Action\" = 1 AND \"StepKind\" NOT IN (0,4) AND (\"ReportedMinutes\" IS NULL OR \"ReportedMinutes\" BETWEEN 1 AND 1440)) OR (\"Action\" IN (0,2) AND \"ReportedMinutes\" IS NULL))))");
                    table.ForeignKey(
                        name: "FK_EduActivityVersions_BasedOnRevisionId",
                        columns: x => new { x.BasedOnRevisionId, x.ActivityId },
                        principalTable: "AppEducationTeachingActivityRevisions",
                        principalColumns: new[] { "Id", "ActivityId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduActivityVersions_Recipient",
                        columns: x => new { x.ActivityId, x.StudentUserId, x.OwnerUserId, x.StepKind },
                        principalTable: "AppEducationTeachingActivities",
                        principalColumns: new[] { "Id", "StudentUserId", "OwnerUserId", "StepKind" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduActivityVersions_ReplyToStudentRevisionId",
                        columns: x => new { x.ReplyToStudentRevisionId, x.ActivityId },
                        principalTable: "AppEducationTeachingActivityRevisions",
                        principalColumns: new[] { "Id", "ActivityId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingActivities_CurrentFollowUpRevisionId_Id",
                table: "AppEducationTeachingActivities",
                columns: new[] { "CurrentFollowUpRevisionId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingActivities_CurrentRevisionId_Id",
                table: "AppEducationTeachingActivities",
                columns: new[] { "CurrentRevisionId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingActivities_CurrentStudentRevisionId_Id",
                table: "AppEducationTeachingActivities",
                columns: new[] { "CurrentStudentRevisionId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingActivities_PlanRevisionId_PlanId_Studen~",
                table: "AppEducationTeachingActivities",
                columns: new[] { "PlanRevisionId", "PlanId", "StudentUserId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduActivities_AssignedStep",
                table: "AppEducationTeachingActivities",
                columns: new[] { "PlanRevisionId", "StepId", "StudentUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EduActivities_Student",
                table: "AppEducationTeachingActivities",
                columns: new[] { "TenantId", "StudentUserId", "PlanId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingActivityRevisions_ActivityId_StudentUse~",
                table: "AppEducationTeachingActivityRevisions",
                columns: new[] { "ActivityId", "StudentUserId", "OwnerUserId", "StepKind" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingActivityRevisions_BasedOnRevisionId_Act~",
                table: "AppEducationTeachingActivityRevisions",
                columns: new[] { "BasedOnRevisionId", "ActivityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingActivityRevisions_ReplyToStudentRevisio~",
                table: "AppEducationTeachingActivityRevisions",
                columns: new[] { "ReplyToStudentRevisionId", "ActivityId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduActivityVersions_Sequence",
                table: "AppEducationTeachingActivityRevisions",
                columns: new[] { "ActivityId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EduActivityVersions_StudentSequence",
                table: "AppEducationTeachingActivityRevisions",
                columns: new[] { "ActivityId", "StudentRevisionNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_EduActivities_CurrentFollowUpRevisionId",
                table: "AppEducationTeachingActivities",
                columns: new[] { "CurrentFollowUpRevisionId", "Id" },
                principalTable: "AppEducationTeachingActivityRevisions",
                principalColumns: new[] { "Id", "ActivityId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EduActivities_CurrentRevisionId",
                table: "AppEducationTeachingActivities",
                columns: new[] { "CurrentRevisionId", "Id" },
                principalTable: "AppEducationTeachingActivityRevisions",
                principalColumns: new[] { "Id", "ActivityId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EduActivities_CurrentStudentRevisionId",
                table: "AppEducationTeachingActivities",
                columns: new[] { "CurrentStudentRevisionId", "Id" },
                principalTable: "AppEducationTeachingActivityRevisions",
                principalColumns: new[] { "Id", "ActivityId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EduActivities_CurrentFollowUpRevisionId",
                table: "AppEducationTeachingActivities");

            migrationBuilder.DropForeignKey(
                name: "FK_EduActivities_CurrentRevisionId",
                table: "AppEducationTeachingActivities");

            migrationBuilder.DropForeignKey(
                name: "FK_EduActivities_CurrentStudentRevisionId",
                table: "AppEducationTeachingActivities");

            migrationBuilder.DropTable(
                name: "AppEducationTeachingActivityRevisions");

            migrationBuilder.DropTable(
                name: "AppEducationTeachingActivities");
        }
    }
}
