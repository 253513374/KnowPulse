using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddSchoolKnowledgeSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_AppKnowledgeVersions_Id_KnowledgeItemId",
                table: "AppKnowledgeVersions",
                columns: new[] { "Id", "KnowledgeItemId" });

            migrationBuilder.CreateTable(
                name: "AppSchoolKnowledgeShares",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LibraryId = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Summary = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Uses = table.Column<int>(type: "integer", nullable: false),
                    IsRevoked = table.Column<bool>(type: "boolean", nullable: false),
                    SharedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
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
                    table.PrimaryKey("PK_AppSchoolKnowledgeShares", x => x.Id);
                    table.CheckConstraint("CK_AppSchoolKnowledgeShares_Uses", "(\"Uses\" & 1) = 1 AND (\"Uses\" & ~31) = 0 AND ((\"Uses\" & 4) = 0 OR (\"Uses\" & 2) = 2)");
                    table.ForeignKey(
                        name: "FK_AppSchoolKnowledgeShares_AppKnowledgeItems_KnowledgeItemId",
                        column: x => x.KnowledgeItemId,
                        principalTable: "AppKnowledgeItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppSchoolKnowledgeShares_AppKnowledgeLibraries_LibraryId_Ow~",
                        columns: x => new { x.LibraryId, x.OwnerUserId },
                        principalTable: "AppKnowledgeLibraries",
                        principalColumns: new[] { "Id", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppSchoolKnowledgeShares_AppKnowledgeVersions_KnowledgeVers~",
                        columns: x => new { x.KnowledgeVersionId, x.KnowledgeItemId },
                        principalTable: "AppKnowledgeVersions",
                        principalColumns: new[] { "Id", "KnowledgeItemId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppSchoolKnowledgeShares_AppSchools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "AppSchools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppSchoolKnowledgeShares_KnowledgeItemId_KnowledgeVersionId~",
                table: "AppSchoolKnowledgeShares",
                columns: new[] { "KnowledgeItemId", "KnowledgeVersionId", "SchoolId" },
                unique: true,
                filter: "NOT \"IsDeleted\" AND NOT \"IsRevoked\"");

            migrationBuilder.CreateIndex(
                name: "IX_AppSchoolKnowledgeShares_KnowledgeVersionId_KnowledgeItemId",
                table: "AppSchoolKnowledgeShares",
                columns: new[] { "KnowledgeVersionId", "KnowledgeItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppSchoolKnowledgeShares_LibraryId_OwnerUserId",
                table: "AppSchoolKnowledgeShares",
                columns: new[] { "LibraryId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppSchoolKnowledgeShares_SchoolId",
                table: "AppSchoolKnowledgeShares",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_AppSchoolKnowledgeShares_TenantId_OwnerUserId_SharedAtUtc",
                table: "AppSchoolKnowledgeShares",
                columns: new[] { "TenantId", "OwnerUserId", "SharedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppSchoolKnowledgeShares_TenantId_SchoolId_IsRevoked",
                table: "AppSchoolKnowledgeShares",
                columns: new[] { "TenantId", "SchoolId", "IsRevoked" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSchoolKnowledgeShares");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_AppKnowledgeVersions_Id_KnowledgeItemId",
                table: "AppKnowledgeVersions");
        }
    }
}
