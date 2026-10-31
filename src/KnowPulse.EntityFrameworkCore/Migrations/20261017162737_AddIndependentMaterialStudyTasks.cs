using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddIndependentMaterialStudyTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppEducationStudyEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecipientId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    StudentRevisionNumber = table.Column<int>(type: "integer", nullable: true),
                    BasedOnRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReplyToStudentRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    Difficulty = table.Column<int>(type: "integer", nullable: true),
                    SharedText = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    InternalTeacherNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ReportedMinutes = table.Column<int>(type: "integer", nullable: true),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpectedConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationStudyEvents", x => x.Id);
                    table.UniqueConstraint("AK_EduStudyEvents_Recipient", x => new { x.Id, x.RecipientId });
                    table.CheckConstraint("CK_EduStudyEvents_Action", "\"RevisionNumber\" > 0 AND length(\"RequestFingerprint\") = 64 AND ((\"Action\" = 3 AND \"ActorUserId\" = \"OwnerUserId\" AND \"StudentRevisionNumber\" IS NULL AND \"ReplyToStudentRevisionId\" IS NOT NULL AND \"InternalTeacherNote\" IS NOT NULL AND length(trim(\"InternalTeacherNote\")) BETWEEN 1 AND 2000 AND \"SharedText\" IS NULL AND \"Difficulty\" IS NULL AND \"ReportedMinutes\" IS NULL) OR (\"Action\" IN (0,1,2) AND \"ActorUserId\" = \"StudentUserId\" AND \"StudentRevisionNumber\" IS NOT NULL AND \"StudentRevisionNumber\" > 0 AND \"ReplyToStudentRevisionId\" IS NULL AND \"InternalTeacherNote\" IS NULL AND (\"SharedText\" IS NULL OR length(\"SharedText\") <= 2000) AND ((\"Action\" = 2 AND \"Difficulty\" IS NOT NULL AND \"Difficulty\" IN (0,1,2,3) AND \"SharedText\" IS NOT NULL AND length(trim(\"SharedText\")) > 0) OR (\"Action\" IN (0,1) AND \"Difficulty\" IS NULL)) AND ((\"Action\" = 1 AND (\"ReportedMinutes\" IS NULL OR \"ReportedMinutes\" BETWEEN 1 AND 1440)) OR (\"Action\" IN (0,2) AND \"ReportedMinutes\" IS NULL))))");
                    table.ForeignKey(
                        name: "FK_EduStudyEvents_BasedOnRevisionId",
                        columns: x => new { x.BasedOnRevisionId, x.RecipientId },
                        principalTable: "AppEducationStudyEvents",
                        principalColumns: new[] { "Id", "RecipientId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduStudyEvents_ReplyToStudentRevisionId",
                        columns: x => new { x.ReplyToStudentRevisionId, x.RecipientId },
                        principalTable: "AppEducationStudyEvents",
                        principalColumns: new[] { "Id", "RecipientId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationStudyRecipients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicationKind = table.Column<int>(type: "integer", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademicTermId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolClassId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    FirstProvidedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    CurrentRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    StudentRevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    CurrentStudentRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentFollowUpRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_AppEducationStudyRecipients", x => x.Id);
                    table.UniqueConstraint("AK_EduStudyRecipients_Actors", x => new { x.Id, x.StudentUserId, x.OwnerUserId });
                    table.CheckConstraint("CK_EduStudyRecipients_State", "\"PublicationKind\" = 1 AND ((\"RevisionNumber\" = 0 AND \"CurrentRevisionId\" IS NULL) OR (\"RevisionNumber\" > 0 AND \"CurrentRevisionId\" IS NOT NULL)) AND ((\"StudentRevisionNumber\" = 0 AND \"CurrentStudentRevisionId\" IS NULL) OR (\"StudentRevisionNumber\" > 0 AND \"CurrentStudentRevisionId\" IS NOT NULL)) AND \"StudentRevisionNumber\" <= \"RevisionNumber\" AND (\"FirstProvidedAtUtc\" IS NULL OR \"FirstProvidedAtUtc\" >= \"AssignedAtUtc\")");
                    table.ForeignKey(
                        name: "FK_EduStudyRecipients_CurrentFollowUpRevisionId",
                        columns: x => new { x.CurrentFollowUpRevisionId, x.Id },
                        principalTable: "AppEducationStudyEvents",
                        principalColumns: new[] { "Id", "RecipientId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduStudyRecipients_CurrentRevisionId",
                        columns: x => new { x.CurrentRevisionId, x.Id },
                        principalTable: "AppEducationStudyEvents",
                        principalColumns: new[] { "Id", "RecipientId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduStudyRecipients_CurrentStudentRevisionId",
                        columns: x => new { x.CurrentStudentRevisionId, x.Id },
                        principalTable: "AppEducationStudyEvents",
                        principalColumns: new[] { "Id", "RecipientId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduStudyRecipients_Enrollment",
                        columns: x => new { x.StudentEnrollmentId, x.SchoolId, x.SchoolClassId, x.StudentUserId },
                        principalTable: "AppStudentEnrollments",
                        principalColumns: new[] { "Id", "SchoolId", "SchoolClassId", "StudentUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduStudyRecipients_Membership",
                        columns: x => new { x.SchoolMembershipId, x.SchoolId, x.StudentUserId },
                        principalTable: "AppSchoolMemberships",
                        principalColumns: new[] { "Id", "SchoolId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationStudyTaskRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademicTermId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolClassId = table.Column<Guid>(type: "uuid", nullable: false),
                    BasedOnRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TeacherName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ClassName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ScopeJson = table.Column<string>(type: "text", maxLength: 33554432, nullable: false),
                    ContentJson = table.Column<string>(type: "character varying(262144)", maxLength: 262144, nullable: true),
                    StartsAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    PublicReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RosterFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpectedConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationStudyTaskRevisions", x => x.Id);
                    table.UniqueConstraint("AK_EduStudyVersions_Kind", x => new { x.Id, x.TaskId, x.Kind });
                    table.UniqueConstraint("AK_EduStudyVersions_Recipient", x => new { x.Id, x.TaskId, x.OwnerUserId, x.SchoolId, x.AcademicTermId, x.SchoolClassId, x.Kind });
                    table.UniqueConstraint("AK_EduStudyVersions_Root", x => new { x.Id, x.TaskId });
                    table.UniqueConstraint("AK_EduStudyVersions_Sequence", x => new { x.Id, x.TaskId, x.RevisionNumber });
                    table.CheckConstraint("CK_EduStudyVersions_Content", "\"RevisionNumber\" > 0 AND \"Kind\" IN (0,1,2) AND \"StartsAtUtc\" < \"EndsAtUtc\" AND length(\"RequestFingerprint\") = 64 AND ((\"Kind\" = 0 AND \"ContentJson\" IS NOT NULL AND \"SourceRevisionId\" IS NULL AND \"RosterFingerprint\" IS NULL) OR (\"Kind\" = 1 AND \"ContentJson\" IS NOT NULL AND \"SourceRevisionId\" IS NOT NULL AND \"RosterFingerprint\" IS NOT NULL AND length(\"RosterFingerprint\") = 64) OR (\"Kind\" = 2 AND \"ContentJson\" IS NULL AND \"SourceRevisionId\" IS NOT NULL AND \"RosterFingerprint\" IS NULL AND \"PublicReason\" IS NOT NULL AND length(trim(\"PublicReason\")) BETWEEN 1 AND 2000))");
                    table.ForeignKey(
                        name: "FK_EduStudyVersions_BasedOnRevisionId",
                        columns: x => new { x.BasedOnRevisionId, x.TaskId },
                        principalTable: "AppEducationStudyTaskRevisions",
                        principalColumns: new[] { "Id", "TaskId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduStudyVersions_SourceRevisionId",
                        columns: x => new { x.SourceRevisionId, x.TaskId },
                        principalTable: "AppEducationStudyTaskRevisions",
                        principalColumns: new[] { "Id", "TaskId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationStudyTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademicTermId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolClassId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeachingAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    EducationSubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TextbookVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    CurrentRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PublishedRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastPublishedRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PublicationKind = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
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
                    table.PrimaryKey("PK_AppEducationStudyTasks", x => x.Id);
                    table.UniqueConstraint("AK_EduStudyTasks_Authority", x => new { x.Id, x.OwnerUserId, x.SchoolId, x.AcademicTermId, x.SchoolClassId });
                    table.CheckConstraint("CK_EduStudyTasks_State", "\"PublicationKind\" = 1 AND ((\"RevisionNumber\" = 0 AND \"CurrentRevisionId\" IS NULL) OR (\"RevisionNumber\" > 0 AND \"CurrentRevisionId\" IS NOT NULL)) AND (\"PublishedRevisionId\" IS NULL OR \"PublishedRevisionId\" = \"LastPublishedRevisionId\")");
                    table.ForeignKey(
                        name: "FK_AppEducationStudyTasks_AbpUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AbpUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduStudyTasks_Current",
                        columns: x => new { x.CurrentRevisionId, x.Id, x.RevisionNumber },
                        principalTable: "AppEducationStudyTaskRevisions",
                        principalColumns: new[] { "Id", "TaskId", "RevisionNumber" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduStudyTasks_LastPublishedRevisionId",
                        columns: x => new { x.LastPublishedRevisionId, x.Id, x.PublicationKind },
                        principalTable: "AppEducationStudyTaskRevisions",
                        principalColumns: new[] { "Id", "TaskId", "Kind" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduStudyTasks_PublishedRevisionId",
                        columns: x => new { x.PublishedRevisionId, x.Id, x.PublicationKind },
                        principalTable: "AppEducationStudyTaskRevisions",
                        principalColumns: new[] { "Id", "TaskId", "Kind" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduStudyTasks_Teaching",
                        columns: x => new { x.TeachingAssignmentId, x.SchoolId, x.AcademicTermId, x.SchoolClassId, x.OwnerUserId, x.EducationSubjectId },
                        principalTable: "AppTeachingAssignments",
                        principalColumns: new[] { "Id", "SchoolId", "AcademicTermId", "SchoolClassId", "TeacherUserId", "EducationSubjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduStudyTasks_Textbook",
                        columns: x => new { x.TextbookVersionId, x.SchoolId, x.EducationSubjectId },
                        principalTable: "AppTextbookVersions",
                        principalColumns: new[] { "Id", "SchoolId", "EducationSubjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudyEvents_BasedOnRevisionId_RecipientId",
                table: "AppEducationStudyEvents",
                columns: new[] { "BasedOnRevisionId", "RecipientId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudyEvents_RecipientId_StudentUserId_OwnerUser~",
                table: "AppEducationStudyEvents",
                columns: new[] { "RecipientId", "StudentUserId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudyEvents_ReplyToStudentRevisionId_RecipientId",
                table: "AppEducationStudyEvents",
                columns: new[] { "ReplyToStudentRevisionId", "RecipientId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduStudyEvents_Sequence",
                table: "AppEducationStudyEvents",
                columns: new[] { "RecipientId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EduStudyEvents_StudentSequence",
                table: "AppEducationStudyEvents",
                columns: new[] { "RecipientId", "StudentRevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudyRecipients_CurrentFollowUpRevisionId_Id",
                table: "AppEducationStudyRecipients",
                columns: new[] { "CurrentFollowUpRevisionId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudyRecipients_CurrentRevisionId_Id",
                table: "AppEducationStudyRecipients",
                columns: new[] { "CurrentRevisionId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudyRecipients_CurrentStudentRevisionId_Id",
                table: "AppEducationStudyRecipients",
                columns: new[] { "CurrentStudentRevisionId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudyRecipients_SchoolMembershipId_SchoolId_Stu~",
                table: "AppEducationStudyRecipients",
                columns: new[] { "SchoolMembershipId", "SchoolId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudyRecipients_StudentEnrollmentId_SchoolId_Sc~",
                table: "AppEducationStudyRecipients",
                columns: new[] { "StudentEnrollmentId", "SchoolId", "SchoolClassId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudyRecipients_TaskRevisionId_TaskId_OwnerUser~",
                table: "AppEducationStudyRecipients",
                columns: new[] { "TaskRevisionId", "TaskId", "OwnerUserId", "SchoolId", "AcademicTermId", "SchoolClassId", "PublicationKind" });

            migrationBuilder.CreateIndex(
                name: "IX_EduStudyRecipients_Assigned",
                table: "AppEducationStudyRecipients",
                columns: new[] { "TaskRevisionId", "StudentUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EduStudyRecipients_Student",
                table: "AppEducationStudyRecipients",
                columns: new[] { "TenantId", "StudentUserId", "AssignedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudyTaskRevisions_BasedOnRevisionId_TaskId",
                table: "AppEducationStudyTaskRevisions",
                columns: new[] { "BasedOnRevisionId", "TaskId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudyTaskRevisions_SourceRevisionId_TaskId",
                table: "AppEducationStudyTaskRevisions",
                columns: new[] { "SourceRevisionId", "TaskId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudyTaskRevisions_TaskId_OwnerUserId_SchoolId_~",
                table: "AppEducationStudyTaskRevisions",
                columns: new[] { "TaskId", "OwnerUserId", "SchoolId", "AcademicTermId", "SchoolClassId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduStudyVersions_Sequence",
                table: "AppEducationStudyTaskRevisions",
                columns: new[] { "TaskId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudyTasks_CurrentRevisionId_Id_RevisionNumber",
                table: "AppEducationStudyTasks",
                columns: new[] { "CurrentRevisionId", "Id", "RevisionNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudyTasks_LastPublishedRevisionId_Id_Publicati~",
                table: "AppEducationStudyTasks",
                columns: new[] { "LastPublishedRevisionId", "Id", "PublicationKind" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudyTasks_OwnerUserId",
                table: "AppEducationStudyTasks",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudyTasks_PublishedRevisionId_Id_PublicationKi~",
                table: "AppEducationStudyTasks",
                columns: new[] { "PublishedRevisionId", "Id", "PublicationKind" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudyTasks_TeachingAssignmentId_SchoolId_Academ~",
                table: "AppEducationStudyTasks",
                columns: new[] { "TeachingAssignmentId", "SchoolId", "AcademicTermId", "SchoolClassId", "OwnerUserId", "EducationSubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationStudyTasks_TextbookVersionId_SchoolId_Education~",
                table: "AppEducationStudyTasks",
                columns: new[] { "TextbookVersionId", "SchoolId", "EducationSubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduStudyTasks_Owner",
                table: "AppEducationStudyTasks",
                columns: new[] { "TenantId", "OwnerUserId", "CreationTime" });

            migrationBuilder.AddForeignKey(
                name: "FK_EduStudyEvents_Recipient",
                table: "AppEducationStudyEvents",
                columns: new[] { "RecipientId", "StudentUserId", "OwnerUserId" },
                principalTable: "AppEducationStudyRecipients",
                principalColumns: new[] { "Id", "StudentUserId", "OwnerUserId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EduStudyRecipients_Publication",
                table: "AppEducationStudyRecipients",
                columns: new[] { "TaskRevisionId", "TaskId", "OwnerUserId", "SchoolId", "AcademicTermId", "SchoolClassId", "PublicationKind" },
                principalTable: "AppEducationStudyTaskRevisions",
                principalColumns: new[] { "Id", "TaskId", "OwnerUserId", "SchoolId", "AcademicTermId", "SchoolClassId", "Kind" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EduStudyVersions_Root",
                table: "AppEducationStudyTaskRevisions",
                columns: new[] { "TaskId", "OwnerUserId", "SchoolId", "AcademicTermId", "SchoolClassId" },
                principalTable: "AppEducationStudyTasks",
                principalColumns: new[] { "Id", "OwnerUserId", "SchoolId", "AcademicTermId", "SchoolClassId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "AppEducationStudyTasks") OR EXISTS (SELECT 1 FROM "AppEducationStudyTaskRevisions")
                        OR EXISTS (SELECT 1 FROM "AppEducationStudyRecipients") OR EXISTS (SELECT 1 FROM "AppEducationStudyEvents") THEN
                        RAISE EXCEPTION 'Study task history exists; schema downgrade requires an explicit compatible restore.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_EduStudyEvents_Recipient",
                table: "AppEducationStudyEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_EduStudyTasks_Current",
                table: "AppEducationStudyTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_EduStudyTasks_LastPublishedRevisionId",
                table: "AppEducationStudyTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_EduStudyTasks_PublishedRevisionId",
                table: "AppEducationStudyTasks");

            migrationBuilder.DropTable(
                name: "AppEducationStudyRecipients");

            migrationBuilder.DropTable(
                name: "AppEducationStudyEvents");

            migrationBuilder.DropTable(
                name: "AppEducationStudyTaskRevisions");

            migrationBuilder.DropTable(
                name: "AppEducationStudyTasks");
        }
    }
}
