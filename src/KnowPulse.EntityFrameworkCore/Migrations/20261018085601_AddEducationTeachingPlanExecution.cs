using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddEducationTeachingPlanExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CurrentDecisionId",
                table: "AppEducationTeachingPlans",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DecisionRevisionNumber",
                table: "AppEducationTeachingPlans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExecutionChangedAtUtc",
                table: "AppEducationTeachingPlans",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExecutionState",
                table: "AppEducationTeachingPlans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "AppEducationTeachingPlanDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublishedRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BasedOnDecisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    Decision = table.Column<int>(type: "integer", nullable: false),
                    Before = table.Column<int>(type: "integer", nullable: false),
                    After = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    EvidenceJson = table.Column<string>(type: "text", nullable: false),
                    EvidenceFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpectedConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationTeachingPlanDecisions", x => x.Id);
                    table.UniqueConstraint("AK_EduPlanDecisions_Recipient", x => new { x.Id, x.PlanId, x.StudentUserId, x.OwnerUserId });
                    table.CheckConstraint("CK_EduPlanDecisions_Source", "length(\"EvidenceJson\") BETWEEN 1 AND 2097152 AND length(\"Reason\") BETWEEN 1 AND 2000 AND length(\"EvidenceFingerprint\") = 64 AND length(\"RequestFingerprint\") = 64 AND ((\"RevisionNumber\" = 1 AND \"BasedOnDecisionId\" IS NULL) OR (\"RevisionNumber\" > 1 AND \"BasedOnDecisionId\" IS NOT NULL AND \"BasedOnDecisionId\" <> \"Id\"))");
                    table.CheckConstraint("CK_EduPlanDecisions_State", "\"Before\" IN (0,1) AND ((\"Decision\" = 0 AND \"Before\" = 0 AND \"After\" = 1) OR (\"Decision\" = 1 AND \"Before\" = 1 AND \"After\" = 0) OR (\"Decision\" = 2 AND \"Before\" = 0 AND \"After\" = 0) OR (\"Decision\" = 3 AND \"After\" = 1) OR (\"Decision\" = 4 AND \"After\" = 2) OR (\"Decision\" = 5 AND \"After\" = 3))");
                    table.ForeignKey(
                        name: "FK_EduPlanDecisions_Plan",
                        columns: x => new { x.PlanId, x.StudentUserId, x.OwnerUserId },
                        principalTable: "AppEducationTeachingPlans",
                        principalColumns: new[] { "Id", "StudentUserId", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduPlanDecisions_Predecessor",
                        columns: x => new { x.BasedOnDecisionId, x.PlanId, x.StudentUserId, x.OwnerUserId },
                        principalTable: "AppEducationTeachingPlanDecisions",
                        principalColumns: new[] { "Id", "PlanId", "StudentUserId", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduPlanDecisions_Publication",
                        columns: x => new { x.PublishedRevisionId, x.PlanId, x.StudentUserId, x.OwnerUserId },
                        principalTable: "AppEducationTeachingPlanRevisions",
                        principalColumns: new[] { "Id", "PlanId", "StudentUserId", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlans_CurrentDecisionId_Id_StudentUserI~",
                table: "AppEducationTeachingPlans",
                columns: new[] { "CurrentDecisionId", "Id", "StudentUserId", "OwnerUserId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_EduPlans_Execution",
                table: "AppEducationTeachingPlans",
                sql: "\"ExecutionState\" BETWEEN 0 AND 3 AND ((\"DecisionRevisionNumber\" = 0 AND \"CurrentDecisionId\" IS NULL AND \"ExecutionState\" = 0 AND \"ExecutionChangedAtUtc\" IS NULL) OR (\"DecisionRevisionNumber\" > 0 AND \"CurrentDecisionId\" IS NOT NULL AND (\"ExecutionState\" = 0 OR \"ExecutionChangedAtUtc\" IS NOT NULL)))");

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlanDecisions_BasedOnDecisionId_PlanId_~",
                table: "AppEducationTeachingPlanDecisions",
                columns: new[] { "BasedOnDecisionId", "PlanId", "StudentUserId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlanDecisions_PlanId_StudentUserId_Owne~",
                table: "AppEducationTeachingPlanDecisions",
                columns: new[] { "PlanId", "StudentUserId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlanDecisions_PublishedRevisionId_PlanI~",
                table: "AppEducationTeachingPlanDecisions",
                columns: new[] { "PublishedRevisionId", "PlanId", "StudentUserId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduPlanDecisions_Sequence",
                table: "AppEducationTeachingPlanDecisions",
                columns: new[] { "PlanId", "RevisionNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_EduPlans_CurrentDecision",
                table: "AppEducationTeachingPlans",
                columns: new[] { "CurrentDecisionId", "Id", "StudentUserId", "OwnerUserId" },
                principalTable: "AppEducationTeachingPlanDecisions",
                principalColumns: new[] { "Id", "PlanId", "StudentUserId", "OwnerUserId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EduPlans_CurrentDecision",
                table: "AppEducationTeachingPlans");

            migrationBuilder.DropTable(
                name: "AppEducationTeachingPlanDecisions");

            migrationBuilder.DropIndex(
                name: "IX_AppEducationTeachingPlans_CurrentDecisionId_Id_StudentUserI~",
                table: "AppEducationTeachingPlans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EduPlans_Execution",
                table: "AppEducationTeachingPlans");

            migrationBuilder.DropColumn(
                name: "CurrentDecisionId",
                table: "AppEducationTeachingPlans");

            migrationBuilder.DropColumn(
                name: "DecisionRevisionNumber",
                table: "AppEducationTeachingPlans");

            migrationBuilder.DropColumn(
                name: "ExecutionChangedAtUtc",
                table: "AppEducationTeachingPlans");

            migrationBuilder.DropColumn(
                name: "ExecutionState",
                table: "AppEducationTeachingPlans");
        }
    }
}
