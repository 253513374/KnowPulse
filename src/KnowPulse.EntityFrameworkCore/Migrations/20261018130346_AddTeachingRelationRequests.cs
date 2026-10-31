using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddTeachingRelationRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppTeachingRelationRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeacherUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalAssignmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    OriginalAssignmentStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    OriginalEndsAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    RequestText = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Resolution = table.Column<int>(type: "integer", nullable: true),
                    ResolutionText = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ResultingAssignmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    EndedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletionOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletionFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppTeachingRelationRequests", x => x.Id);
                    table.CheckConstraint("CK_RelationRequests_Completion", "(\"Status\" = 0 AND \"CompletedByUserId\" IS NULL AND \"CompletionOperationId\" IS NULL AND \"CompletionFingerprint\" IS NULL AND \"CompletedAtUtc\" IS NULL AND \"Resolution\" IS NULL AND \"ResolutionText\" IS NULL AND \"ResultingAssignmentId\" IS NULL AND \"EndedAtUtc\" IS NULL) OR (\"CompletedByUserId\" IS NOT NULL AND \"CompletionOperationId\" IS NOT NULL AND \"CompletionFingerprint\" IS NOT NULL AND \"CompletedAtUtc\" IS NOT NULL AND \"CompletedAtUtc\" >= \"RecordedAtUtc\" AND ((\"Status\" = 2 AND \"CompletedByUserId\" = \"TeacherUserId\" AND \"Resolution\" IS NULL AND \"ResolutionText\" IS NULL AND \"ResultingAssignmentId\" IS NULL AND \"EndedAtUtc\" IS NULL) OR (\"Status\" = 1 AND \"Resolution\" IS NOT NULL AND \"ResolutionText\" IS NOT NULL AND length(\"ResolutionText\") > 0 AND ((\"Resolution\" = 0 AND \"ResultingAssignmentId\" IS NULL AND \"EndedAtUtc\" IS NULL) OR (\"Resolution\" = 1 AND \"OriginalAssignmentId\" IS NOT NULL AND \"ResultingAssignmentId\" IS NULL AND \"EndedAtUtc\" IS NOT NULL) OR (\"Resolution\" = 2 AND \"OriginalAssignmentId\" IS NULL AND \"ResultingAssignmentId\" IS NOT NULL AND \"EndedAtUtc\" IS NULL) OR (\"Resolution\" = 3 AND \"OriginalAssignmentId\" IS NOT NULL AND \"ResultingAssignmentId\" IS NOT NULL AND \"OriginalAssignmentId\" <> \"ResultingAssignmentId\" AND \"EndedAtUtc\" IS NOT NULL)))) )");
                    table.CheckConstraint("CK_RelationRequests_EndTime", "\"EndedAtUtc\" IS NULL OR (\"CompletedAtUtc\" IS NOT NULL AND \"EndedAtUtc\" >= \"CompletedAtUtc\")");
                    table.CheckConstraint("CK_RelationRequests_Fingerprints", "length(\"RequestFingerprint\") = 64 AND (\"CompletionFingerprint\" IS NULL OR length(\"CompletionFingerprint\") = 64)");
                    table.CheckConstraint("CK_RelationRequests_Original", "(\"OriginalAssignmentId\" IS NULL AND \"OriginalAssignmentStamp\" IS NULL AND \"OriginalEndsAtUtc\" IS NULL) OR (\"OriginalAssignmentId\" IS NOT NULL AND \"OriginalAssignmentStamp\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_AppTeachingRelationRequests_AbpUsers_CompletedByUserId",
                        column: x => x.CompletedByUserId,
                        principalTable: "AbpUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppTeachingRelationRequests_AppSchoolMemberships_SchoolMemb~",
                        columns: x => new { x.SchoolMembershipId, x.SchoolId, x.TeacherUserId },
                        principalTable: "AppSchoolMemberships",
                        principalColumns: new[] { "Id", "SchoolId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RelationRequests_OriginalAssignmentId",
                        columns: x => new { x.OriginalAssignmentId, x.SchoolId, x.TeacherUserId },
                        principalTable: "AppTeachingAssignments",
                        principalColumns: new[] { "Id", "SchoolId", "TeacherUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RelationRequests_ResultingAssignmentId",
                        columns: x => new { x.ResultingAssignmentId, x.SchoolId, x.TeacherUserId },
                        principalTable: "AppTeachingAssignments",
                        principalColumns: new[] { "Id", "SchoolId", "TeacherUserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppTeachingRelationRequests_CompletedByUserId",
                table: "AppTeachingRelationRequests",
                column: "CompletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AppTeachingRelationRequests_CompletionOperationId",
                table: "AppTeachingRelationRequests",
                column: "CompletionOperationId",
                unique: true,
                filter: "\"CompletionOperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppTeachingRelationRequests_OriginalAssignmentId_SchoolId_T~",
                table: "AppTeachingRelationRequests",
                columns: new[] { "OriginalAssignmentId", "SchoolId", "TeacherUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppTeachingRelationRequests_ResultingAssignmentId_SchoolId_~",
                table: "AppTeachingRelationRequests",
                columns: new[] { "ResultingAssignmentId", "SchoolId", "TeacherUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppTeachingRelationRequests_SchoolMembershipId_SchoolId_Tea~",
                table: "AppTeachingRelationRequests",
                columns: new[] { "SchoolMembershipId", "SchoolId", "TeacherUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_RelationRequests_School",
                table: "AppTeachingRelationRequests",
                columns: new[] { "SchoolId", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RelationRequests_Teacher",
                table: "AppTeachingRelationRequests",
                columns: new[] { "TenantId", "TeacherUserId", "SchoolId", "RecordedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppTeachingRelationRequests");
        }
    }
}
