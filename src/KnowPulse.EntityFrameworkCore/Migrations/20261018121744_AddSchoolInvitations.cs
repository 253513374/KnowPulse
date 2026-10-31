using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddSchoolInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_SchoolMembers_InvitationRole",
                table: "AppSchoolMemberships",
                columns: new[] { "Id", "SchoolId", "UserId", "Role" });

            migrationBuilder.CreateTable(
                name: "AppSchoolInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvitedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EffectiveToUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CompletionOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletionFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DeclineReason = table.Column<int>(type: "integer", nullable: true),
                    RevocationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSchoolInvitations", x => x.Id);
                    table.CheckConstraint("CK_SchoolInvites_Completion", "(\"Status\" = 0 AND \"CompletedByUserId\" IS NULL AND \"CompletedAtUtc\" IS NULL AND \"CompletionOperationId\" IS NULL AND \"CompletionFingerprint\" IS NULL AND \"MembershipId\" IS NULL AND \"DeclineReason\" IS NULL AND \"RevocationReason\" IS NULL) OR (\"CompletedByUserId\" IS NOT NULL AND \"CompletedAtUtc\" IS NOT NULL AND \"CompletedAtUtc\" >= \"RecordedAtUtc\" AND \"CompletedAtUtc\" < \"ExpiresAtUtc\" AND \"CompletionOperationId\" IS NOT NULL AND \"CompletionFingerprint\" IS NOT NULL AND ((\"Status\" = 1 AND \"CompletedByUserId\" = \"TargetUserId\" AND \"MembershipId\" IS NOT NULL AND \"DeclineReason\" IS NULL AND \"RevocationReason\" IS NULL) OR (\"Status\" = 2 AND \"CompletedByUserId\" = \"TargetUserId\" AND \"MembershipId\" IS NULL AND \"DeclineReason\" IS NOT NULL AND \"DeclineReason\" IN (0, 1, 2) AND \"RevocationReason\" IS NULL) OR (\"Status\" = 3 AND \"MembershipId\" IS NULL AND \"DeclineReason\" IS NULL AND \"RevocationReason\" IS NOT NULL AND length(\"RevocationReason\") > 0)))");
                    table.CheckConstraint("CK_SchoolInvites_Fingerprints", "length(\"RequestFingerprint\") = 64 AND (\"CompletionFingerprint\" IS NULL OR length(\"CompletionFingerprint\") = 64)");
                    table.CheckConstraint("CK_SchoolInvites_Period", "\"RecordedAtUtc\" < \"ExpiresAtUtc\" AND (\"EffectiveToUtc\" IS NULL OR (\"EffectiveFromUtc\" < \"EffectiveToUtc\" AND \"RecordedAtUtc\" < \"EffectiveToUtc\"))");
                    table.CheckConstraint("CK_SchoolInvites_Role", "\"Role\" IN (1, 2) AND \"InvitedByUserId\" <> \"TargetUserId\"");
                    table.ForeignKey(
                        name: "FK_AppSchoolInvitations_AbpUsers_CompletedByUserId",
                        column: x => x.CompletedByUserId,
                        principalTable: "AbpUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppSchoolInvitations_AbpUsers_InvitedByUserId",
                        column: x => x.InvitedByUserId,
                        principalTable: "AbpUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppSchoolInvitations_AbpUsers_TargetUserId",
                        column: x => x.TargetUserId,
                        principalTable: "AbpUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppSchoolInvitations_AppSchools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "AppSchools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SchoolInvites_MembershipRecipientRole",
                        columns: x => new { x.MembershipId, x.SchoolId, x.TargetUserId, x.Role },
                        principalTable: "AppSchoolMemberships",
                        principalColumns: new[] { "Id", "SchoolId", "UserId", "Role" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppSchoolInvitations_CompletedByUserId",
                table: "AppSchoolInvitations",
                column: "CompletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AppSchoolInvitations_CompletionOperationId",
                table: "AppSchoolInvitations",
                column: "CompletionOperationId",
                unique: true,
                filter: "\"CompletionOperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppSchoolInvitations_InvitedByUserId",
                table: "AppSchoolInvitations",
                column: "InvitedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AppSchoolInvitations_MembershipId_SchoolId_TargetUserId_Role",
                table: "AppSchoolInvitations",
                columns: new[] { "MembershipId", "SchoolId", "TargetUserId", "Role" });

            migrationBuilder.CreateIndex(
                name: "IX_AppSchoolInvitations_TargetUserId",
                table: "AppSchoolInvitations",
                column: "TargetUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SchoolInvites_Manager",
                table: "AppSchoolInvitations",
                columns: new[] { "SchoolId", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SchoolInvites_Recipient",
                table: "AppSchoolInvitations",
                columns: new[] { "TenantId", "TargetUserId", "RecordedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSchoolInvitations");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_SchoolMembers_InvitationRole",
                table: "AppSchoolMemberships");
        }
    }
}
