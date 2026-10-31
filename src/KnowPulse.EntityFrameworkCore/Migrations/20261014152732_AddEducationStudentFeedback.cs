using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddEducationStudentFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppEducationTeacherObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeachingAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademicTermId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolClassId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    ObservedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationTeacherObservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EduObservations_Enrollment",
                        columns: x => new { x.StudentEnrollmentId, x.SchoolId, x.SchoolClassId, x.StudentUserId },
                        principalTable: "AppStudentEnrollments",
                        principalColumns: new[] { "Id", "SchoolId", "SchoolClassId", "StudentUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduObservations_Membership",
                        columns: x => new { x.SchoolMembershipId, x.SchoolId, x.StudentUserId },
                        principalTable: "AppSchoolMemberships",
                        principalColumns: new[] { "Id", "SchoolId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduObservations_Teaching",
                        columns: x => new { x.TeachingAssignmentId, x.SchoolId, x.AcademicTermId, x.SchoolClassId, x.OwnerUserId },
                        principalTable: "AppTeachingAssignments",
                        principalColumns: new[] { "Id", "SchoolId", "AcademicTermId", "SchoolClassId", "TeacherUserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationFeedbackAssessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    FeedbackId = table.Column<Guid>(type: "uuid", nullable: false),
                    FeedbackRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationFeedbackAssessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EduFeedbackAssessments_Recipient",
                        columns: x => new { x.RecipientId, x.TaskId, x.TaskRevisionId, x.StudentUserId },
                        principalTable: "AppEducationAssessmentRecipients",
                        principalColumns: new[] { "Id", "TaskId", "TaskRevisionId", "StudentUserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationFeedbackNotices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    FeedbackId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationFeedbackNotices", x => x.Id);
                    table.CheckConstraint("CK_EduFeedbackNotices_Kind", "\"Kind\" IN (1, 2)");
                });

            migrationBuilder.CreateTable(
                name: "AppEducationFeedbackReads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    FeedbackId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpenedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationFeedbackReads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationStudentFeedback",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeachingAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademicTermId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolClassId = table.Column<Guid>(type: "uuid", nullable: false),
                    EducationSubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeacherName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    StudentName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AccountName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ClassName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SubjectName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    CurrentRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentDraftRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PublishedRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastPublishedRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_AppEducationStudentFeedback", x => x.Id);
                    table.UniqueConstraint("AK_EduFeedback_StudentOwner", x => new { x.Id, x.StudentUserId, x.OwnerUserId });
                    table.CheckConstraint("CK_EduFeedback_Revision", "(\"RevisionNumber\" = 0 AND \"CurrentRevisionId\" IS NULL) OR (\"RevisionNumber\" > 0 AND \"CurrentRevisionId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_EduFeedback_Enrollment",
                        columns: x => new { x.StudentEnrollmentId, x.SchoolId, x.SchoolClassId, x.StudentUserId },
                        principalTable: "AppStudentEnrollments",
                        principalColumns: new[] { "Id", "SchoolId", "SchoolClassId", "StudentUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduFeedback_Membership",
                        columns: x => new { x.SchoolMembershipId, x.SchoolId, x.StudentUserId },
                        principalTable: "AppSchoolMemberships",
                        principalColumns: new[] { "Id", "SchoolId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduFeedback_Subject",
                        column: x => x.EducationSubjectId,
                        principalTable: "AppEducationSubjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduFeedback_Teaching",
                        columns: x => new { x.TeachingAssignmentId, x.SchoolId, x.AcademicTermId, x.SchoolClassId, x.OwnerUserId },
                        principalTable: "AppTeachingAssignments",
                        principalColumns: new[] { "Id", "SchoolId", "AcademicTermId", "SchoolClassId", "TeacherUserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationStudentFeedbackRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    FeedbackId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    BasedOnRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    DraftRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreviousPublishedRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    ContentJson = table.Column<string>(type: "character varying(32000)", maxLength: 32000, nullable: true),
                    Reason = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpectedConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationStudentFeedbackRevisions", x => x.Id);
                    table.UniqueConstraint("AK_EduFeedbackVersions_FeedbackStudent", x => new { x.Id, x.FeedbackId, x.StudentUserId });
                    table.CheckConstraint("CK_EduFeedbackVersions_Kind", "\"RevisionNumber\" > 0 AND ((\"Kind\" = 0 AND \"ContentJson\" IS NOT NULL AND \"DraftRevisionId\" IS NULL) OR (\"Kind\" = 1 AND \"ContentJson\" IS NOT NULL AND \"DraftRevisionId\" IS NOT NULL) OR (\"Kind\" = 2 AND \"ContentJson\" IS NULL AND \"DraftRevisionId\" IS NULL AND \"PreviousPublishedRevisionId\" IS NOT NULL AND \"Reason\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_EduFeedbackVersions_BasedOnRevisionId",
                        columns: x => new { x.BasedOnRevisionId, x.FeedbackId, x.StudentUserId },
                        principalTable: "AppEducationStudentFeedbackRevisions",
                        principalColumns: new[] { "Id", "FeedbackId", "StudentUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduFeedbackVersions_DraftRevisionId",
                        columns: x => new { x.DraftRevisionId, x.FeedbackId, x.StudentUserId },
                        principalTable: "AppEducationStudentFeedbackRevisions",
                        principalColumns: new[] { "Id", "FeedbackId", "StudentUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduFeedbackVersions_PreviousPublishedRevisionId",
                        columns: x => new { x.PreviousPublishedRevisionId, x.FeedbackId, x.StudentUserId },
                        principalTable: "AppEducationStudentFeedbackRevisions",
                        principalColumns: new[] { "Id", "FeedbackId", "StudentUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduFeedbackVersions_Root",
                        columns: x => new { x.FeedbackId, x.StudentUserId, x.OwnerUserId },
                        principalTable: "AppEducationStudentFeedback",
                        principalColumns: new[] { "Id", "StudentUserId", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationFeedbackAssessments_FeedbackRevisionId_Feedback~",
                table: "AppEducationFeedbackAssessments",
                columns: new[] { "FeedbackRevisionId", "FeedbackId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationFeedbackAssessments_RecipientId_TaskId_TaskRevi~",
                table: "AppEducationFeedbackAssessments",
                columns: new[] { "RecipientId", "TaskId", "TaskRevisionId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduFeedbackAssessments_Unique",
                table: "AppEducationFeedbackAssessments",
                columns: new[] { "FeedbackRevisionId", "TaskId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationFeedbackNotices_Id_FeedbackId_StudentUserId",
                table: "AppEducationFeedbackNotices",
                columns: new[] { "Id", "FeedbackId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduFeedbackNotices_Inbox",
                table: "AppEducationFeedbackNotices",
                columns: new[] { "TenantId", "StudentUserId", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationFeedbackReads_Id_FeedbackId_StudentUserId",
                table: "AppEducationFeedbackReads",
                columns: new[] { "Id", "FeedbackId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudentFeedback_CurrentDraftRevisionId_Id_Stude~",
                table: "AppEducationStudentFeedback",
                columns: new[] { "CurrentDraftRevisionId", "Id", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudentFeedback_CurrentRevisionId_Id_StudentUse~",
                table: "AppEducationStudentFeedback",
                columns: new[] { "CurrentRevisionId", "Id", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudentFeedback_EducationSubjectId",
                table: "AppEducationStudentFeedback",
                column: "EducationSubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudentFeedback_LastPublishedRevisionId_Id_Stud~",
                table: "AppEducationStudentFeedback",
                columns: new[] { "LastPublishedRevisionId", "Id", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudentFeedback_PublishedRevisionId_Id_StudentU~",
                table: "AppEducationStudentFeedback",
                columns: new[] { "PublishedRevisionId", "Id", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudentFeedback_SchoolMembershipId_SchoolId_Stu~",
                table: "AppEducationStudentFeedback",
                columns: new[] { "SchoolMembershipId", "SchoolId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudentFeedback_StudentEnrollmentId_SchoolId_Sc~",
                table: "AppEducationStudentFeedback",
                columns: new[] { "StudentEnrollmentId", "SchoolId", "SchoolClassId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudentFeedback_TeachingAssignmentId_SchoolId_A~",
                table: "AppEducationStudentFeedback",
                columns: new[] { "TeachingAssignmentId", "SchoolId", "AcademicTermId", "SchoolClassId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduFeedback_Student",
                table: "AppEducationStudentFeedback",
                columns: new[] { "TenantId", "StudentUserId", "PublishedRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduFeedback_Teacher",
                table: "AppEducationStudentFeedback",
                columns: new[] { "TenantId", "OwnerUserId", "TeachingAssignmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudentFeedbackRevisions_BasedOnRevisionId_Feed~",
                table: "AppEducationStudentFeedbackRevisions",
                columns: new[] { "BasedOnRevisionId", "FeedbackId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudentFeedbackRevisions_DraftRevisionId_Feedba~",
                table: "AppEducationStudentFeedbackRevisions",
                columns: new[] { "DraftRevisionId", "FeedbackId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudentFeedbackRevisions_FeedbackId_StudentUser~",
                table: "AppEducationStudentFeedbackRevisions",
                columns: new[] { "FeedbackId", "StudentUserId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudentFeedbackRevisions_PreviousPublishedRevis~",
                table: "AppEducationStudentFeedbackRevisions",
                columns: new[] { "PreviousPublishedRevisionId", "FeedbackId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduFeedbackVersions_Sequence",
                table: "AppEducationStudentFeedbackRevisions",
                columns: new[] { "FeedbackId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeacherObservations_SchoolMembershipId_SchoolId~",
                table: "AppEducationTeacherObservations",
                columns: new[] { "SchoolMembershipId", "SchoolId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeacherObservations_StudentEnrollmentId_SchoolI~",
                table: "AppEducationTeacherObservations",
                columns: new[] { "StudentEnrollmentId", "SchoolId", "SchoolClassId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeacherObservations_TeachingAssignmentId_School~",
                table: "AppEducationTeacherObservations",
                columns: new[] { "TeachingAssignmentId", "SchoolId", "AcademicTermId", "SchoolClassId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduObservations_OwnerStudent",
                table: "AppEducationTeacherObservations",
                columns: new[] { "TenantId", "OwnerUserId", "TeachingAssignmentId", "StudentEnrollmentId", "ObservedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_EduFeedbackAssessments_Version",
                table: "AppEducationFeedbackAssessments",
                columns: new[] { "FeedbackRevisionId", "FeedbackId", "StudentUserId" },
                principalTable: "AppEducationStudentFeedbackRevisions",
                principalColumns: new[] { "Id", "FeedbackId", "StudentUserId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EduFeedbackNotices_Version",
                table: "AppEducationFeedbackNotices",
                columns: new[] { "Id", "FeedbackId", "StudentUserId" },
                principalTable: "AppEducationStudentFeedbackRevisions",
                principalColumns: new[] { "Id", "FeedbackId", "StudentUserId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EduFeedbackReads_Version",
                table: "AppEducationFeedbackReads",
                columns: new[] { "Id", "FeedbackId", "StudentUserId" },
                principalTable: "AppEducationStudentFeedbackRevisions",
                principalColumns: new[] { "Id", "FeedbackId", "StudentUserId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EduFeedback_CurrentDraftRevisionId",
                table: "AppEducationStudentFeedback",
                columns: new[] { "CurrentDraftRevisionId", "Id", "StudentUserId" },
                principalTable: "AppEducationStudentFeedbackRevisions",
                principalColumns: new[] { "Id", "FeedbackId", "StudentUserId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EduFeedback_CurrentRevisionId",
                table: "AppEducationStudentFeedback",
                columns: new[] { "CurrentRevisionId", "Id", "StudentUserId" },
                principalTable: "AppEducationStudentFeedbackRevisions",
                principalColumns: new[] { "Id", "FeedbackId", "StudentUserId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EduFeedback_LastPublishedRevisionId",
                table: "AppEducationStudentFeedback",
                columns: new[] { "LastPublishedRevisionId", "Id", "StudentUserId" },
                principalTable: "AppEducationStudentFeedbackRevisions",
                principalColumns: new[] { "Id", "FeedbackId", "StudentUserId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EduFeedback_PublishedRevisionId",
                table: "AppEducationStudentFeedback",
                columns: new[] { "PublishedRevisionId", "Id", "StudentUserId" },
                principalTable: "AppEducationStudentFeedbackRevisions",
                principalColumns: new[] { "Id", "FeedbackId", "StudentUserId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EduFeedback_CurrentDraftRevisionId",
                table: "AppEducationStudentFeedback");

            migrationBuilder.DropForeignKey(
                name: "FK_EduFeedback_CurrentRevisionId",
                table: "AppEducationStudentFeedback");

            migrationBuilder.DropForeignKey(
                name: "FK_EduFeedback_LastPublishedRevisionId",
                table: "AppEducationStudentFeedback");

            migrationBuilder.DropForeignKey(
                name: "FK_EduFeedback_PublishedRevisionId",
                table: "AppEducationStudentFeedback");

            migrationBuilder.DropTable(
                name: "AppEducationFeedbackAssessments");

            migrationBuilder.DropTable(
                name: "AppEducationFeedbackNotices");

            migrationBuilder.DropTable(
                name: "AppEducationFeedbackReads");

            migrationBuilder.DropTable(
                name: "AppEducationTeacherObservations");

            migrationBuilder.DropTable(
                name: "AppEducationStudentFeedbackRevisions");

            migrationBuilder.DropTable(
                name: "AppEducationStudentFeedback");
        }
    }
}
