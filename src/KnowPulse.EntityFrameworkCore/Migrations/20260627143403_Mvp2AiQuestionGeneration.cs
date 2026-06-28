using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class Mvp2AiQuestionGeneration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppAiQuestionPromptTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TemplateKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SystemPrompt = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    UserPromptTemplate = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_AppAiQuestionPromptTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppAiQuestionGenerationTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PromptTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    PromptVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ModelProvider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ModelName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ModelVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    RequestedCount = table.Column<int>(type: "integer", nullable: false),
                    GeneratedCount = table.Column<int>(type: "integer", nullable: false),
                    TopK = table.Column<int>(type: "integer", nullable: false),
                    KnowledgeScope = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RetrievalQuery = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ApplicablePositionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SecurityLevel = table.Column<int>(type: "integer", nullable: false),
                    Tags = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    StartedTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CompletedTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    FailureStage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
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
                    table.PrimaryKey("PK_AppAiQuestionGenerationTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppAiQuestionGenerationTasks_AppAiQuestionPromptTemplates_P~",
                        column: x => x.PromptTemplateId,
                        principalTable: "AppAiQuestionPromptTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppAiQuestionCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Stem = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    ApplicablePositionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SecurityLevel = table.Column<int>(type: "integer", nullable: false),
                    Tags = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Analysis = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    OptionsJson = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    AnswersJson = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    KnowledgePointsJson = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    ValidationErrors = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    LinkedQuestionId = table.Column<Guid>(type: "uuid", nullable: true),
                    HighestSimilarQuestionId = table.Column<Guid>(type: "uuid", nullable: true),
                    HighestSimilarityScore = table.Column<double>(type: "double precision", nullable: true),
                    SimilaritySummary = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
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
                    table.PrimaryKey("PK_AppAiQuestionCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppAiQuestionCandidates_AppAiQuestionGenerationTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "AppAiQuestionGenerationTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AppAiQuestionCandidates_AppQuestions_HighestSimilarQuestion~",
                        column: x => x.HighestSimilarQuestionId,
                        principalTable: "AppQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppAiQuestionCandidates_AppQuestions_LinkedQuestionId",
                        column: x => x.LinkedQuestionId,
                        principalTable: "AppQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppAiQuestionModelCallLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    PromptTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    PromptVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    QueryHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    QuerySummary = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ModelProvider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ModelName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ModelVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TopK = table.Column<int>(type: "integer", nullable: false),
                    RetrievedChunkCount = table.Column<int>(type: "integer", nullable: false),
                    CandidateCount = table.Column<int>(type: "integer", nullable: false),
                    PromptTokenCount = table.Column<int>(type: "integer", nullable: false),
                    CompletionTokenCount = table.Column<int>(type: "integer", nullable: false),
                    TotalTokenCount = table.Column<int>(type: "integer", nullable: false),
                    ExecutionDurationMs = table.Column<int>(type: "integer", nullable: false),
                    FailureStage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppAiQuestionModelCallLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppAiQuestionModelCallLogs_AppAiQuestionGenerationTasks_Tas~",
                        column: x => x.TaskId,
                        principalTable: "AppAiQuestionGenerationTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AppAiQuestionModelCallLogs_AppAiQuestionPromptTemplates_Pro~",
                        column: x => x.PromptTemplateId,
                        principalTable: "AppAiQuestionPromptTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppAiQuestionCandidateCitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChunkId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: false),
                    SourceTitle = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SourcePage = table.Column<int>(type: "integer", nullable: true),
                    SourceExcerpt = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SourceHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
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
                    table.PrimaryKey("PK_AppAiQuestionCandidateCitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppAiQuestionCandidateCitations_AppAiQuestionCandidates_Can~",
                        column: x => x.CandidateId,
                        principalTable: "AppAiQuestionCandidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppAiQuestionCandidateCitations_CandidateId_Rank",
                table: "AppAiQuestionCandidateCitations",
                columns: new[] { "CandidateId", "Rank" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppAiQuestionCandidateCitations_KnowledgeItemId",
                table: "AppAiQuestionCandidateCitations",
                column: "KnowledgeItemId");

            migrationBuilder.CreateIndex(
                name: "IX_AppAiQuestionCandidateCitations_KnowledgeVersionId_ChunkId",
                table: "AppAiQuestionCandidateCitations",
                columns: new[] { "KnowledgeVersionId", "ChunkId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppAiQuestionCandidates_HighestSimilarQuestionId",
                table: "AppAiQuestionCandidates",
                column: "HighestSimilarQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_AppAiQuestionCandidates_LinkedQuestionId",
                table: "AppAiQuestionCandidates",
                column: "LinkedQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_AppAiQuestionCandidates_Status_Type_Difficulty",
                table: "AppAiQuestionCandidates",
                columns: new[] { "Status", "Type", "Difficulty" });

            migrationBuilder.CreateIndex(
                name: "IX_AppAiQuestionCandidates_TaskId_CreationTime",
                table: "AppAiQuestionCandidates",
                columns: new[] { "TaskId", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AppAiQuestionGenerationTasks_PromptTemplateId",
                table: "AppAiQuestionGenerationTasks",
                column: "PromptTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_AppAiQuestionGenerationTasks_TenantId_Status_CreationTime",
                table: "AppAiQuestionGenerationTasks",
                columns: new[] { "TenantId", "Status", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AppAiQuestionGenerationTasks_Type_Difficulty",
                table: "AppAiQuestionGenerationTasks",
                columns: new[] { "Type", "Difficulty" });

            migrationBuilder.CreateIndex(
                name: "IX_AppAiQuestionModelCallLogs_PromptTemplateId",
                table: "AppAiQuestionModelCallLogs",
                column: "PromptTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_AppAiQuestionModelCallLogs_Status_CreationTime",
                table: "AppAiQuestionModelCallLogs",
                columns: new[] { "Status", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AppAiQuestionModelCallLogs_TaskId_CreationTime",
                table: "AppAiQuestionModelCallLogs",
                columns: new[] { "TaskId", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AppAiQuestionPromptTemplates_TenantId_TemplateKey_IsActive",
                table: "AppAiQuestionPromptTemplates",
                columns: new[] { "TenantId", "TemplateKey", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_AppAiQuestionPromptTemplates_TenantId_TemplateKey_Version",
                table: "AppAiQuestionPromptTemplates",
                columns: new[] { "TenantId", "TemplateKey", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppAiQuestionCandidateCitations");

            migrationBuilder.DropTable(
                name: "AppAiQuestionModelCallLogs");

            migrationBuilder.DropTable(
                name: "AppAiQuestionCandidates");

            migrationBuilder.DropTable(
                name: "AppAiQuestionGenerationTasks");

            migrationBuilder.DropTable(
                name: "AppAiQuestionPromptTemplates");
        }
    }
}
