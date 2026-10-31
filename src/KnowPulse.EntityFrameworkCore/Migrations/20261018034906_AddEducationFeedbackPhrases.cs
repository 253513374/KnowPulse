using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddEducationFeedbackPhrases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppEducationFeedbackPhraseRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PhraseId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    BasedOnRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationFeedbackPhraseRevisions", x => x.Id);
                    table.UniqueConstraint("AK_EduPhraseVersions_Owner", x => new { x.Id, x.PhraseId, x.OwnerUserId });
                    table.CheckConstraint("CK_EduPhraseVersions_Revision", "\"RevisionNumber\" > 0");
                    table.ForeignKey(
                        name: "FK_EduPhraseVersions_Previous",
                        columns: x => new { x.BasedOnRevisionId, x.PhraseId, x.OwnerUserId },
                        principalTable: "AppEducationFeedbackPhraseRevisions",
                        principalColumns: new[] { "Id", "PhraseId", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationFeedbackPhrases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_AppEducationFeedbackPhrases", x => x.Id);
                    table.UniqueConstraint("AK_EduPhrases_Owner", x => new { x.Id, x.OwnerUserId });
                    table.CheckConstraint("CK_EduPhrases_Revision", "(\"RevisionNumber\" = 0 AND \"CurrentRevisionId\" IS NULL AND NOT \"IsArchived\") OR (\"RevisionNumber\" > 0 AND \"CurrentRevisionId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_EduPhrases_Current",
                        columns: x => new { x.CurrentRevisionId, x.Id, x.OwnerUserId },
                        principalTable: "AppEducationFeedbackPhraseRevisions",
                        principalColumns: new[] { "Id", "PhraseId", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationFeedbackPhraseRevisions_BasedOnRevisionId_Phras~",
                table: "AppEducationFeedbackPhraseRevisions",
                columns: new[] { "BasedOnRevisionId", "PhraseId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationFeedbackPhraseRevisions_PhraseId_OwnerUserId",
                table: "AppEducationFeedbackPhraseRevisions",
                columns: new[] { "PhraseId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduPhraseVersions_Sequence",
                table: "AppEducationFeedbackPhraseRevisions",
                columns: new[] { "PhraseId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationFeedbackPhrases_CurrentRevisionId_Id_OwnerUserId",
                table: "AppEducationFeedbackPhrases",
                columns: new[] { "CurrentRevisionId", "Id", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduPhrases_Owner",
                table: "AppEducationFeedbackPhrases",
                columns: new[] { "TenantId", "OwnerUserId", "IsArchived" });

            migrationBuilder.AddForeignKey(
                name: "FK_EduPhraseVersions_Root",
                table: "AppEducationFeedbackPhraseRevisions",
                columns: new[] { "PhraseId", "OwnerUserId" },
                principalTable: "AppEducationFeedbackPhrases",
                principalColumns: new[] { "Id", "OwnerUserId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EduPhraseVersions_Root",
                table: "AppEducationFeedbackPhraseRevisions");

            migrationBuilder.DropTable(
                name: "AppEducationFeedbackPhrases");

            migrationBuilder.DropTable(
                name: "AppEducationFeedbackPhraseRevisions");
        }
    }
}
