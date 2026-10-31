using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddEducationTeachingPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppEducationTeachingPlanQuestionExposures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    EducationSubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamPaperId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaperQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExposedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationTeachingPlanQuestionExposures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EduPlanContexts_Definition",
                        columns: x => new { x.DefinitionRevisionId, x.OriginalQuestionId },
                        principalTable: "AppQuestionDefinitionRevisions",
                        principalColumns: new[] { "Id", "QuestionId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduPlanContexts_PaperRevision",
                        columns: x => new { x.TaskRevisionId, x.TaskId, x.ExamPaperId },
                        principalTable: "AppEducationAssessmentTaskRevisions",
                        principalColumns: new[] { "Id", "TaskId", "ExamPaperId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduPlanContexts_Question",
                        columns: x => new { x.PaperQuestionId, x.ExamPaperId },
                        principalTable: "AppExamPaperQuestions",
                        principalColumns: new[] { "Id", "PaperId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduPlanContexts_Recipient",
                        columns: x => new { x.RecipientId, x.TaskId, x.TaskRevisionId, x.StudentUserId },
                        principalTable: "AppEducationAssessmentRecipients",
                        principalColumns: new[] { "Id", "TaskId", "TaskRevisionId", "StudentUserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationTeachingPlanRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    BasedOnRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    BasisJson = table.Column<string>(type: "text", nullable: true),
                    ContentJson = table.Column<string>(type: "text", nullable: true),
                    InternalNotes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Reason = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpectedConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationTeachingPlanRevisions", x => x.Id);
                    table.UniqueConstraint("AK_EduPlanVersions_Recipient", x => new { x.Id, x.PlanId, x.StudentUserId, x.OwnerUserId });
                    table.UniqueConstraint("AK_EduPlanVersions_Student", x => new { x.Id, x.PlanId, x.StudentUserId });
                    table.CheckConstraint("CK_EduPlanVersions_Content", "\"RevisionNumber\" > 0 AND ((\"Kind\" IN (0,1,2) AND \"BasisJson\" IS NOT NULL AND length(\"BasisJson\") BETWEEN 1 AND 8388608 AND \"ContentJson\" IS NOT NULL AND length(\"ContentJson\") BETWEEN 1 AND 262144) OR (\"Kind\" = 3 AND \"BasisJson\" IS NULL AND \"ContentJson\" IS NULL AND \"InternalNotes\" IS NULL AND \"Reason\" IS NOT NULL AND length(trim(\"Reason\")) > 0)) AND ((\"Kind\" = 0 AND \"SourceRevisionId\" IS NULL) OR (\"Kind\" IN (1,2,3) AND \"SourceRevisionId\" IS NOT NULL AND \"BasedOnRevisionId\" IS NOT NULL AND \"SourceRevisionId\" = \"BasedOnRevisionId\"))");
                    table.ForeignKey(
                        name: "FK_EduPlanVersions_BasedOnRevisionId",
                        columns: x => new { x.BasedOnRevisionId, x.PlanId, x.StudentUserId, x.OwnerUserId },
                        principalTable: "AppEducationTeachingPlanRevisions",
                        principalColumns: new[] { "Id", "PlanId", "StudentUserId", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduPlanVersions_SourceRevisionId",
                        columns: x => new { x.SourceRevisionId, x.PlanId, x.StudentUserId, x.OwnerUserId },
                        principalTable: "AppEducationTeachingPlanRevisions",
                        principalColumns: new[] { "Id", "PlanId", "StudentUserId", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationTeachingPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademicTermId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolClassId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeachingAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    EducationSubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TextbookVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeacherName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    StudentName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ClassName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SubjectName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    CurrentRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PublishedRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastPublishedRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PublicRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_AppEducationTeachingPlans", x => x.Id);
                    table.UniqueConstraint("AK_EduPlans_Recipient", x => new { x.Id, x.StudentUserId, x.OwnerUserId });
                    table.CheckConstraint("CK_EduPlans_State", "((\"RevisionNumber\" = 0 AND \"CurrentRevisionId\" IS NULL) OR (\"RevisionNumber\" > 0 AND \"CurrentRevisionId\" IS NOT NULL)) AND ((\"LastPublishedRevisionId\" IS NULL AND \"PublicRevisionId\" IS NULL AND \"PublishedRevisionId\" IS NULL) OR (\"LastPublishedRevisionId\" IS NOT NULL AND \"PublicRevisionId\" IS NOT NULL)) AND (\"PublishedRevisionId\" IS NULL OR (\"PublishedRevisionId\" = \"LastPublishedRevisionId\" AND \"PublicRevisionId\" = \"PublishedRevisionId\"))");
                    table.ForeignKey(
                        name: "FK_EduPlans_Book",
                        columns: x => new { x.TextbookVersionId, x.SchoolId, x.EducationSubjectId },
                        principalTable: "AppTextbookVersions",
                        principalColumns: new[] { "Id", "SchoolId", "EducationSubjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduPlans_CurrentRevisionId",
                        columns: x => new { x.CurrentRevisionId, x.Id, x.StudentUserId, x.OwnerUserId },
                        principalTable: "AppEducationTeachingPlanRevisions",
                        principalColumns: new[] { "Id", "PlanId", "StudentUserId", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduPlans_Enrollment",
                        columns: x => new { x.StudentEnrollmentId, x.SchoolId, x.SchoolClassId, x.StudentUserId },
                        principalTable: "AppStudentEnrollments",
                        principalColumns: new[] { "Id", "SchoolId", "SchoolClassId", "StudentUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduPlans_LastPublishedRevisionId",
                        columns: x => new { x.LastPublishedRevisionId, x.Id, x.StudentUserId, x.OwnerUserId },
                        principalTable: "AppEducationTeachingPlanRevisions",
                        principalColumns: new[] { "Id", "PlanId", "StudentUserId", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduPlans_Member",
                        columns: x => new { x.SchoolMembershipId, x.SchoolId, x.StudentUserId },
                        principalTable: "AppSchoolMemberships",
                        principalColumns: new[] { "Id", "SchoolId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduPlans_PublicRevisionId",
                        columns: x => new { x.PublicRevisionId, x.Id, x.StudentUserId, x.OwnerUserId },
                        principalTable: "AppEducationTeachingPlanRevisions",
                        principalColumns: new[] { "Id", "PlanId", "StudentUserId", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduPlans_PublishedRevisionId",
                        columns: x => new { x.PublishedRevisionId, x.Id, x.StudentUserId, x.OwnerUserId },
                        principalTable: "AppEducationTeachingPlanRevisions",
                        principalColumns: new[] { "Id", "PlanId", "StudentUserId", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduPlans_Teaching",
                        columns: x => new { x.TeachingAssignmentId, x.SchoolId, x.AcademicTermId, x.SchoolClassId, x.OwnerUserId, x.EducationSubjectId },
                        principalTable: "AppTeachingAssignments",
                        principalColumns: new[] { "Id", "SchoolId", "AcademicTermId", "SchoolClassId", "TeacherUserId", "EducationSubjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlanQuestionExposures_DefinitionRevisio~",
                table: "AppEducationTeachingPlanQuestionExposures",
                columns: new[] { "DefinitionRevisionId", "OriginalQuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlanQuestionExposures_PaperQuestionId_E~",
                table: "AppEducationTeachingPlanQuestionExposures",
                columns: new[] { "PaperQuestionId", "ExamPaperId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlanQuestionExposures_PlanRevisionId_Pl~",
                table: "AppEducationTeachingPlanQuestionExposures",
                columns: new[] { "PlanRevisionId", "PlanId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlanQuestionExposures_RecipientId_TaskI~",
                table: "AppEducationTeachingPlanQuestionExposures",
                columns: new[] { "RecipientId", "TaskId", "TaskRevisionId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlanQuestionExposures_TaskRevisionId_Ta~",
                table: "AppEducationTeachingPlanQuestionExposures",
                columns: new[] { "TaskRevisionId", "TaskId", "ExamPaperId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduPlanContexts_Once",
                table: "AppEducationTeachingPlanQuestionExposures",
                columns: new[] { "PlanRevisionId", "RecipientId", "PaperQuestionId", "StudentUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EduPlanContexts_Original",
                table: "AppEducationTeachingPlanQuestionExposures",
                columns: new[] { "TenantId", "StudentUserId", "EducationSubjectId", "OriginalQuestionId", "ExposedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlanRevisions_BasedOnRevisionId_PlanId_~",
                table: "AppEducationTeachingPlanRevisions",
                columns: new[] { "BasedOnRevisionId", "PlanId", "StudentUserId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlanRevisions_PlanId_StudentUserId_Owne~",
                table: "AppEducationTeachingPlanRevisions",
                columns: new[] { "PlanId", "StudentUserId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlanRevisions_SourceRevisionId_PlanId_S~",
                table: "AppEducationTeachingPlanRevisions",
                columns: new[] { "SourceRevisionId", "PlanId", "StudentUserId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduPlanVersions_Sequence",
                table: "AppEducationTeachingPlanRevisions",
                columns: new[] { "PlanId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlans_CurrentRevisionId_Id_StudentUserI~",
                table: "AppEducationTeachingPlans",
                columns: new[] { "CurrentRevisionId", "Id", "StudentUserId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlans_LastPublishedRevisionId_Id_Studen~",
                table: "AppEducationTeachingPlans",
                columns: new[] { "LastPublishedRevisionId", "Id", "StudentUserId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlans_PublicRevisionId_Id_StudentUserId~",
                table: "AppEducationTeachingPlans",
                columns: new[] { "PublicRevisionId", "Id", "StudentUserId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlans_PublishedRevisionId_Id_StudentUse~",
                table: "AppEducationTeachingPlans",
                columns: new[] { "PublishedRevisionId", "Id", "StudentUserId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlans_SchoolMembershipId_SchoolId_Stude~",
                table: "AppEducationTeachingPlans",
                columns: new[] { "SchoolMembershipId", "SchoolId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlans_StudentEnrollmentId_SchoolId_Scho~",
                table: "AppEducationTeachingPlans",
                columns: new[] { "StudentEnrollmentId", "SchoolId", "SchoolClassId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlans_TeachingAssignmentId_SchoolId_Aca~",
                table: "AppEducationTeachingPlans",
                columns: new[] { "TeachingAssignmentId", "SchoolId", "AcademicTermId", "SchoolClassId", "OwnerUserId", "EducationSubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingPlans_TextbookVersionId_SchoolId_Educat~",
                table: "AppEducationTeachingPlans",
                columns: new[] { "TextbookVersionId", "SchoolId", "EducationSubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduPlans_Student",
                table: "AppEducationTeachingPlans",
                columns: new[] { "TenantId", "StudentUserId", "PublicRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduPlans_Teacher",
                table: "AppEducationTeachingPlans",
                columns: new[] { "TenantId", "OwnerUserId", "TeachingAssignmentId" });

            migrationBuilder.AddForeignKey(
                name: "FK_EduPlanContexts_Version",
                table: "AppEducationTeachingPlanQuestionExposures",
                columns: new[] { "PlanRevisionId", "PlanId", "StudentUserId" },
                principalTable: "AppEducationTeachingPlanRevisions",
                principalColumns: new[] { "Id", "PlanId", "StudentUserId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EduPlanVersions_Plan",
                table: "AppEducationTeachingPlanRevisions",
                columns: new[] { "PlanId", "StudentUserId", "OwnerUserId" },
                principalTable: "AppEducationTeachingPlans",
                principalColumns: new[] { "Id", "StudentUserId", "OwnerUserId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EduPlans_CurrentRevisionId",
                table: "AppEducationTeachingPlans");

            migrationBuilder.DropForeignKey(
                name: "FK_EduPlans_LastPublishedRevisionId",
                table: "AppEducationTeachingPlans");

            migrationBuilder.DropForeignKey(
                name: "FK_EduPlans_PublicRevisionId",
                table: "AppEducationTeachingPlans");

            migrationBuilder.DropForeignKey(
                name: "FK_EduPlans_PublishedRevisionId",
                table: "AppEducationTeachingPlans");

            migrationBuilder.DropTable(
                name: "AppEducationTeachingPlanQuestionExposures");

            migrationBuilder.DropTable(
                name: "AppEducationTeachingPlanRevisions");

            migrationBuilder.DropTable(
                name: "AppEducationTeachingPlans");
        }
    }
}
