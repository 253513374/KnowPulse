using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddEducationStudentGrowthSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppEducationStudentGrowthSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademicTermId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolClassId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeachingAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeacherUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EducationSubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TextbookVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContextId = table.Column<Guid>(type: "uuid", nullable: false),
                    WindowStartsAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    WindowEndsAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CalculatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    SourceFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResultJson = table.Column<string>(type: "text", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationStudentGrowthSnapshots", x => x.Id);
                    table.CheckConstraint("CK_EduGrowth_ResultSize", "length(\"ResultJson\") BETWEEN 1 AND 8388608");
                    table.CheckConstraint("CK_EduGrowth_Window", "\"WindowStartsAtUtc\" < \"WindowEndsAtUtc\" AND \"WindowEndsAtUtc\" <= \"CalculatedAtUtc\"");
                    table.ForeignKey(
                        name: "FK_EduGrowth_Book",
                        columns: x => new { x.TextbookVersionId, x.SchoolId, x.EducationSubjectId },
                        principalTable: "AppTextbookVersions",
                        principalColumns: new[] { "Id", "SchoolId", "EducationSubjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduGrowth_Enrollment",
                        columns: x => new { x.StudentEnrollmentId, x.SchoolId, x.SchoolClassId, x.StudentUserId },
                        principalTable: "AppStudentEnrollments",
                        principalColumns: new[] { "Id", "SchoolId", "SchoolClassId", "StudentUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduGrowth_Membership",
                        columns: x => new { x.SchoolMembershipId, x.SchoolId, x.StudentUserId },
                        principalTable: "AppSchoolMemberships",
                        principalColumns: new[] { "Id", "SchoolId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduGrowth_Teaching",
                        columns: x => new { x.TeachingAssignmentId, x.SchoolId, x.AcademicTermId, x.SchoolClassId, x.TeacherUserId, x.EducationSubjectId },
                        principalTable: "AppTeachingAssignments",
                        principalColumns: new[] { "Id", "SchoolId", "AcademicTermId", "SchoolClassId", "TeacherUserId", "EducationSubjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudentGrowthSnapshots_SchoolMembershipId_Schoo~",
                table: "AppEducationStudentGrowthSnapshots",
                columns: new[] { "SchoolMembershipId", "SchoolId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudentGrowthSnapshots_StudentEnrollmentId_Scho~",
                table: "AppEducationStudentGrowthSnapshots",
                columns: new[] { "StudentEnrollmentId", "SchoolId", "SchoolClassId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudentGrowthSnapshots_TeachingAssignmentId_Sch~",
                table: "AppEducationStudentGrowthSnapshots",
                columns: new[] { "TeachingAssignmentId", "SchoolId", "AcademicTermId", "SchoolClassId", "TeacherUserId", "EducationSubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudentGrowthSnapshots_TextbookVersionId_School~",
                table: "AppEducationStudentGrowthSnapshots",
                columns: new[] { "TextbookVersionId", "SchoolId", "EducationSubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduGrowth_SourceVersion",
                table: "AppEducationStudentGrowthSnapshots",
                columns: new[] { "StudentUserId", "ContextId", "WindowStartsAtUtc", "WindowEndsAtUtc", "SourceFingerprint" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppEducationStudentGrowthSnapshots");
        }
    }
}
