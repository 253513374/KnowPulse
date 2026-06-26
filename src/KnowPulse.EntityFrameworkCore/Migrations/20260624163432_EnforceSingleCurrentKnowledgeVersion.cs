using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleCurrentKnowledgeVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppKnowledgeVersions_KnowledgeItemId_IsCurrentPublished",
                table: "AppKnowledgeVersions");

            migrationBuilder.CreateIndex(
                name: "IX_AppKnowledgeVersions_KnowledgeItemId_CurrentPublished",
                table: "AppKnowledgeVersions",
                columns: new[] { "KnowledgeItemId", "IsCurrentPublished" },
                unique: true,
                filter: "\"IsCurrentPublished\" AND NOT \"IsDeleted\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppKnowledgeVersions_KnowledgeItemId_CurrentPublished",
                table: "AppKnowledgeVersions");

            migrationBuilder.CreateIndex(
                name: "IX_AppKnowledgeVersions_KnowledgeItemId_IsCurrentPublished",
                table: "AppKnowledgeVersions",
                columns: new[] { "KnowledgeItemId", "IsCurrentPublished" });
        }
    }
}
