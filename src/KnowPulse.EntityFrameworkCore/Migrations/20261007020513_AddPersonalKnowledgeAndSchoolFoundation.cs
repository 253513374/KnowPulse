using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalKnowledgeAndSchoolFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AbpBackgroundJobs_IsAbandoned_NextTryTime",
                table: "AbpBackgroundJobs");

            migrationBuilder.AddColumn<Guid>(
                name: "PersonalOwnerUserId",
                table: "AppRagEvaluationRuns",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PersonalOwnerUserId",
                table: "AppRagEvaluationCases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PersonalOwnerUserId",
                table: "AppRagCallLogs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PersonalOwnerUserId",
                table: "AppQuestions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LibraryId",
                table: "AppKnowledgeItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerUserId",
                table: "AppKnowledgeItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PersonalOwnerUserId",
                table: "AppExamPapers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PersonalOwnerUserId",
                table: "AppAiQuestionGenerationTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletionTime",
                table: "AbpBackgroundJobs",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppEducationSubjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
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
                    table.PrimaryKey("PK_AppEducationSubjects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppKnowledgeLibraries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
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
                    table.PrimaryKey("PK_AppKnowledgeLibraries", x => x.Id);
                    table.UniqueConstraint("AK_AppKnowledgeLibraries_Id_OwnerUserId", x => new { x.Id, x.OwnerUserId });
                });

            migrationBuilder.CreateTable(
                name: "AppSchools",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ConfigurationRevision = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_AppSchools", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppAcademicTerms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
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
                    table.PrimaryKey("PK_AppAcademicTerms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppAcademicTerms_AppSchools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "AppSchools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppSchoolMemberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EffectiveToUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
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
                    table.PrimaryKey("PK_AppSchoolMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppSchoolMemberships_AbpUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AbpUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppSchoolMemberships_AppSchools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "AppSchools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppSchoolClasses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademicTermId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Grade = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_AppSchoolClasses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppSchoolClasses_AppAcademicTerms_AcademicTermId",
                        column: x => x.AcademicTermId,
                        principalTable: "AppAcademicTerms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppSchoolClasses_AppSchools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "AppSchools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppStudentEnrollments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolClassId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EffectiveToUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
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
                    table.PrimaryKey("PK_AppStudentEnrollments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppStudentEnrollments_AbpUsers_StudentUserId",
                        column: x => x.StudentUserId,
                        principalTable: "AbpUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppStudentEnrollments_AppSchoolClasses_SchoolClassId",
                        column: x => x.SchoolClassId,
                        principalTable: "AppSchoolClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppStudentEnrollments_AppSchools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "AppSchools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppTeachingAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademicTermId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolClassId = table.Column<Guid>(type: "uuid", nullable: false),
                    EducationSubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeacherUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EffectiveToUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
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
                    table.PrimaryKey("PK_AppTeachingAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppTeachingAssignments_AbpUsers_TeacherUserId",
                        column: x => x.TeacherUserId,
                        principalTable: "AbpUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppTeachingAssignments_AppAcademicTerms_AcademicTermId",
                        column: x => x.AcademicTermId,
                        principalTable: "AppAcademicTerms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppTeachingAssignments_AppEducationSubjects_EducationSubjec~",
                        column: x => x.EducationSubjectId,
                        principalTable: "AppEducationSubjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppTeachingAssignments_AppSchoolClasses_SchoolClassId",
                        column: x => x.SchoolClassId,
                        principalTable: "AppSchoolClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppTeachingAssignments_AppSchools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "AppSchools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppRagEvaluationRuns_TenantId_PersonalOwnerUserId",
                table: "AppRagEvaluationRuns",
                columns: new[] { "TenantId", "PersonalOwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppRagEvaluationCases_TenantId_PersonalOwnerUserId",
                table: "AppRagEvaluationCases",
                columns: new[] { "TenantId", "PersonalOwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppRagCallLogs_TenantId_PersonalOwnerUserId",
                table: "AppRagCallLogs",
                columns: new[] { "TenantId", "PersonalOwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppQuestions_TenantId_PersonalOwnerUserId",
                table: "AppQuestions",
                columns: new[] { "TenantId", "PersonalOwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppKnowledgeItems_LibraryId_OwnerUserId",
                table: "AppKnowledgeItems",
                columns: new[] { "LibraryId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppKnowledgeItems_TenantId_OwnerUserId_LibraryId",
                table: "AppKnowledgeItems",
                columns: new[] { "TenantId", "OwnerUserId", "LibraryId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppKnowledgeItems_PersonalOwnership",
                table: "AppKnowledgeItems",
                sql: "(\"LibraryId\" IS NULL AND \"OwnerUserId\" IS NULL) OR (\"LibraryId\" IS NOT NULL AND \"OwnerUserId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_AppAiQuestionGenerationTasks_TenantId_PersonalOwnerUserId",
                table: "AppAiQuestionGenerationTasks",
                columns: new[] { "TenantId", "PersonalOwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpBackgroundJobs_ApplicationName_CompletionTime_IsAbandone~",
                table: "AbpBackgroundJobs",
                columns: new[] { "ApplicationName", "CompletionTime", "IsAbandoned", "NextTryTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AppAcademicTerms_SchoolId_Code",
                table: "AppAcademicTerms",
                columns: new[] { "SchoolId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationSubjects_Code",
                table: "AppEducationSubjects",
                column: "Code",
                unique: true,
                filter: "\"TenantId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationSubjects_TenantId_Code",
                table: "AppEducationSubjects",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppKnowledgeLibraries_OwnerUserId",
                table: "AppKnowledgeLibraries",
                column: "OwnerUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppSchoolClasses_AcademicTermId",
                table: "AppSchoolClasses",
                column: "AcademicTermId");

            migrationBuilder.CreateIndex(
                name: "IX_AppSchoolClasses_SchoolId_AcademicTermId_Code",
                table: "AppSchoolClasses",
                columns: new[] { "SchoolId", "AcademicTermId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppSchoolMemberships_SchoolId_UserId_Role_EffectiveFromUtc",
                table: "AppSchoolMemberships",
                columns: new[] { "SchoolId", "UserId", "Role", "EffectiveFromUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppSchoolMemberships_TenantId_UserId_EffectiveFromUtc_Effec~",
                table: "AppSchoolMemberships",
                columns: new[] { "TenantId", "UserId", "EffectiveFromUtc", "EffectiveToUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppSchoolMemberships_UserId",
                table: "AppSchoolMemberships",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AppSchools_Code",
                table: "AppSchools",
                column: "Code",
                unique: true,
                filter: "\"TenantId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppSchools_TenantId_Code",
                table: "AppSchools",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppStudentEnrollments_SchoolClassId",
                table: "AppStudentEnrollments",
                column: "SchoolClassId");

            migrationBuilder.CreateIndex(
                name: "IX_AppStudentEnrollments_SchoolId_StudentUserId_SchoolClassId_~",
                table: "AppStudentEnrollments",
                columns: new[] { "SchoolId", "StudentUserId", "SchoolClassId", "EffectiveFromUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppStudentEnrollments_StudentUserId",
                table: "AppStudentEnrollments",
                column: "StudentUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AppStudentEnrollments_TenantId_SchoolId_StudentUserId_Effec~",
                table: "AppStudentEnrollments",
                columns: new[] { "TenantId", "SchoolId", "StudentUserId", "EffectiveFromUtc", "EffectiveToUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppTeachingAssignments_AcademicTermId",
                table: "AppTeachingAssignments",
                column: "AcademicTermId");

            migrationBuilder.CreateIndex(
                name: "IX_AppTeachingAssignments_EducationSubjectId",
                table: "AppTeachingAssignments",
                column: "EducationSubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_AppTeachingAssignments_SchoolClassId",
                table: "AppTeachingAssignments",
                column: "SchoolClassId");

            migrationBuilder.CreateIndex(
                name: "IX_AppTeachingAssignments_SchoolId_SchoolClassId_EducationSubj~",
                table: "AppTeachingAssignments",
                columns: new[] { "SchoolId", "SchoolClassId", "EducationSubjectId", "TeacherUserId", "EffectiveFromUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppTeachingAssignments_TeacherUserId",
                table: "AppTeachingAssignments",
                column: "TeacherUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AppTeachingAssignments_TenantId_TeacherUserId_EffectiveFrom~",
                table: "AppTeachingAssignments",
                columns: new[] { "TenantId", "TeacherUserId", "EffectiveFromUtc", "EffectiveToUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_AppKnowledgeItems_AppKnowledgeLibraries_LibraryId_OwnerUser~",
                table: "AppKnowledgeItems",
                columns: new[] { "LibraryId", "OwnerUserId" },
                principalTable: "AppKnowledgeLibraries",
                principalColumns: new[] { "Id", "OwnerUserId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppKnowledgeItems_AppKnowledgeLibraries_LibraryId_OwnerUser~",
                table: "AppKnowledgeItems");

            migrationBuilder.DropTable(
                name: "AppKnowledgeLibraries");

            migrationBuilder.DropTable(
                name: "AppSchoolMemberships");

            migrationBuilder.DropTable(
                name: "AppStudentEnrollments");

            migrationBuilder.DropTable(
                name: "AppTeachingAssignments");

            migrationBuilder.DropTable(
                name: "AppEducationSubjects");

            migrationBuilder.DropTable(
                name: "AppSchoolClasses");

            migrationBuilder.DropTable(
                name: "AppAcademicTerms");

            migrationBuilder.DropTable(
                name: "AppSchools");

            migrationBuilder.DropIndex(
                name: "IX_AppRagEvaluationRuns_TenantId_PersonalOwnerUserId",
                table: "AppRagEvaluationRuns");

            migrationBuilder.DropIndex(
                name: "IX_AppRagEvaluationCases_TenantId_PersonalOwnerUserId",
                table: "AppRagEvaluationCases");

            migrationBuilder.DropIndex(
                name: "IX_AppRagCallLogs_TenantId_PersonalOwnerUserId",
                table: "AppRagCallLogs");

            migrationBuilder.DropIndex(
                name: "IX_AppQuestions_TenantId_PersonalOwnerUserId",
                table: "AppQuestions");

            migrationBuilder.DropIndex(
                name: "IX_AppKnowledgeItems_LibraryId_OwnerUserId",
                table: "AppKnowledgeItems");

            migrationBuilder.DropIndex(
                name: "IX_AppKnowledgeItems_TenantId_OwnerUserId_LibraryId",
                table: "AppKnowledgeItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppKnowledgeItems_PersonalOwnership",
                table: "AppKnowledgeItems");

            migrationBuilder.DropIndex(
                name: "IX_AppAiQuestionGenerationTasks_TenantId_PersonalOwnerUserId",
                table: "AppAiQuestionGenerationTasks");

            migrationBuilder.DropIndex(
                name: "IX_AbpBackgroundJobs_ApplicationName_CompletionTime_IsAbandone~",
                table: "AbpBackgroundJobs");

            migrationBuilder.DropColumn(
                name: "PersonalOwnerUserId",
                table: "AppRagEvaluationRuns");

            migrationBuilder.DropColumn(
                name: "PersonalOwnerUserId",
                table: "AppRagEvaluationCases");

            migrationBuilder.DropColumn(
                name: "PersonalOwnerUserId",
                table: "AppRagCallLogs");

            migrationBuilder.DropColumn(
                name: "PersonalOwnerUserId",
                table: "AppQuestions");

            migrationBuilder.DropColumn(
                name: "LibraryId",
                table: "AppKnowledgeItems");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "AppKnowledgeItems");

            migrationBuilder.DropColumn(
                name: "PersonalOwnerUserId",
                table: "AppExamPapers");

            migrationBuilder.DropColumn(
                name: "PersonalOwnerUserId",
                table: "AppAiQuestionGenerationTasks");

            migrationBuilder.DropColumn(
                name: "CompletionTime",
                table: "AbpBackgroundJobs");

            migrationBuilder.CreateIndex(
                name: "IX_AbpBackgroundJobs_IsAbandoned_NextTryTime",
                table: "AbpBackgroundJobs",
                columns: new[] { "IsAbandoned", "NextTryTime" });
        }
    }
}
