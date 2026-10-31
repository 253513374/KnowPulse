using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AllowExplicitStudentMaterialSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AppSchoolKnowledgeShares_Uses",
                table: "AppSchoolKnowledgeShares");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppSchoolKnowledgeShares_Uses",
                table: "AppSchoolKnowledgeShares",
                sql: "(\"Uses\" & 1) = 1 AND (\"Uses\" & ~63) = 0 AND ((\"Uses\" & 4) = 0 OR (\"Uses\" & 2) = 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AppSchoolKnowledgeShares_Uses",
                table: "AppSchoolKnowledgeShares");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppSchoolKnowledgeShares_Uses",
                table: "AppSchoolKnowledgeShares",
                sql: "(\"Uses\" & 1) = 1 AND (\"Uses\" & ~31) = 0 AND ((\"Uses\" & 4) = 0 OR (\"Uses\" & 2) = 2)");
        }
    }
}
