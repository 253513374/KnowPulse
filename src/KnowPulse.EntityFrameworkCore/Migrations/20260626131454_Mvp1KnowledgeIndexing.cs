using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <summary>
    /// 创建 MVP-1 知识索引相关表。
    /// 包含模型版本、知识分块、embedding 主库追踪记录和索引任务四类业务事实。
    /// </summary>
    public partial class Mvp1KnowledgeIndexing : Migration
    {
        /// <summary>
        /// 建立索引任务和 embedding 追踪所需表结构及查询索引。
        /// 真实向量保存在 Qdrant，本迁移只保存可审计、可重建、可删除的主库元数据。
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 模型版本表用于解释每条 embedding 记录的 provider、模型名、模型版本、维度和 collection。
            migrationBuilder.CreateTable(
                name: "AppEmbeddingModelVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ModelName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ModelVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VectorDimension = table.Column<int>(type: "integer", nullable: false),
                    CollectionName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
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
                    table.PrimaryKey("PK_AppEmbeddingModelVersions", x => x.Id);
                });

            // 知识分块表保存 RAG 检索和 citation 的最小可追溯单元。
            migrationBuilder.CreateTable(
                name: "AppKnowledgeChunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    KnowledgeItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeIndexJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SourceTitle = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SourcePage = table.Column<int>(type: "integer", nullable: true),
                    TokenCount = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsPublishedSnapshot = table.Column<bool>(type: "boolean", nullable: false),
                    SecurityLevelSnapshot = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_AppKnowledgeChunks", x => x.Id);
                });

            // embedding 记录表不保存真实向量，只保存外部向量记录 ID 和内容/model 元数据。
            migrationBuilder.CreateTable(
                name: "AppKnowledgeEmbeddingRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    KnowledgeChunkId = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmbeddingModelVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    VectorStoreRecordId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    VectorDimension = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IndexedTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DeletedTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
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
                    table.PrimaryKey("PK_AppKnowledgeEmbeddingRecords", x => x.Id);
                });

            // 索引任务表用于追踪发布、重建、权限变化、模型变化等触发源和失败排障信息。
            migrationBuilder.CreateTable(
                name: "AppKnowledgeIndexJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    KnowledgeItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmbeddingModelVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    TriggerType = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    MaxRetryCount = table.Column<int>(type: "integer", nullable: false),
                    FailureStage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    StartedTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CompletedTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    NextRetryTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
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
                    table.PrimaryKey("PK_AppKnowledgeIndexJobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppEmbeddingModelVersions_CollectionName",
                table: "AppEmbeddingModelVersions",
                column: "CollectionName");

            migrationBuilder.CreateIndex(
                name: "IX_AppEmbeddingModelVersions_TenantId_IsDefault_IsActive",
                table: "AppEmbeddingModelVersions",
                columns: new[] { "TenantId", "IsDefault", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEmbeddingModelVersions_TenantId_Provider_ModelName_Model~",
                table: "AppEmbeddingModelVersions",
                columns: new[] { "TenantId", "Provider", "ModelName", "ModelVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_AppKnowledgeChunks_ContentHash",
                table: "AppKnowledgeChunks",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_AppKnowledgeChunks_KnowledgeIndexJobId_Sequence",
                table: "AppKnowledgeChunks",
                columns: new[] { "KnowledgeIndexJobId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppKnowledgeChunks_KnowledgeVersionId_IsActive",
                table: "AppKnowledgeChunks",
                columns: new[] { "KnowledgeVersionId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_AppKnowledgeChunks_TenantId_IsActive_IsPublishedSnapshot",
                table: "AppKnowledgeChunks",
                columns: new[] { "TenantId", "IsActive", "IsPublishedSnapshot" });

            migrationBuilder.CreateIndex(
                name: "IX_AppKnowledgeEmbeddingRecords_Chunk_ModelVersion_Active",
                table: "AppKnowledgeEmbeddingRecords",
                columns: new[] { "KnowledgeChunkId", "EmbeddingModelVersionId" },
                unique: true,
                filter: "\"IsActive\" AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_AppKnowledgeEmbeddingRecords_EmbeddingModelVersionId_Vector~",
                table: "AppKnowledgeEmbeddingRecords",
                columns: new[] { "EmbeddingModelVersionId", "VectorStoreRecordId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppKnowledgeEmbeddingRecords_KnowledgeVersionId_IsActive",
                table: "AppKnowledgeEmbeddingRecords",
                columns: new[] { "KnowledgeVersionId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_AppKnowledgeIndexJobs_EmbeddingModelVersionId",
                table: "AppKnowledgeIndexJobs",
                column: "EmbeddingModelVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_AppKnowledgeIndexJobs_KnowledgeItemId_Status",
                table: "AppKnowledgeIndexJobs",
                columns: new[] { "KnowledgeItemId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AppKnowledgeIndexJobs_NextRetryTime",
                table: "AppKnowledgeIndexJobs",
                column: "NextRetryTime");

            migrationBuilder.CreateIndex(
                name: "IX_AppKnowledgeIndexJobs_TenantId_KnowledgeVersionId_Status",
                table: "AppKnowledgeIndexJobs",
                columns: new[] { "TenantId", "KnowledgeVersionId", "Status" });
        }

        /// <summary>
        /// 回滚 MVP-1 知识索引表。
        /// 按无外键依赖的建表方式逐表删除，保持迁移回滚简单可控。
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppEmbeddingModelVersions");

            migrationBuilder.DropTable(
                name: "AppKnowledgeChunks");

            migrationBuilder.DropTable(
                name: "AppKnowledgeEmbeddingRecords");

            migrationBuilder.DropTable(
                name: "AppKnowledgeIndexJobs");
        }
    }
}
