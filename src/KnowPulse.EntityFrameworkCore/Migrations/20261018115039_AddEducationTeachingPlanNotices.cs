using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddEducationTeachingPlanNotices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_EduPlans_NoticeSchool",
                table: "AppEducationTeachingPlans",
                columns: new[] { "Id", "SchoolId", "StudentUserId", "OwnerUserId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_EduActivities_NoticePlan",
                table: "AppEducationTeachingActivities",
                columns: new[] { "Id", "PlanId", "PlanRevisionId", "StudentUserId", "OwnerUserId" });

            migrationBuilder.CreateTable(
                name: "AppEducationTeachingPlanNotices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublishedRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    ContentRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActivityId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActivityRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationTeachingPlanNotices", x => x.Id);
                    table.CheckConstraint("CK_EduPlanNotices_ReadTime", "\"ReadAtUtc\" IS NULL OR \"ReadAtUtc\" >= \"RecordedAtUtc\"");
                    table.CheckConstraint("CK_EduPlanNotices_Source", "(\"Kind\" IN (0, 1) AND \"ContentRevisionId\" IS NOT NULL AND \"DecisionId\" IS NULL AND \"ActivityId\" IS NULL AND \"ActivityRevisionId\" IS NULL) OR (\"Kind\" = 2 AND \"ContentRevisionId\" IS NULL AND \"DecisionId\" IS NOT NULL AND \"ActivityId\" IS NULL AND \"ActivityRevisionId\" IS NULL) OR (\"Kind\" = 3 AND \"ContentRevisionId\" IS NULL AND \"DecisionId\" IS NULL AND \"ActivityId\" IS NOT NULL AND \"ActivityRevisionId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_EduPlanNotices_Activity",
                        columns: x => new { x.ActivityId, x.PlanId, x.PublishedRevisionId, x.StudentUserId, x.OwnerUserId },
                        principalTable: "AppEducationTeachingActivities",
                        principalColumns: new[] { "Id", "PlanId", "PlanRevisionId", "StudentUserId", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduPlanNotices_ContentRevisionId",
                        columns: x => new { x.ContentRevisionId, x.PlanId, x.StudentUserId, x.OwnerUserId },
                        principalTable: "AppEducationTeachingPlanRevisions",
                        principalColumns: new[] { "Id", "PlanId", "StudentUserId", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduPlanNotices_Decision",
                        columns: x => new { x.DecisionId, x.PlanId, x.StudentUserId, x.OwnerUserId },
                        principalTable: "AppEducationTeachingPlanDecisions",
                        principalColumns: new[] { "Id", "PlanId", "StudentUserId", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduPlanNotices_FollowUp",
                        columns: x => new { x.ActivityRevisionId, x.ActivityId },
                        principalTable: "AppEducationTeachingActivityRevisions",
                        principalColumns: new[] { "Id", "ActivityId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduPlanNotices_PlanSchool",
                        columns: x => new { x.PlanId, x.SchoolId, x.StudentUserId, x.OwnerUserId },
                        principalTable: "AppEducationTeachingPlans",
                        principalColumns: new[] { "Id", "SchoolId", "StudentUserId", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduPlanNotices_PublishedRevisionId",
                        columns: x => new { x.PublishedRevisionId, x.PlanId, x.StudentUserId, x.OwnerUserId },
                        principalTable: "AppEducationTeachingPlanRevisions",
                        principalColumns: new[] { "Id", "PlanId", "StudentUserId", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlanNotices_ActivityId_PlanId_Published~",
                table: "AppEducationTeachingPlanNotices",
                columns: new[] { "ActivityId", "PlanId", "PublishedRevisionId", "StudentUserId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlanNotices_ActivityRevisionId",
                table: "AppEducationTeachingPlanNotices",
                column: "ActivityRevisionId",
                unique: true,
                filter: "\"ActivityRevisionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlanNotices_ActivityRevisionId_Activity~",
                table: "AppEducationTeachingPlanNotices",
                columns: new[] { "ActivityRevisionId", "ActivityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlanNotices_ContentRevisionId",
                table: "AppEducationTeachingPlanNotices",
                column: "ContentRevisionId",
                unique: true,
                filter: "\"ContentRevisionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlanNotices_ContentRevisionId_PlanId_St~",
                table: "AppEducationTeachingPlanNotices",
                columns: new[] { "ContentRevisionId", "PlanId", "StudentUserId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlanNotices_DecisionId",
                table: "AppEducationTeachingPlanNotices",
                column: "DecisionId",
                unique: true,
                filter: "\"DecisionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlanNotices_DecisionId_PlanId_StudentUs~",
                table: "AppEducationTeachingPlanNotices",
                columns: new[] { "DecisionId", "PlanId", "StudentUserId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlanNotices_PlanId_SchoolId_StudentUser~",
                table: "AppEducationTeachingPlanNotices",
                columns: new[] { "PlanId", "SchoolId", "StudentUserId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlanNotices_PublishedRevisionId_PlanId_~",
                table: "AppEducationTeachingPlanNotices",
                columns: new[] { "PublishedRevisionId", "PlanId", "StudentUserId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduPlanNotices_Inbox",
                table: "AppEducationTeachingPlanNotices",
                columns: new[] { "TenantId", "StudentUserId", "SchoolId", "RecordedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppEducationTeachingPlanNotices");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_EduPlans_NoticeSchool",
                table: "AppEducationTeachingPlans");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_EduActivities_NoticePlan",
                table: "AppEducationTeachingActivities");
        }
    }
}
