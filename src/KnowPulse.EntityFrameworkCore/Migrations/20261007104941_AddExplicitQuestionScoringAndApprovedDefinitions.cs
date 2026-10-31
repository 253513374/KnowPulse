using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddExplicitQuestionScoringAndApprovedDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedDefinitionRevisionId",
                table: "AppQuestions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScoringDefinitionJson",
                table: "AppQuestions",
                type: "character varying(64000)",
                maxLength: 64000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DefinitionRevisionId",
                table: "AppQuestionReviews",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpectedConcurrencyStamp",
                table: "AppQuestionReviews",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperationRequestHash",
                table: "AppQuestionReviews",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PersonalOwnerUserId",
                table: "AppQuestionReviews",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "QuestionDefinitionRevisionId",
                table: "AppExamPaperQuestions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScoringDefinitionJson",
                table: "AppExamPaperQuestions",
                type: "character varying(64000)",
                maxLength: 64000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScoringDefinitionJson",
                table: "AppAiQuestionCandidates",
                type: "character varying(64000)",
                maxLength: 64000,
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_AppQuestionReviews_Id_QuestionId",
                table: "AppQuestionReviews",
                columns: new[] { "Id", "QuestionId" });

            migrationBuilder.CreateTable(
                name: "AppQuestionDefinitionRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    QuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonalOwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    ReviewOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DefinitionHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DefinitionJson = table.Column<string>(type: "character varying(1000000)", maxLength: 1000000, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppQuestionDefinitionRevisions", x => x.Id);
                    table.UniqueConstraint("AK_AppQuestionDefinitionRevisions_Id_QuestionId", x => new { x.Id, x.QuestionId });
                    table.ForeignKey(
                        name: "FK_AppQuestionDefinitionRevisions_AppQuestionReviews_ReviewOpe~",
                        columns: x => new { x.ReviewOperationId, x.QuestionId },
                        principalTable: "AppQuestionReviews",
                        principalColumns: new[] { "Id", "QuestionId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppQuestionDefinitionRevisions_AppQuestions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "AppQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppQuestions_ApprovedDefinitionRevisionId_Id",
                table: "AppQuestions",
                columns: new[] { "ApprovedDefinitionRevisionId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AppExamPaperQuestions_QuestionDefinitionRevisionId_Original~",
                table: "AppExamPaperQuestions",
                columns: new[] { "QuestionDefinitionRevisionId", "OriginalQuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppQuestionDefinitionRevisions_QuestionId_RevisionNumber",
                table: "AppQuestionDefinitionRevisions",
                columns: new[] { "QuestionId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppQuestionDefinitionRevisions_ReviewOperationId",
                table: "AppQuestionDefinitionRevisions",
                column: "ReviewOperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppQuestionDefinitionRevisions_ReviewOperationId_QuestionId",
                table: "AppQuestionDefinitionRevisions",
                columns: new[] { "ReviewOperationId", "QuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppQuestionDefinitionRevisions_TenantId_PersonalOwnerUserId~",
                table: "AppQuestionDefinitionRevisions",
                columns: new[] { "TenantId", "PersonalOwnerUserId", "QuestionId" });

            migrationBuilder.AddForeignKey(
                name: "FK_AppExamPaperQuestions_AppQuestionDefinitionRevisions_Questi~",
                table: "AppExamPaperQuestions",
                columns: new[] { "QuestionDefinitionRevisionId", "OriginalQuestionId" },
                principalTable: "AppQuestionDefinitionRevisions",
                principalColumns: new[] { "Id", "QuestionId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AppQuestions_AppQuestionDefinitionRevisions_ApprovedDefinit~",
                table: "AppQuestions",
                columns: new[] { "ApprovedDefinitionRevisionId", "Id" },
                principalTable: "AppQuestionDefinitionRevisions",
                principalColumns: new[] { "Id", "QuestionId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppExamPaperQuestions_AppQuestionDefinitionRevisions_Questi~",
                table: "AppExamPaperQuestions");

            migrationBuilder.DropForeignKey(
                name: "FK_AppQuestions_AppQuestionDefinitionRevisions_ApprovedDefinit~",
                table: "AppQuestions");

            migrationBuilder.DropTable(
                name: "AppQuestionDefinitionRevisions");

            migrationBuilder.DropIndex(
                name: "IX_AppQuestions_ApprovedDefinitionRevisionId_Id",
                table: "AppQuestions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_AppQuestionReviews_Id_QuestionId",
                table: "AppQuestionReviews");

            migrationBuilder.DropIndex(
                name: "IX_AppExamPaperQuestions_QuestionDefinitionRevisionId_Original~",
                table: "AppExamPaperQuestions");

            migrationBuilder.DropColumn(
                name: "ApprovedDefinitionRevisionId",
                table: "AppQuestions");

            migrationBuilder.DropColumn(
                name: "ScoringDefinitionJson",
                table: "AppQuestions");

            migrationBuilder.DropColumn(
                name: "DefinitionRevisionId",
                table: "AppQuestionReviews");

            migrationBuilder.DropColumn(
                name: "ExpectedConcurrencyStamp",
                table: "AppQuestionReviews");

            migrationBuilder.DropColumn(
                name: "OperationRequestHash",
                table: "AppQuestionReviews");

            migrationBuilder.DropColumn(
                name: "PersonalOwnerUserId",
                table: "AppQuestionReviews");

            migrationBuilder.DropColumn(
                name: "QuestionDefinitionRevisionId",
                table: "AppExamPaperQuestions");

            migrationBuilder.DropColumn(
                name: "ScoringDefinitionJson",
                table: "AppExamPaperQuestions");

            migrationBuilder.DropColumn(
                name: "ScoringDefinitionJson",
                table: "AppAiQuestionCandidates");
        }
    }
}
