using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class Mvp2QuestionBank : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    CreationSource = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Stem = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    ApplicablePositionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SecurityLevel = table.Column<int>(type: "integer", nullable: false),
                    Tags = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SubmittedTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ReviewedTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_AppQuestions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppQuestionAnalyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    QuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
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
                    table.PrimaryKey("PK_AppQuestionAnalyses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppQuestionAnalyses_AppQuestions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "AppQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AppQuestionAnswers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    QuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Value = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_AppQuestionAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppQuestionAnswers_AppQuestions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "AppQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AppQuestionKnowledgePoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    QuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
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
                    table.PrimaryKey("PK_AppQuestionKnowledgePoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppQuestionKnowledgePoints_AppQuestions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "AppQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AppQuestionOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    QuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OptionKey = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Content = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_AppQuestionOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppQuestionOptions_AppQuestions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "AppQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AppQuestionReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    QuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    FromStatus = table.Column<int>(type: "integer", nullable: false),
                    ToStatus = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ReviewTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppQuestionReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppQuestionReviews_AppQuestions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "AppQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AppQuestionSimilarityChecks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    QuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SimilarQuestionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SimilarityScore = table.Column<double>(type: "double precision", nullable: false),
                    Summary = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppQuestionSimilarityChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppQuestionSimilarityChecks_AppQuestions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "AppQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AppQuestionSimilarityChecks_AppQuestions_SimilarQuestionId",
                        column: x => x.SimilarQuestionId,
                        principalTable: "AppQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppQuestionSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    QuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    KnowledgeVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChunkId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceTitle = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SourceExcerpt = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SourceHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ManualSourceDescription = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
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
                    table.PrimaryKey("PK_AppQuestionSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppQuestionSources_AppQuestions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "AppQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppQuestionAnalyses_QuestionId",
                table: "AppQuestionAnalyses",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_AppQuestionAnswers_QuestionId_DisplayOrder",
                table: "AppQuestionAnswers",
                columns: new[] { "QuestionId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_AppQuestionKnowledgePoints_QuestionId_Name",
                table: "AppQuestionKnowledgePoints",
                columns: new[] { "QuestionId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_AppQuestionKnowledgePoints_TenantId_Name",
                table: "AppQuestionKnowledgePoints",
                columns: new[] { "TenantId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_AppQuestionOptions_QuestionId_DisplayOrder",
                table: "AppQuestionOptions",
                columns: new[] { "QuestionId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_AppQuestionOptions_QuestionId_OptionKey",
                table: "AppQuestionOptions",
                columns: new[] { "QuestionId", "OptionKey" });

            migrationBuilder.CreateIndex(
                name: "IX_AppQuestionReviews_QuestionId_CreationTime",
                table: "AppQuestionReviews",
                columns: new[] { "QuestionId", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AppQuestionReviews_ReviewerUserId_ReviewTime",
                table: "AppQuestionReviews",
                columns: new[] { "ReviewerUserId", "ReviewTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AppQuestions_ApplicablePositionId",
                table: "AppQuestions",
                column: "ApplicablePositionId");

            migrationBuilder.CreateIndex(
                name: "IX_AppQuestions_CreationSource",
                table: "AppQuestions",
                column: "CreationSource");

            migrationBuilder.CreateIndex(
                name: "IX_AppQuestions_TenantId_Status_Type",
                table: "AppQuestions",
                columns: new[] { "TenantId", "Status", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_AppQuestionSimilarityChecks_QuestionId_CreationTime",
                table: "AppQuestionSimilarityChecks",
                columns: new[] { "QuestionId", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AppQuestionSimilarityChecks_SimilarQuestionId",
                table: "AppQuestionSimilarityChecks",
                column: "SimilarQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_AppQuestionSources_ChunkId",
                table: "AppQuestionSources",
                column: "ChunkId");

            migrationBuilder.CreateIndex(
                name: "IX_AppQuestionSources_KnowledgeItemId",
                table: "AppQuestionSources",
                column: "KnowledgeItemId");

            migrationBuilder.CreateIndex(
                name: "IX_AppQuestionSources_KnowledgeVersionId",
                table: "AppQuestionSources",
                column: "KnowledgeVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_AppQuestionSources_QuestionId",
                table: "AppQuestionSources",
                column: "QuestionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppQuestionAnalyses");

            migrationBuilder.DropTable(
                name: "AppQuestionAnswers");

            migrationBuilder.DropTable(
                name: "AppQuestionKnowledgePoints");

            migrationBuilder.DropTable(
                name: "AppQuestionOptions");

            migrationBuilder.DropTable(
                name: "AppQuestionReviews");

            migrationBuilder.DropTable(
                name: "AppQuestionSimilarityChecks");

            migrationBuilder.DropTable(
                name: "AppQuestionSources");

            migrationBuilder.DropTable(
                name: "AppQuestions");
        }
    }
}
