using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddExactStudyMaterialHintReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppEducationStudyHintReviewRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaperQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_AppEducationStudyHintReviewRevisions", x => x.Id);
                    table.UniqueConstraint("AK_EduStudyHintVersions_Head", x => new { x.Id, x.ReviewId, x.RevisionNumber });
                    table.UniqueConstraint("AK_EduStudyHintVersions_Root", x => new { x.Id, x.ReviewId });
                    table.CheckConstraint("CK_EduStudyHintVersions_State", "\"RevisionNumber\" > 0 AND \"Use\" IN (0,1,2) AND length(trim(\"Reason\")) BETWEEN 1 AND 2048 AND length(\"RequestFingerprint\") = 64");
                    table.ForeignKey(
                        name: "FK_EduStudyHintVersions_Previous",
                        columns: x => new { x.BasedOnRevisionId, x.ReviewId },
                        principalTable: "AppEducationStudyHintReviewRevisions",
                        principalColumns: new[] { "Id", "ReviewId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationStudyHintReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecipientId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaperQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamPaperId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExposedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
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
                    table.PrimaryKey("PK_AppEducationStudyHintReviews", x => x.Id);
                    table.UniqueConstraint("AK_EduStudyHints_Context", x => new { x.Id, x.RecipientId, x.PaperQuestionId, x.StudentUserId, x.OwnerUserId });
                    table.CheckConstraint("CK_EduStudyHints_State", "(\"RevisionNumber\" = 0 AND \"CurrentRevisionId\" IS NULL) OR (\"RevisionNumber\" > 0 AND \"CurrentRevisionId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_EduStudyHints_Current",
                        columns: x => new { x.CurrentRevisionId, x.Id, x.RevisionNumber },
                        principalTable: "AppEducationStudyHintReviewRevisions",
                        principalColumns: new[] { "Id", "ReviewId", "RevisionNumber" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduStudyHints_Question",
                        columns: x => new { x.PaperQuestionId, x.ExamPaperId },
                        principalTable: "AppExamPaperQuestions",
                        principalColumns: new[] { "Id", "PaperId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduStudyHints_Recipient",
                        columns: x => new { x.RecipientId, x.StudentUserId, x.OwnerUserId },
                        principalTable: "AppEducationStudyRecipients",
                        principalColumns: new[] { "Id", "StudentUserId", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudyHintReviewRevisions_BasedOnRevisionId_Revi~",
                table: "AppEducationStudyHintReviewRevisions",
                columns: new[] { "BasedOnRevisionId", "ReviewId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudyHintReviewRevisions_ReviewId_RecipientId_P~",
                table: "AppEducationStudyHintReviewRevisions",
                columns: new[] { "ReviewId", "RecipientId", "PaperQuestionId", "StudentUserId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduStudyHintVersions_Sequence",
                table: "AppEducationStudyHintReviewRevisions",
                columns: new[] { "ReviewId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudyHintReviews_CurrentRevisionId_Id_RevisionN~",
                table: "AppEducationStudyHintReviews",
                columns: new[] { "CurrentRevisionId", "Id", "RevisionNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudyHintReviews_PaperQuestionId_ExamPaperId",
                table: "AppEducationStudyHintReviews",
                columns: new[] { "PaperQuestionId", "ExamPaperId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudyHintReviews_RecipientId_StudentUserId_Owne~",
                table: "AppEducationStudyHintReviews",
                columns: new[] { "RecipientId", "StudentUserId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduStudyHints_Question",
                table: "AppEducationStudyHintReviews",
                columns: new[] { "RecipientId", "PaperQuestionId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_EduStudyHintVersions_Root",
                table: "AppEducationStudyHintReviewRevisions",
                columns: new[] { "ReviewId", "RecipientId", "PaperQuestionId", "StudentUserId", "OwnerUserId" },
                principalTable: "AppEducationStudyHintReviews",
                principalColumns: new[] { "Id", "RecipientId", "PaperQuestionId", "StudentUserId", "OwnerUserId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "AppEducationStudyHintReviews") OR EXISTS (SELECT 1 FROM "AppEducationStudyHintReviewRevisions") THEN
                        RAISE EXCEPTION 'Study hint review history exists; downgrade requires an explicit compatible restore.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_EduStudyHintVersions_Root",
                table: "AppEducationStudyHintReviewRevisions");

            migrationBuilder.DropTable(
                name: "AppEducationStudyHintReviews");

            migrationBuilder.DropTable(
                name: "AppEducationStudyHintReviewRevisions");
        }
    }
}
