using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalOrganizationAndEducationCurriculum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaterialType",
                table: "AppKnowledgeItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaterialYear",
                table: "AppKnowledgeItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PersonalTags",
                table: "AppKnowledgeItems",
                type: "character varying(1301)",
                maxLength: 1301,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubjectName",
                table: "AppKnowledgeItems",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_AppTeachingAssignments_Id_SchoolId_TeacherUserId",
                table: "AppTeachingAssignments",
                columns: new[] { "Id", "SchoolId", "TeacherUserId" });

            migrationBuilder.CreateTable(
                name: "AppCourseKnowledgePoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    EducationSubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CurrentRevisionNumber = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_AppCourseKnowledgePoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppCourseKnowledgePoints_AppEducationSubjects_EducationSubj~",
                        column: x => x.EducationSubjectId,
                        principalTable: "AppEducationSubjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppCourseKnowledgePoints_AppSchools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "AppSchools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppTextbookVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    EducationSubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CatalogCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CatalogRevision = table.Column<int>(type: "integer", nullable: false),
                    PreviousVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Grade = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Publisher = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Edition = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Volume = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    RetiredAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
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
                    table.PrimaryKey("PK_AppTextbookVersions", x => x.Id);
                    table.UniqueConstraint("AK_AppTextbookVersions_Id_SchoolId", x => new { x.Id, x.SchoolId });
                    table.ForeignKey(
                        name: "FK_AppTextbookVersions_AppEducationSubjects_EducationSubjectId",
                        column: x => x.EducationSubjectId,
                        principalTable: "AppEducationSubjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppTextbookVersions_AppSchools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "AppSchools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppTextbookVersions_AppTextbookVersions_PreviousVersionId_S~",
                        columns: x => new { x.PreviousVersionId, x.SchoolId },
                        principalTable: "AppTextbookVersions",
                        principalColumns: new[] { "Id", "SchoolId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppCourseKnowledgePointRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    KnowledgePointId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ParentRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_AppCourseKnowledgePointRevisions", x => x.Id);
                    table.UniqueConstraint("AK_AppCourseKnowledgePointRevisions_Id_KnowledgePointId", x => new { x.Id, x.KnowledgePointId });
                    table.ForeignKey(
                        name: "FK_AppCourseKnowledgePointRevisions_AppCourseKnowledgePointRev~",
                        column: x => x.ParentRevisionId,
                        principalTable: "AppCourseKnowledgePointRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppCourseKnowledgePointRevisions_AppCourseKnowledgePoints_K~",
                        column: x => x.KnowledgePointId,
                        principalTable: "AppCourseKnowledgePoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppTeacherTextbookSelections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeacherUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeachingAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TextbookVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SelectedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
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
                    table.PrimaryKey("PK_AppTeacherTextbookSelections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppTeacherTextbookSelections_AbpUsers_TeacherUserId",
                        column: x => x.TeacherUserId,
                        principalTable: "AbpUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppTeacherTextbookSelections_AppTeachingAssignments_Teachin~",
                        columns: x => new { x.TeachingAssignmentId, x.SchoolId, x.TeacherUserId },
                        principalTable: "AppTeachingAssignments",
                        principalColumns: new[] { "Id", "SchoolId", "TeacherUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppTeacherTextbookSelections_AppTextbookVersions_TextbookVe~",
                        columns: x => new { x.TextbookVersionId, x.SchoolId },
                        principalTable: "AppTextbookVersions",
                        principalColumns: new[] { "Id", "SchoolId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppTextbookChapters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TextbookVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentChapterId = table.Column<Guid>(type: "uuid", nullable: true),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_AppTextbookChapters", x => x.Id);
                    table.UniqueConstraint("AK_AppTextbookChapters_Id_TextbookVersionId", x => new { x.Id, x.TextbookVersionId });
                    table.ForeignKey(
                        name: "FK_AppTextbookChapters_AppTextbookChapters_ParentChapterId_Tex~",
                        columns: x => new { x.ParentChapterId, x.TextbookVersionId },
                        principalTable: "AppTextbookChapters",
                        principalColumns: new[] { "Id", "TextbookVersionId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppTextbookChapters_AppTextbookVersions_TextbookVersionId",
                        column: x => x.TextbookVersionId,
                        principalTable: "AppTextbookVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppTextbookKnowledgePointMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TextbookVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChapterId = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgePointId = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgePointRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfirmedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
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
                    table.PrimaryKey("PK_AppTextbookKnowledgePointMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppTextbookKnowledgePointMappings_AppCourseKnowledgePointRe~",
                        columns: x => new { x.KnowledgePointRevisionId, x.KnowledgePointId },
                        principalTable: "AppCourseKnowledgePointRevisions",
                        principalColumns: new[] { "Id", "KnowledgePointId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppTextbookKnowledgePointMappings_AppTextbookChapters_Chapt~",
                        columns: x => new { x.ChapterId, x.TextbookVersionId },
                        principalTable: "AppTextbookChapters",
                        principalColumns: new[] { "Id", "TextbookVersionId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppKnowledgeItems_TenantId_OwnerUserId_MaterialType_Materia~",
                table: "AppKnowledgeItems",
                columns: new[] { "TenantId", "OwnerUserId", "MaterialType", "MaterialYear" });

            migrationBuilder.CreateIndex(
                name: "IX_AppCourseKnowledgePointRevisions_KnowledgePointId_RevisionN~",
                table: "AppCourseKnowledgePointRevisions",
                columns: new[] { "KnowledgePointId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppCourseKnowledgePointRevisions_ParentRevisionId",
                table: "AppCourseKnowledgePointRevisions",
                column: "ParentRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_AppCourseKnowledgePoints_EducationSubjectId",
                table: "AppCourseKnowledgePoints",
                column: "EducationSubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_AppCourseKnowledgePoints_SchoolId_EducationSubjectId_Code",
                table: "AppCourseKnowledgePoints",
                columns: new[] { "SchoolId", "EducationSubjectId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppTeacherTextbookSelections_TeacherUserId",
                table: "AppTeacherTextbookSelections",
                column: "TeacherUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AppTeacherTextbookSelections_TeachingAssignmentId",
                table: "AppTeacherTextbookSelections",
                column: "TeachingAssignmentId",
                unique: true,
                filter: "NOT \"IsDeleted\" AND \"EndedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppTeacherTextbookSelections_TeachingAssignmentId_SchoolId_~",
                table: "AppTeacherTextbookSelections",
                columns: new[] { "TeachingAssignmentId", "SchoolId", "TeacherUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppTeacherTextbookSelections_TenantId_TeacherUserId_Selecte~",
                table: "AppTeacherTextbookSelections",
                columns: new[] { "TenantId", "TeacherUserId", "SelectedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppTeacherTextbookSelections_TextbookVersionId_SchoolId",
                table: "AppTeacherTextbookSelections",
                columns: new[] { "TextbookVersionId", "SchoolId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppTextbookChapters_ParentChapterId_TextbookVersionId",
                table: "AppTextbookChapters",
                columns: new[] { "ParentChapterId", "TextbookVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppTextbookChapters_TextbookVersionId_Code",
                table: "AppTextbookChapters",
                columns: new[] { "TextbookVersionId", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_AppTextbookKnowledgePointMappings_ChapterId_TextbookVersion~",
                table: "AppTextbookKnowledgePointMappings",
                columns: new[] { "ChapterId", "TextbookVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppTextbookKnowledgePointMappings_KnowledgePointRevisionId_~",
                table: "AppTextbookKnowledgePointMappings",
                columns: new[] { "KnowledgePointRevisionId", "KnowledgePointId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppTextbookKnowledgePointMappings_TextbookVersionId_Chapter~",
                table: "AppTextbookKnowledgePointMappings",
                columns: new[] { "TextbookVersionId", "ChapterId", "KnowledgePointId" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_AppTextbookVersions_EducationSubjectId",
                table: "AppTextbookVersions",
                column: "EducationSubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_AppTextbookVersions_PreviousVersionId_SchoolId",
                table: "AppTextbookVersions",
                columns: new[] { "PreviousVersionId", "SchoolId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppTextbookVersions_SchoolId_CatalogCode_CatalogRevision",
                table: "AppTextbookVersions",
                columns: new[] { "SchoolId", "CatalogCode", "CatalogRevision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppTeacherTextbookSelections");

            migrationBuilder.DropTable(
                name: "AppTextbookKnowledgePointMappings");

            migrationBuilder.DropTable(
                name: "AppCourseKnowledgePointRevisions");

            migrationBuilder.DropTable(
                name: "AppTextbookChapters");

            migrationBuilder.DropTable(
                name: "AppCourseKnowledgePoints");

            migrationBuilder.DropTable(
                name: "AppTextbookVersions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_AppTeachingAssignments_Id_SchoolId_TeacherUserId",
                table: "AppTeachingAssignments");

            migrationBuilder.DropIndex(
                name: "IX_AppKnowledgeItems_TenantId_OwnerUserId_MaterialType_Materia~",
                table: "AppKnowledgeItems");

            migrationBuilder.DropColumn(
                name: "MaterialType",
                table: "AppKnowledgeItems");

            migrationBuilder.DropColumn(
                name: "MaterialYear",
                table: "AppKnowledgeItems");

            migrationBuilder.DropColumn(
                name: "PersonalTags",
                table: "AppKnowledgeItems");

            migrationBuilder.DropColumn(
                name: "SubjectName",
                table: "AppKnowledgeItems");
        }
    }
}
