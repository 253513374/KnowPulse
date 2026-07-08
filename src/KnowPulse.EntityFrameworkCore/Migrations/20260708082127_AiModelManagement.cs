using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AiModelManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppAiModelDeployments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Alias = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProviderType = table.Column<int>(type: "integer", nullable: false),
                    Capability = table.Column<int>(type: "integer", nullable: false),
                    UpstreamModelName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ModelVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ApiKeyEnvironmentVariable = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    BaseUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ContextWindowTokens = table.Column<int>(type: "integer", nullable: true),
                    MaxOutputTokens = table.Column<int>(type: "integer", nullable: true),
                    EmbeddingDimension = table.Column<int>(type: "integer", nullable: true),
                    PromptTokenPriceUsd = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    CompletionTokenPriceUsd = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
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
                    table.PrimaryKey("PK_AppAiModelDeployments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppAiModelHealthSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeploymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CheckedTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    LatencyMs = table.Column<int>(type: "integer", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    GatewayModelInfoJson = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
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
                    table.PrimaryKey("PK_AppAiModelHealthSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppAiModelHealthSnapshots_AppAiModelDeployments_DeploymentId",
                        column: x => x.DeploymentId,
                        principalTable: "AppAiModelDeployments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppAiModelUsageBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Usage = table.Column<int>(type: "integer", nullable: false),
                    DeploymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessModelVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MaxOutputTokens = table.Column<int>(type: "integer", nullable: false),
                    Temperature = table.Column<double>(type: "double precision", nullable: false),
                    FallbackAliasesJson = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
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
                    table.PrimaryKey("PK_AppAiModelUsageBindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppAiModelUsageBindings_AppAiModelDeployments_DeploymentId",
                        column: x => x.DeploymentId,
                        principalTable: "AppAiModelDeployments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppAiModelDeployments_ProviderType_IsActive",
                table: "AppAiModelDeployments",
                columns: new[] { "ProviderType", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_AppAiModelDeployments_TenantId_Alias",
                table: "AppAiModelDeployments",
                columns: new[] { "TenantId", "Alias" });

            migrationBuilder.CreateIndex(
                name: "IX_AppAiModelDeployments_TenantId_Capability_IsActive",
                table: "AppAiModelDeployments",
                columns: new[] { "TenantId", "Capability", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_AppAiModelHealthSnapshots_DeploymentId_CheckedTime",
                table: "AppAiModelHealthSnapshots",
                columns: new[] { "DeploymentId", "CheckedTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AppAiModelHealthSnapshots_TenantId_Status_CheckedTime",
                table: "AppAiModelHealthSnapshots",
                columns: new[] { "TenantId", "Status", "CheckedTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AppAiModelUsageBindings_DeploymentId",
                table: "AppAiModelUsageBindings",
                column: "DeploymentId");

            migrationBuilder.CreateIndex(
                name: "IX_AppAiModelUsageBindings_TenantId_Usage_IsActive",
                table: "AppAiModelUsageBindings",
                columns: new[] { "TenantId", "Usage", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppAiModelHealthSnapshots");

            migrationBuilder.DropTable(
                name: "AppAiModelUsageBindings");

            migrationBuilder.DropTable(
                name: "AppAiModelDeployments");
        }
    }
}
