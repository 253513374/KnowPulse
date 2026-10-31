using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddEducationAssessmentPublicationAndStarts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_AppTeachingAssignments_Id_SchoolId_AcademicTermId_SchoolCla~",
                table: "AppTeachingAssignments",
                columns: new[] { "Id", "SchoolId", "AcademicTermId", "SchoolClassId", "TeacherUserId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_AppStudentEnrollments_Id_SchoolId_SchoolClassId_StudentUser~",
                table: "AppStudentEnrollments",
                columns: new[] { "Id", "SchoolId", "SchoolClassId", "StudentUserId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_AppSchoolMemberships_Id_SchoolId_UserId",
                table: "AppSchoolMemberships",
                columns: new[] { "Id", "SchoolId", "UserId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_AppSchoolClasses_Id_SchoolId_AcademicTermId",
                table: "AppSchoolClasses",
                columns: new[] { "Id", "SchoolId", "AcademicTermId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_AppAcademicTerms_Id_SchoolId",
                table: "AppAcademicTerms",
                columns: new[] { "Id", "SchoolId" });

            migrationBuilder.CreateTable(
                name: "AppEducationAssessmentAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamPaperId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
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
                    table.PrimaryKey("PK_AppEducationAssessmentAttempts", x => x.Id);
                    table.UniqueConstraint("AK_AppEducationAssessmentAttempts_Id_RecipientId_AttemptNumber", x => new { x.Id, x.RecipientId, x.AttemptNumber });
                });

            migrationBuilder.CreateTable(
                name: "AppEducationAssessmentRecipients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolClassId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AccountName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    StartedAttemptCount = table.Column<int>(type: "integer", nullable: false),
                    CurrentAttemptId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_AppEducationAssessmentRecipients", x => x.Id);
                    table.UniqueConstraint("AK_AppEducationAssessmentRecipients_Id_TaskId_TaskRevisionId_S~", x => new { x.Id, x.TaskId, x.TaskRevisionId, x.StudentUserId });
                    table.CheckConstraint("CK_EducationAssessmentRecipients_CurrentAttempt", "(\"CurrentAttemptId\" IS NULL AND \"StartedAttemptCount\" = 0) OR (\"CurrentAttemptId\" IS NOT NULL AND \"StartedAttemptCount\" > 0)");
                    table.ForeignKey(
                        name: "FK_EduAssessmentRecipients_CurrentAttempt",
                        columns: x => new { x.CurrentAttemptId, x.Id, x.StartedAttemptCount },
                        principalTable: "AppEducationAssessmentAttempts",
                        principalColumns: new[] { "Id", "RecipientId", "AttemptNumber" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduAssessmentRecipients_Enrollment",
                        columns: x => new { x.StudentEnrollmentId, x.SchoolId, x.SchoolClassId, x.StudentUserId },
                        principalTable: "AppStudentEnrollments",
                        principalColumns: new[] { "Id", "SchoolId", "SchoolClassId", "StudentUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduAssessmentRecipients_Membership",
                        columns: x => new { x.SchoolMembershipId, x.SchoolId, x.StudentUserId },
                        principalTable: "AppSchoolMemberships",
                        principalColumns: new[] { "Id", "SchoolId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationAssessmentTaskRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    BasedOnRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Instructions = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademicTermId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolClassId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolClassName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TeachingAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamPaperId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurriculumSnapshotJson = table.Column<string>(type: "character varying(200000)", maxLength: 200000, nullable: false),
                    RecipientScope = table.Column<int>(type: "integer", nullable: false),
                    SelectedStudentIdsJson = table.Column<string>(type: "character varying(120000)", maxLength: 120000, nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    ResultVisibility = table.Column<int>(type: "integer", nullable: false),
                    AllowWrongQuestionCollection = table.Column<bool>(type: "boolean", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    RosterFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationAssessmentTaskRevisions", x => x.Id);
                    table.UniqueConstraint("AK_AppEducationAssessmentTaskRevisions_Id_TaskId", x => new { x.Id, x.TaskId });
                    table.UniqueConstraint("AK_AppEducationAssessmentTaskRevisions_Id_TaskId_ExamPaperId", x => new { x.Id, x.TaskId, x.ExamPaperId });
                    table.UniqueConstraint("AK_AppEducationAssessmentTaskRevisions_Id_TaskId_RevisionNumber", x => new { x.Id, x.TaskId, x.RevisionNumber });
                    table.UniqueConstraint("AK_AppEducationAssessmentTaskRevisions_Id_TaskId_SchoolId_Scho~", x => new { x.Id, x.TaskId, x.SchoolId, x.SchoolClassId });
                    table.ForeignKey(
                        name: "FK_EduAssessmentRevisions_Class",
                        columns: x => new { x.SchoolClassId, x.SchoolId, x.AcademicTermId },
                        principalTable: "AppSchoolClasses",
                        principalColumns: new[] { "Id", "SchoolId", "AcademicTermId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduAssessmentRevisions_Paper",
                        column: x => x.ExamPaperId,
                        principalTable: "AppExamPapers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduAssessmentRevisions_PreviousRevision",
                        columns: x => new { x.BasedOnRevisionId, x.TaskId },
                        principalTable: "AppEducationAssessmentTaskRevisions",
                        principalColumns: new[] { "Id", "TaskId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduAssessmentRevisions_TeachingAssignment",
                        columns: x => new { x.TeachingAssignmentId, x.SchoolId, x.AcademicTermId, x.SchoolClassId, x.OwnerUserId },
                        principalTable: "AppTeachingAssignments",
                        principalColumns: new[] { "Id", "SchoolId", "AcademicTermId", "SchoolClassId", "TeacherUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduAssessmentRevisions_Term",
                        columns: x => new { x.AcademicTermId, x.SchoolId },
                        principalTable: "AppAcademicTerms",
                        principalColumns: new[] { "Id", "SchoolId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationAssessmentTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentRevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    PublishedRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreateFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
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
                    table.PrimaryKey("PK_AppEducationAssessmentTasks", x => x.Id);
                    table.UniqueConstraint("AK_AppEducationAssessmentTasks_Id_OwnerUserId", x => new { x.Id, x.OwnerUserId });
                    table.CheckConstraint("CK_EducationAssessmentTasks_CurrentRevision", "(\"CurrentRevisionId\" IS NULL AND \"CurrentRevisionNumber\" = 0) OR (\"CurrentRevisionId\" IS NOT NULL AND \"CurrentRevisionNumber\" > 0)");
                    table.CheckConstraint("CK_EducationAssessmentTasks_PublishedRevision", "\"PublishedRevisionId\" IS NULL OR (\"CurrentRevisionId\" IS NOT NULL AND \"PublishedRevisionId\" = \"CurrentRevisionId\")");
                    table.ForeignKey(
                        name: "FK_EduAssessmentTasks_CurrentRevision",
                        columns: x => new { x.CurrentRevisionId, x.Id, x.CurrentRevisionNumber },
                        principalTable: "AppEducationAssessmentTaskRevisions",
                        principalColumns: new[] { "Id", "TaskId", "RevisionNumber" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduAssessmentTasks_Owner",
                        column: x => x.OwnerUserId,
                        principalTable: "AbpUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduAssessmentTasks_PublishedRevision",
                        columns: x => new { x.PublishedRevisionId, x.Id },
                        principalTable: "AppEducationAssessmentTaskRevisions",
                        principalColumns: new[] { "Id", "TaskId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentAttempts_RecipientId_AttemptNumber",
                table: "AppEducationAssessmentAttempts",
                columns: new[] { "RecipientId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentAttempts_RecipientId_TaskId_TaskRevis~",
                table: "AppEducationAssessmentAttempts",
                columns: new[] { "RecipientId", "TaskId", "TaskRevisionId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentAttempts_TaskRevisionId_TaskId_ExamPa~",
                table: "AppEducationAssessmentAttempts",
                columns: new[] { "TaskRevisionId", "TaskId", "ExamPaperId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentAttempts_TenantId_StudentUserId_Expir~",
                table: "AppEducationAssessmentAttempts",
                columns: new[] { "TenantId", "StudentUserId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentRecipients_CurrentAttemptId_Id_Starte~",
                table: "AppEducationAssessmentRecipients",
                columns: new[] { "CurrentAttemptId", "Id", "StartedAttemptCount" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentRecipients_SchoolMembershipId_SchoolI~",
                table: "AppEducationAssessmentRecipients",
                columns: new[] { "SchoolMembershipId", "SchoolId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentRecipients_StudentEnrollmentId_School~",
                table: "AppEducationAssessmentRecipients",
                columns: new[] { "StudentEnrollmentId", "SchoolId", "SchoolClassId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentRecipients_TaskRevisionId_StudentUser~",
                table: "AppEducationAssessmentRecipients",
                columns: new[] { "TaskRevisionId", "StudentUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentRecipients_TaskRevisionId_TaskId_Scho~",
                table: "AppEducationAssessmentRecipients",
                columns: new[] { "TaskRevisionId", "TaskId", "SchoolId", "SchoolClassId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentRecipients_TenantId_StudentUserId_Sch~",
                table: "AppEducationAssessmentRecipients",
                columns: new[] { "TenantId", "StudentUserId", "SchoolId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentTaskRevisions_AcademicTermId_SchoolId",
                table: "AppEducationAssessmentTaskRevisions",
                columns: new[] { "AcademicTermId", "SchoolId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentTaskRevisions_BasedOnRevisionId_TaskId",
                table: "AppEducationAssessmentTaskRevisions",
                columns: new[] { "BasedOnRevisionId", "TaskId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentTaskRevisions_ExamPaperId",
                table: "AppEducationAssessmentTaskRevisions",
                column: "ExamPaperId");

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentTaskRevisions_SchoolClassId_SchoolId_~",
                table: "AppEducationAssessmentTaskRevisions",
                columns: new[] { "SchoolClassId", "SchoolId", "AcademicTermId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentTaskRevisions_TaskId_OwnerUserId",
                table: "AppEducationAssessmentTaskRevisions",
                columns: new[] { "TaskId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentTaskRevisions_TaskId_RevisionNumber",
                table: "AppEducationAssessmentTaskRevisions",
                columns: new[] { "TaskId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentTaskRevisions_TeachingAssignmentId_Sc~",
                table: "AppEducationAssessmentTaskRevisions",
                columns: new[] { "TeachingAssignmentId", "SchoolId", "AcademicTermId", "SchoolClassId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentTaskRevisions_TenantId_OwnerUserId_Sc~",
                table: "AppEducationAssessmentTaskRevisions",
                columns: new[] { "TenantId", "OwnerUserId", "SchoolId", "SchoolClassId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentTasks_CurrentRevisionId_Id_CurrentRev~",
                table: "AppEducationAssessmentTasks",
                columns: new[] { "CurrentRevisionId", "Id", "CurrentRevisionNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentTasks_OwnerUserId",
                table: "AppEducationAssessmentTasks",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentTasks_PublishedRevisionId_Id",
                table: "AppEducationAssessmentTasks",
                columns: new[] { "PublishedRevisionId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationAssessmentTasks_TenantId_OwnerUserId_UpdatedAtU~",
                table: "AppEducationAssessmentTasks",
                columns: new[] { "TenantId", "OwnerUserId", "UpdatedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_EduAssessmentAttempts_Recipient",
                table: "AppEducationAssessmentAttempts",
                columns: new[] { "RecipientId", "TaskId", "TaskRevisionId", "StudentUserId" },
                principalTable: "AppEducationAssessmentRecipients",
                principalColumns: new[] { "Id", "TaskId", "TaskRevisionId", "StudentUserId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EduAssessmentAttempts_RevisionPaper",
                table: "AppEducationAssessmentAttempts",
                columns: new[] { "TaskRevisionId", "TaskId", "ExamPaperId" },
                principalTable: "AppEducationAssessmentTaskRevisions",
                principalColumns: new[] { "Id", "TaskId", "ExamPaperId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EduAssessmentRecipients_RevisionClass",
                table: "AppEducationAssessmentRecipients",
                columns: new[] { "TaskRevisionId", "TaskId", "SchoolId", "SchoolClassId" },
                principalTable: "AppEducationAssessmentTaskRevisions",
                principalColumns: new[] { "Id", "TaskId", "SchoolId", "SchoolClassId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EduAssessmentRevisions_TaskOwner",
                table: "AppEducationAssessmentTaskRevisions",
                columns: new[] { "TaskId", "OwnerUserId" },
                principalTable: "AppEducationAssessmentTasks",
                principalColumns: new[] { "Id", "OwnerUserId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EduAssessmentAttempts_Recipient",
                table: "AppEducationAssessmentAttempts");

            migrationBuilder.DropForeignKey(
                name: "FK_EduAssessmentTasks_CurrentRevision",
                table: "AppEducationAssessmentTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_EduAssessmentTasks_PublishedRevision",
                table: "AppEducationAssessmentTasks");

            migrationBuilder.DropTable(
                name: "AppEducationAssessmentRecipients");

            migrationBuilder.DropTable(
                name: "AppEducationAssessmentAttempts");

            migrationBuilder.DropTable(
                name: "AppEducationAssessmentTaskRevisions");

            migrationBuilder.DropTable(
                name: "AppEducationAssessmentTasks");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_AppTeachingAssignments_Id_SchoolId_AcademicTermId_SchoolCla~",
                table: "AppTeachingAssignments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_AppStudentEnrollments_Id_SchoolId_SchoolClassId_StudentUser~",
                table: "AppStudentEnrollments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_AppSchoolMemberships_Id_SchoolId_UserId",
                table: "AppSchoolMemberships");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_AppSchoolClasses_Id_SchoolId_AcademicTermId",
                table: "AppSchoolClasses");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_AppAcademicTerms_Id_SchoolId",
                table: "AppAcademicTerms");
        }
    }
}
