using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddEducationTeachingPlanHintReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_EduPlanContexts_Recipient",
                table: "AppEducationTeachingPlanQuestionExposures",
                columns: new[] { "Id", "PlanId", "PlanRevisionId", "StudentUserId" });

            migrationBuilder.CreateTable(
                name: "AppEducationTeachingPlanHintReviewRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExposureId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_AppEducationTeachingPlanHintReviewRevisions", x => x.Id);
                    table.UniqueConstraint("AK_EduPlanHintVersions_Root", x => new { x.Id, x.ExposureId });
                    table.CheckConstraint("CK_EduPlanHintVersions_State", "\"RevisionNumber\" > 0 AND \"Use\" IN (0,1,2) AND length(trim(\"Reason\")) BETWEEN 1 AND 2048");
                    table.ForeignKey(
                        name: "FK_EduPlanHintVersions_Previous",
                        columns: x => new { x.BasedOnRevisionId, x.ExposureId },
                        principalTable: "AppEducationTeachingPlanHintReviewRevisions",
                        principalColumns: new[] { "Id", "ExposureId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationTeachingPlanHintReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_AppEducationTeachingPlanHintReviews", x => x.Id);
                    table.UniqueConstraint("AK_EduPlanHintReviews_Scope", x => new { x.Id, x.StudentUserId, x.OwnerUserId, x.PlanId, x.PlanRevisionId });
                    table.CheckConstraint("CK_EduPlanHintReviews_State", "(\"RevisionNumber\" = 0 AND \"CurrentRevisionId\" IS NULL) OR (\"RevisionNumber\" > 0 AND \"CurrentRevisionId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_EduPlanHintReviews_Context",
                        columns: x => new { x.Id, x.PlanId, x.PlanRevisionId, x.StudentUserId },
                        principalTable: "AppEducationTeachingPlanQuestionExposures",
                        principalColumns: new[] { "Id", "PlanId", "PlanRevisionId", "StudentUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduPlanHintReviews_Current",
                        columns: x => new { x.CurrentRevisionId, x.Id },
                        principalTable: "AppEducationTeachingPlanHintReviewRevisions",
                        principalColumns: new[] { "Id", "ExposureId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduPlanHintReviews_PublicVersion",
                        columns: x => new { x.PlanRevisionId, x.PlanId, x.StudentUserId, x.OwnerUserId },
                        principalTable: "AppEducationTeachingPlanRevisions",
                        principalColumns: new[] { "Id", "PlanId", "StudentUserId", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlanHintReviewRevisions_BasedOnRevision~",
                table: "AppEducationTeachingPlanHintReviewRevisions",
                columns: new[] { "BasedOnRevisionId", "ExposureId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlanHintReviewRevisions_ExposureId_Stud~",
                table: "AppEducationTeachingPlanHintReviewRevisions",
                columns: new[] { "ExposureId", "StudentUserId", "OwnerUserId", "PlanId", "PlanRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduPlanHintVersions_Sequence",
                table: "AppEducationTeachingPlanHintReviewRevisions",
                columns: new[] { "ExposureId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlanHintReviews_CurrentRevisionId_Id",
                table: "AppEducationTeachingPlanHintReviews",
                columns: new[] { "CurrentRevisionId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlanHintReviews_Id_PlanId_PlanRevisionI~",
                table: "AppEducationTeachingPlanHintReviews",
                columns: new[] { "Id", "PlanId", "PlanRevisionId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlanHintReviews_PlanRevisionId_PlanId_S~",
                table: "AppEducationTeachingPlanHintReviews",
                columns: new[] { "PlanRevisionId", "PlanId", "StudentUserId", "OwnerUserId" });

            migrationBuilder.AddForeignKey(
                name: "FK_EduPlanHintVersions_Root",
                table: "AppEducationTeachingPlanHintReviewRevisions",
                columns: new[] { "ExposureId", "StudentUserId", "OwnerUserId", "PlanId", "PlanRevisionId" },
                principalTable: "AppEducationTeachingPlanHintReviews",
                principalColumns: new[] { "Id", "StudentUserId", "OwnerUserId", "PlanId", "PlanRevisionId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EduPlanHintVersions_Root",
                table: "AppEducationTeachingPlanHintReviewRevisions");

            migrationBuilder.DropTable(
                name: "AppEducationTeachingPlanHintReviews");

            migrationBuilder.DropTable(
                name: "AppEducationTeachingPlanHintReviewRevisions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_EduPlanContexts_Recipient",
                table: "AppEducationTeachingPlanQuestionExposures");
        }
    }
}
