using System;
using KnowPulse.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <summary>
    /// 创建 RAG 调用审计表和 citation 链路表。
    /// 审计表只保存问题摘要、hash、模型元数据和短摘录，不保存完整 prompt 或完整知识正文。
    /// </summary>
    [DbContext(typeof(KnowPulseDbContext))]
    [Migration("20260627043000_Mvp1RagAuditLogs")]
    public partial class Mvp1RagAuditLogs : Migration
    {
        /// <summary>
        /// 建立 RAG 调用日志、citation 记录以及常用审计查询索引。
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 调用日志表保存一次 Search/Answer 的状态、模型、token、耗时和失败摘要。
            migrationBuilder.CreateTable(
                name: "AppRagCallLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CallType = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    QueryHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    QuerySummary = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ModelProvider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ModelName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ModelVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CollectionName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TopK = table.Column<int>(type: "integer", nullable: false),
                    RetrievedChunkCount = table.Column<int>(type: "integer", nullable: false),
                    CitationCount = table.Column<int>(type: "integer", nullable: false),
                    PromptTokenCount = table.Column<int>(type: "integer", nullable: false),
                    CompletionTokenCount = table.Column<int>(type: "integer", nullable: false),
                    TotalTokenCount = table.Column<int>(type: "integer", nullable: false),
                    EstimatedCostUsd = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: true),
                    NeedHumanReview = table.Column<bool>(type: "boolean", nullable: false),
                    ExecutionDurationMs = table.Column<int>(type: "integer", nullable: false),
                    FailureStage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppRagCallLogs", x => x.Id);
                });

            // citation 表把一次 RAG 调用关联回知识条目、版本和 chunk，并只保存短摘录。
            migrationBuilder.CreateTable(
                name: "AppRagCitationRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    RagCallLogId = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChunkId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: false),
                    SourceTitle = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SourcePage = table.Column<int>(type: "integer", nullable: true),
                    QuoteHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    QuotePreview = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppRagCitationRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppRagCitationRecords_AppRagCallLogs_RagCallLogId",
                        column: x => x.RagCallLogId,
                        principalTable: "AppRagCallLogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppRagCallLogs_CallType_Status_CreationTime",
                table: "AppRagCallLogs",
                columns: new[] { "CallType", "Status", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AppRagCallLogs_TenantId_ModelVersion_CreationTime",
                table: "AppRagCallLogs",
                columns: new[] { "TenantId", "ModelVersion", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AppRagCallLogs_TenantId_UserId_CreationTime",
                table: "AppRagCallLogs",
                columns: new[] { "TenantId", "UserId", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AppRagCitationRecords_KnowledgeItemId",
                table: "AppRagCitationRecords",
                column: "KnowledgeItemId");

            migrationBuilder.CreateIndex(
                name: "IX_AppRagCitationRecords_KnowledgeVersionId_ChunkId",
                table: "AppRagCitationRecords",
                columns: new[] { "KnowledgeVersionId", "ChunkId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppRagCitationRecords_RagCallLogId_Rank",
                table: "AppRagCitationRecords",
                columns: new[] { "RagCallLogId", "Rank" },
                unique: true);
        }

        /// <summary>
        /// 回滚 RAG 审计表。先删 citation，再删调用日志，避免外键约束冲突。
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppRagCitationRecords");

            migrationBuilder.DropTable(
                name: "AppRagCallLogs");
        }
    }
}
