using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class Mvp3LearningPath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppLearningPaths",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ConfigVersion = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_AppLearningPaths", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppLearningPathAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    LearningPathId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeType = table.Column<int>(type: "integer", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_AppLearningPathAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppLearningPathAssignments_AppLearningPaths_LearningPathId",
                        column: x => x.LearningPathId,
                        principalTable: "AppLearningPaths",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AppLearningPathRecommendedKnowledge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    LearningPathId = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
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
                    table.PrimaryKey("PK_AppLearningPathRecommendedKnowledge", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppLearningPathRecommendedKnowledge_AppKnowledgeItems_Knowl~",
                        column: x => x.KnowledgeItemId,
                        principalTable: "AppKnowledgeItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppLearningPathRecommendedKnowledge_AppKnowledgeVersions_Kn~",
                        column: x => x.KnowledgeVersionId,
                        principalTable: "AppKnowledgeVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppLearningPathRecommendedKnowledge_AppLearningPaths_Learni~",
                        column: x => x.LearningPathId,
                        principalTable: "AppLearningPaths",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AppLearningPathRequiredExams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    LearningPathId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    PassingRequired = table.Column<bool>(type: "boolean", nullable: false),
                    Note = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
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
                    table.PrimaryKey("PK_AppLearningPathRequiredExams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppLearningPathRequiredExams_AppExamPlans_ExamPlanId",
                        column: x => x.ExamPlanId,
                        principalTable: "AppExamPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppLearningPathRequiredExams_AppLearningPaths_LearningPathId",
                        column: x => x.LearningPathId,
                        principalTable: "AppLearningPaths",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AppLearningPathRequiredKnowledge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    LearningPathId = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
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
                    table.PrimaryKey("PK_AppLearningPathRequiredKnowledge", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppLearningPathRequiredKnowledge_AppKnowledgeItems_Knowledg~",
                        column: x => x.KnowledgeItemId,
                        principalTable: "AppKnowledgeItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppLearningPathRequiredKnowledge_AppKnowledgeVersions_Knowl~",
                        column: x => x.KnowledgeVersionId,
                        principalTable: "AppKnowledgeVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppLearningPathRequiredKnowledge_AppLearningPaths_LearningP~",
                        column: x => x.LearningPathId,
                        principalTable: "AppLearningPaths",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppLearningPathAssignments_LearningPathId_ScopeType_TargetId",
                table: "AppLearningPathAssignments",
                columns: new[] { "LearningPathId", "ScopeType", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppLearningPathAssignments_TenantId_ScopeType_TargetId_IsAc~",
                table: "AppLearningPathAssignments",
                columns: new[] { "TenantId", "ScopeType", "TargetId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_AppLearningPathRecommendedKnowledge_KnowledgeItemId",
                table: "AppLearningPathRecommendedKnowledge",
                column: "KnowledgeItemId");

            migrationBuilder.CreateIndex(
                name: "IX_AppLearningPathRecommendedKnowledge_KnowledgeVersionId",
                table: "AppLearningPathRecommendedKnowledge",
                column: "KnowledgeVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_AppLearningPathRecommendedKnowledge_LearningPathId_DisplayO~",
                table: "AppLearningPathRecommendedKnowledge",
                columns: new[] { "LearningPathId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_AppLearningPathRequiredExams_ExamPlanId",
                table: "AppLearningPathRequiredExams",
                column: "ExamPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_AppLearningPathRequiredExams_LearningPathId_DisplayOrder",
                table: "AppLearningPathRequiredExams",
                columns: new[] { "LearningPathId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_AppLearningPathRequiredKnowledge_KnowledgeItemId",
                table: "AppLearningPathRequiredKnowledge",
                column: "KnowledgeItemId");

            migrationBuilder.CreateIndex(
                name: "IX_AppLearningPathRequiredKnowledge_KnowledgeVersionId",
                table: "AppLearningPathRequiredKnowledge",
                column: "KnowledgeVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_AppLearningPathRequiredKnowledge_LearningPathId_DisplayOrder",
                table: "AppLearningPathRequiredKnowledge",
                columns: new[] { "LearningPathId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_AppLearningPaths_Name",
                table: "AppLearningPaths",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_AppLearningPaths_TenantId_IsActive_CreationTime",
                table: "AppLearningPaths",
                columns: new[] { "TenantId", "IsActive", "CreationTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppLearningPathAssignments");

            migrationBuilder.DropTable(
                name: "AppLearningPathRecommendedKnowledge");

            migrationBuilder.DropTable(
                name: "AppLearningPathRequiredExams");

            migrationBuilder.DropTable(
                name: "AppLearningPathRequiredKnowledge");

            migrationBuilder.DropTable(
                name: "AppLearningPaths");
        }
    }
}
