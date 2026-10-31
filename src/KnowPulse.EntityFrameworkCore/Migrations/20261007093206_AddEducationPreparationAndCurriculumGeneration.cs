using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddEducationPreparationAndCurriculumGeneration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurriculumBindingIdsJson",
                table: "AppSchoolKnowledgeShares",
                type: "character varying(117002)",
                maxLength: 117002,
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "CurriculumBindingIdsJson",
                table: "AppQuestionSources",
                type: "character varying(200000)",
                maxLength: 200000,
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<Guid>(
                name: "SchoolKnowledgeShareId",
                table: "AppQuestionSources",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TextbookKnowledgePointMappingIdsJson",
                table: "AppQuestionSources",
                type: "character varying(200000)",
                maxLength: 200000,
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<bool>(
                name: "CurriculumConfirmed",
                table: "AppQuestions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "CurriculumConfirmedAtUtc",
                table: "AppQuestions",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CurriculumConfirmedByUserId",
                table: "AppQuestions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurriculumPointWeightsJson",
                table: "AppQuestions",
                type: "character varying(200000)",
                maxLength: 200000,
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<decimal>(
                name: "CurriculumScore",
                table: "AppQuestions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurriculumSnapshotJson",
                table: "AppQuestions",
                type: "character varying(200000)",
                maxLength: 200000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurriculumSourceReferencesJson",
                table: "AppQuestions",
                type: "character varying(200000)",
                maxLength: 200000,
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "CurriculumSuggestedMappingIdsJson",
                table: "AppQuestions",
                type: "character varying(200000)",
                maxLength: 200000,
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "CurriculumSnapshotJson",
                table: "AppExamPapers",
                type: "character varying(200000)",
                maxLength: 200000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SaveOperationRequestHash",
                table: "AppExamPapers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CurriculumConfirmedAtUtc",
                table: "AppExamPaperQuestions",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CurriculumConfirmedByUserId",
                table: "AppExamPaperQuestions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurriculumPointWeightsJson",
                table: "AppExamPaperQuestions",
                type: "character varying(200000)",
                maxLength: 200000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurriculumSnapshotJson",
                table: "AppExamPaperQuestions",
                type: "character varying(200000)",
                maxLength: 200000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurriculumSourceReferencesJson",
                table: "AppExamPaperQuestions",
                type: "character varying(200000)",
                maxLength: 200000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                table: "AppAiQuestionGenerationTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CurriculumScopeJson",
                table: "AppAiQuestionGenerationTasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurriculumSnapshotJson",
                table: "AppAiQuestionGenerationTasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastHeartbeatAtUtc",
                table: "AppAiQuestionGenerationTasks",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAtUtc",
                table: "AppAiQuestionGenerationTasks",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Purpose",
                table: "AppAiQuestionGenerationTasks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "Practice");

            migrationBuilder.AddColumn<string>(
                name: "RequestFingerprint",
                table: "AppAiQuestionGenerationTasks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RunId",
                table: "AppAiQuestionGenerationTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RunNumber",
                table: "AppAiQuestionGenerationTasks",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<decimal>(
                name: "Score",
                table: "AppAiQuestionGenerationTasks",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<int>(
                name: "ContentRevision",
                table: "AppAiQuestionCandidates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CurriculumMappingIdsJson",
                table: "AppAiQuestionCandidates",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "CurriculumSnapshotJson",
                table: "AppAiQuestionCandidates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasValidatedCurriculumMapping",
                table: "AppAiQuestionCandidates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsEdited",
                table: "AppAiQuestionCandidates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "OutputOrdinal",
                table: "AppAiQuestionCandidates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Purpose",
                table: "AppAiQuestionCandidates",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "Practice");

            migrationBuilder.AddColumn<int>(
                name: "RunNumber",
                table: "AppAiQuestionCandidates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "Score",
                table: "AppAiQuestionCandidates",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "SubmittedCitationChunkIdsJson",
                table: "AppAiQuestionCandidates",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "CurriculumBindingIdsJson",
                table: "AppAiQuestionCandidateCitations",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<Guid>(
                name: "SchoolKnowledgeShareId",
                table: "AppAiQuestionCandidateCitations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TextbookKnowledgePointMappingIdsJson",
                table: "AppAiQuestionCandidateCitations",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_AppTextbookKnowledgePointMappings_Id_TextbookVersionId",
                table: "AppTextbookKnowledgePointMappings",
                columns: new[] { "Id", "TextbookVersionId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_AppTextbookKnowledgePointMappings_Id_TextbookVersionId_Chap~",
                table: "AppTextbookKnowledgePointMappings",
                columns: new[] { "Id", "TextbookVersionId", "ChapterId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_AppKnowledgeChunks_Id_KnowledgeVersionId",
                table: "AppKnowledgeChunks",
                columns: new[] { "Id", "KnowledgeVersionId" });

            migrationBuilder.CreateTable(
                name: "AppKnowledgeMaterialCurriculumBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LibraryId = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeChunkId = table.Column<Guid>(type: "uuid", nullable: true),
                    TextbookVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TextbookKnowledgePointMappingId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurriculumSnapshotJson = table.Column<string>(type: "character varying(200000)", maxLength: 200000, nullable: false),
                    MaterialTitle = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    WholeMaterialAppliesToMapping = table.Column<bool>(type: "boolean", nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
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
                    table.PrimaryKey("PK_AppKnowledgeMaterialCurriculumBindings", x => x.Id);
                    table.UniqueConstraint("AK_AppKnowledgeMaterialCurriculumBindings_Id_KnowledgeVersionId", x => new { x.Id, x.KnowledgeVersionId });
                    table.ForeignKey(
                        name: "FK_AppKnowledgeMaterialCurriculumBindings_AppKnowledgeChunks_K~",
                        columns: x => new { x.KnowledgeChunkId, x.KnowledgeVersionId },
                        principalTable: "AppKnowledgeChunks",
                        principalColumns: new[] { "Id", "KnowledgeVersionId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppKnowledgeMaterialCurriculumBindings_AppKnowledgeItems_Kn~",
                        column: x => x.KnowledgeItemId,
                        principalTable: "AppKnowledgeItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppKnowledgeMaterialCurriculumBindings_AppKnowledgeLibrarie~",
                        columns: x => new { x.LibraryId, x.OwnerUserId },
                        principalTable: "AppKnowledgeLibraries",
                        principalColumns: new[] { "Id", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppKnowledgeMaterialCurriculumBindings_AppKnowledgeVersions~",
                        columns: x => new { x.KnowledgeVersionId, x.KnowledgeItemId },
                        principalTable: "AppKnowledgeVersions",
                        principalColumns: new[] { "Id", "KnowledgeItemId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppKnowledgeMaterialCurriculumBindings_AppTextbookKnowledge~",
                        columns: x => new { x.TextbookKnowledgePointMappingId, x.TextbookVersionId },
                        principalTable: "AppTextbookKnowledgePointMappings",
                        principalColumns: new[] { "Id", "TextbookVersionId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppLessonPreparationReferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    MaterialBindingId = table.Column<Guid>(type: "uuid", nullable: true),
                    KnowledgeVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    KnowledgeChunkId = table.Column<Guid>(type: "uuid", nullable: true),
                    QuestionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExamPaperId = table.Column<Guid>(type: "uuid", nullable: true),
                    TitleSnapshot = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SnapshotVersion = table.Column<int>(type: "integer", nullable: true),
                    SourceConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
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
                    table.PrimaryKey("PK_AppLessonPreparationReferences", x => x.Id);
                    table.CheckConstraint("CK_AppLessonPreparationReferences_Source", "(\"Kind\" = 0 AND \"MaterialBindingId\" IS NOT NULL AND \"KnowledgeVersionId\" IS NOT NULL AND \"QuestionId\" IS NULL AND \"ExamPaperId\" IS NULL) OR (\"Kind\" = 1 AND \"QuestionId\" IS NOT NULL AND \"MaterialBindingId\" IS NULL AND \"KnowledgeVersionId\" IS NULL AND \"KnowledgeChunkId\" IS NULL AND \"ExamPaperId\" IS NULL) OR (\"Kind\" = 2 AND \"ExamPaperId\" IS NOT NULL AND \"MaterialBindingId\" IS NULL AND \"KnowledgeVersionId\" IS NULL AND \"KnowledgeChunkId\" IS NULL AND \"QuestionId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_AppLessonPreparationReferences_AppExamPapers_ExamPaperId",
                        column: x => x.ExamPaperId,
                        principalTable: "AppExamPapers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppLessonPreparationReferences_AppKnowledgeChunks_Knowledge~",
                        columns: x => new { x.KnowledgeChunkId, x.KnowledgeVersionId },
                        principalTable: "AppKnowledgeChunks",
                        principalColumns: new[] { "Id", "KnowledgeVersionId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppLessonPreparationReferences_AppKnowledgeMaterialCurricul~",
                        columns: x => new { x.MaterialBindingId, x.KnowledgeVersionId },
                        principalTable: "AppKnowledgeMaterialCurriculumBindings",
                        principalColumns: new[] { "Id", "KnowledgeVersionId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppLessonPreparationReferences_AppKnowledgeVersions_Knowled~",
                        column: x => x.KnowledgeVersionId,
                        principalTable: "AppKnowledgeVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppLessonPreparationReferences_AppQuestions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "AppQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppLessonPreparationRevisionMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TextbookVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TextbookKnowledgePointMappingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChapterId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_AppLessonPreparationRevisionMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppLessonPreparationRevisionMappings_AppTextbookKnowledgePo~",
                        columns: x => new { x.TextbookKnowledgePointMappingId, x.TextbookVersionId, x.ChapterId },
                        principalTable: "AppTextbookKnowledgePointMappings",
                        principalColumns: new[] { "Id", "TextbookVersionId", "ChapterId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppLessonPreparationRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreparationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    TextbookVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurriculumSnapshotJson = table.Column<string>(type: "character varying(200000)", maxLength: 200000, nullable: false),
                    Objectives = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    Reflection = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
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
                    table.PrimaryKey("PK_AppLessonPreparationRevisions", x => x.Id);
                    table.UniqueConstraint("AK_AppLessonPreparationRevisions_Id_PreparationId", x => new { x.Id, x.PreparationId });
                    table.UniqueConstraint("AK_AppLessonPreparationRevisions_Id_TextbookVersionId", x => new { x.Id, x.TextbookVersionId });
                    table.ForeignKey(
                        name: "FK_AppLessonPreparationRevisions_AppTextbookVersions_TextbookV~",
                        columns: x => new { x.TextbookVersionId, x.SchoolId },
                        principalTable: "AppTextbookVersions",
                        principalColumns: new[] { "Id", "SchoolId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppLessonPreparations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LibraryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CurrentRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentRevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    CopiedFromRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_AppLessonPreparations", x => x.Id);
                    table.UniqueConstraint("AK_AppLessonPreparations_Id_OwnerUserId", x => new { x.Id, x.OwnerUserId });
                    table.ForeignKey(
                        name: "FK_AppLessonPreparations_AppKnowledgeLibraries_LibraryId_Owner~",
                        columns: x => new { x.LibraryId, x.OwnerUserId },
                        principalTable: "AppKnowledgeLibraries",
                        principalColumns: new[] { "Id", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppLessonPreparations_AppLessonPreparationRevisions_CopiedF~",
                        column: x => x.CopiedFromRevisionId,
                        principalTable: "AppLessonPreparationRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppLessonPreparations_AppLessonPreparationRevisions_Current~",
                        columns: x => new { x.CurrentRevisionId, x.Id },
                        principalTable: "AppLessonPreparationRevisions",
                        principalColumns: new[] { "Id", "PreparationId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppAiQuestionGenerationTasks_Status_NextAttemptAtUtc_LastHe~",
                table: "AppAiQuestionGenerationTasks",
                columns: new[] { "Status", "NextAttemptAtUtc", "LastHeartbeatAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppAiQuestionCandidates_TaskId_RunNumber_OutputOrdinal",
                table: "AppAiQuestionCandidates",
                columns: new[] { "TaskId", "RunNumber", "OutputOrdinal" },
                unique: true,
                filter: "\"RunNumber\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_AppKnowledgeMaterialCurriculumBindings_KnowledgeChunkId_Kno~",
                table: "AppKnowledgeMaterialCurriculumBindings",
                columns: new[] { "KnowledgeChunkId", "KnowledgeVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppKnowledgeMaterialCurriculumBindings_KnowledgeItemId",
                table: "AppKnowledgeMaterialCurriculumBindings",
                column: "KnowledgeItemId");

            migrationBuilder.CreateIndex(
                name: "IX_AppKnowledgeMaterialCurriculumBindings_KnowledgeVersionId_~1",
                table: "AppKnowledgeMaterialCurriculumBindings",
                columns: new[] { "KnowledgeVersionId", "KnowledgeChunkId", "TextbookKnowledgePointMappingId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppKnowledgeMaterialCurriculumBindings_KnowledgeVersionId_K~",
                table: "AppKnowledgeMaterialCurriculumBindings",
                columns: new[] { "KnowledgeVersionId", "KnowledgeItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppKnowledgeMaterialCurriculumBindings_LibraryId_OwnerUserId",
                table: "AppKnowledgeMaterialCurriculumBindings",
                columns: new[] { "LibraryId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppKnowledgeMaterialCurriculumBindings_TenantId_OwnerUserId~",
                table: "AppKnowledgeMaterialCurriculumBindings",
                columns: new[] { "TenantId", "OwnerUserId", "TextbookVersionId", "EndedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppKnowledgeMaterialCurriculumBindings_TextbookKnowledgePoi~",
                table: "AppKnowledgeMaterialCurriculumBindings",
                columns: new[] { "TextbookKnowledgePointMappingId", "TextbookVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppLessonPreparationReferences_ExamPaperId",
                table: "AppLessonPreparationReferences",
                column: "ExamPaperId");

            migrationBuilder.CreateIndex(
                name: "IX_AppLessonPreparationReferences_KnowledgeChunkId_KnowledgeVe~",
                table: "AppLessonPreparationReferences",
                columns: new[] { "KnowledgeChunkId", "KnowledgeVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppLessonPreparationReferences_KnowledgeVersionId",
                table: "AppLessonPreparationReferences",
                column: "KnowledgeVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_AppLessonPreparationReferences_MaterialBindingId_KnowledgeV~",
                table: "AppLessonPreparationReferences",
                columns: new[] { "MaterialBindingId", "KnowledgeVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppLessonPreparationReferences_QuestionId",
                table: "AppLessonPreparationReferences",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_AppLessonPreparationReferences_RevisionId_ExamPaperId",
                table: "AppLessonPreparationReferences",
                columns: new[] { "RevisionId", "ExamPaperId" },
                unique: true,
                filter: "\"ExamPaperId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppLessonPreparationReferences_RevisionId_MaterialBindingId",
                table: "AppLessonPreparationReferences",
                columns: new[] { "RevisionId", "MaterialBindingId" },
                unique: true,
                filter: "\"MaterialBindingId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppLessonPreparationReferences_RevisionId_QuestionId",
                table: "AppLessonPreparationReferences",
                columns: new[] { "RevisionId", "QuestionId" },
                unique: true,
                filter: "\"QuestionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppLessonPreparationRevisionMappings_ChapterId",
                table: "AppLessonPreparationRevisionMappings",
                column: "ChapterId");

            migrationBuilder.CreateIndex(
                name: "IX_AppLessonPreparationRevisionMappings_RevisionId_TextbookKno~",
                table: "AppLessonPreparationRevisionMappings",
                columns: new[] { "RevisionId", "TextbookKnowledgePointMappingId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppLessonPreparationRevisionMappings_RevisionId_TextbookVer~",
                table: "AppLessonPreparationRevisionMappings",
                columns: new[] { "RevisionId", "TextbookVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppLessonPreparationRevisionMappings_TextbookKnowledgePoint~",
                table: "AppLessonPreparationRevisionMappings",
                columns: new[] { "TextbookKnowledgePointMappingId", "TextbookVersionId", "ChapterId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppLessonPreparationRevisions_PreparationId_OwnerUserId",
                table: "AppLessonPreparationRevisions",
                columns: new[] { "PreparationId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppLessonPreparationRevisions_PreparationId_RevisionNumber",
                table: "AppLessonPreparationRevisions",
                columns: new[] { "PreparationId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppLessonPreparationRevisions_TenantId_OwnerUserId_Textbook~",
                table: "AppLessonPreparationRevisions",
                columns: new[] { "TenantId", "OwnerUserId", "TextbookVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppLessonPreparationRevisions_TextbookVersionId_SchoolId",
                table: "AppLessonPreparationRevisions",
                columns: new[] { "TextbookVersionId", "SchoolId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppLessonPreparations_CopiedFromRevisionId",
                table: "AppLessonPreparations",
                column: "CopiedFromRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_AppLessonPreparations_CurrentRevisionId_Id",
                table: "AppLessonPreparations",
                columns: new[] { "CurrentRevisionId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AppLessonPreparations_LibraryId_OwnerUserId",
                table: "AppLessonPreparations",
                columns: new[] { "LibraryId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppLessonPreparations_TenantId_OwnerUserId_UpdatedAtUtc",
                table: "AppLessonPreparations",
                columns: new[] { "TenantId", "OwnerUserId", "UpdatedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_AppLessonPreparationReferences_AppLessonPreparationRevision~",
                table: "AppLessonPreparationReferences",
                column: "RevisionId",
                principalTable: "AppLessonPreparationRevisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AppLessonPreparationRevisionMappings_AppLessonPreparationRe~",
                table: "AppLessonPreparationRevisionMappings",
                columns: new[] { "RevisionId", "TextbookVersionId" },
                principalTable: "AppLessonPreparationRevisions",
                principalColumns: new[] { "Id", "TextbookVersionId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AppLessonPreparationRevisions_AppLessonPreparations_Prepara~",
                table: "AppLessonPreparationRevisions",
                columns: new[] { "PreparationId", "OwnerUserId" },
                principalTable: "AppLessonPreparations",
                principalColumns: new[] { "Id", "OwnerUserId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppLessonPreparations_AppLessonPreparationRevisions_CopiedF~",
                table: "AppLessonPreparations");

            migrationBuilder.DropForeignKey(
                name: "FK_AppLessonPreparations_AppLessonPreparationRevisions_Current~",
                table: "AppLessonPreparations");

            migrationBuilder.DropTable(
                name: "AppLessonPreparationReferences");

            migrationBuilder.DropTable(
                name: "AppLessonPreparationRevisionMappings");

            migrationBuilder.DropTable(
                name: "AppKnowledgeMaterialCurriculumBindings");

            migrationBuilder.DropTable(
                name: "AppLessonPreparationRevisions");

            migrationBuilder.DropTable(
                name: "AppLessonPreparations");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_AppTextbookKnowledgePointMappings_Id_TextbookVersionId",
                table: "AppTextbookKnowledgePointMappings");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_AppTextbookKnowledgePointMappings_Id_TextbookVersionId_Chap~",
                table: "AppTextbookKnowledgePointMappings");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_AppKnowledgeChunks_Id_KnowledgeVersionId",
                table: "AppKnowledgeChunks");

            migrationBuilder.DropIndex(
                name: "IX_AppAiQuestionGenerationTasks_Status_NextAttemptAtUtc_LastHe~",
                table: "AppAiQuestionGenerationTasks");

            migrationBuilder.DropIndex(
                name: "IX_AppAiQuestionCandidates_TaskId_RunNumber_OutputOrdinal",
                table: "AppAiQuestionCandidates");

            migrationBuilder.DropColumn(
                name: "CurriculumBindingIdsJson",
                table: "AppSchoolKnowledgeShares");

            migrationBuilder.DropColumn(
                name: "CurriculumBindingIdsJson",
                table: "AppQuestionSources");

            migrationBuilder.DropColumn(
                name: "SchoolKnowledgeShareId",
                table: "AppQuestionSources");

            migrationBuilder.DropColumn(
                name: "TextbookKnowledgePointMappingIdsJson",
                table: "AppQuestionSources");

            migrationBuilder.DropColumn(
                name: "CurriculumConfirmed",
                table: "AppQuestions");

            migrationBuilder.DropColumn(
                name: "CurriculumConfirmedAtUtc",
                table: "AppQuestions");

            migrationBuilder.DropColumn(
                name: "CurriculumConfirmedByUserId",
                table: "AppQuestions");

            migrationBuilder.DropColumn(
                name: "CurriculumPointWeightsJson",
                table: "AppQuestions");

            migrationBuilder.DropColumn(
                name: "CurriculumScore",
                table: "AppQuestions");

            migrationBuilder.DropColumn(
                name: "CurriculumSnapshotJson",
                table: "AppQuestions");

            migrationBuilder.DropColumn(
                name: "CurriculumSourceReferencesJson",
                table: "AppQuestions");

            migrationBuilder.DropColumn(
                name: "CurriculumSuggestedMappingIdsJson",
                table: "AppQuestions");

            migrationBuilder.DropColumn(
                name: "CurriculumSnapshotJson",
                table: "AppExamPapers");

            migrationBuilder.DropColumn(
                name: "SaveOperationRequestHash",
                table: "AppExamPapers");

            migrationBuilder.DropColumn(
                name: "CurriculumConfirmedAtUtc",
                table: "AppExamPaperQuestions");

            migrationBuilder.DropColumn(
                name: "CurriculumConfirmedByUserId",
                table: "AppExamPaperQuestions");

            migrationBuilder.DropColumn(
                name: "CurriculumPointWeightsJson",
                table: "AppExamPaperQuestions");

            migrationBuilder.DropColumn(
                name: "CurriculumSnapshotJson",
                table: "AppExamPaperQuestions");

            migrationBuilder.DropColumn(
                name: "CurriculumSourceReferencesJson",
                table: "AppExamPaperQuestions");

            migrationBuilder.DropColumn(
                name: "AttemptCount",
                table: "AppAiQuestionGenerationTasks");

            migrationBuilder.DropColumn(
                name: "CurriculumScopeJson",
                table: "AppAiQuestionGenerationTasks");

            migrationBuilder.DropColumn(
                name: "CurriculumSnapshotJson",
                table: "AppAiQuestionGenerationTasks");

            migrationBuilder.DropColumn(
                name: "LastHeartbeatAtUtc",
                table: "AppAiQuestionGenerationTasks");

            migrationBuilder.DropColumn(
                name: "NextAttemptAtUtc",
                table: "AppAiQuestionGenerationTasks");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "AppAiQuestionGenerationTasks");

            migrationBuilder.DropColumn(
                name: "RequestFingerprint",
                table: "AppAiQuestionGenerationTasks");

            migrationBuilder.DropColumn(
                name: "RunId",
                table: "AppAiQuestionGenerationTasks");

            migrationBuilder.DropColumn(
                name: "RunNumber",
                table: "AppAiQuestionGenerationTasks");

            migrationBuilder.DropColumn(
                name: "Score",
                table: "AppAiQuestionGenerationTasks");

            migrationBuilder.DropColumn(
                name: "ContentRevision",
                table: "AppAiQuestionCandidates");

            migrationBuilder.DropColumn(
                name: "CurriculumMappingIdsJson",
                table: "AppAiQuestionCandidates");

            migrationBuilder.DropColumn(
                name: "CurriculumSnapshotJson",
                table: "AppAiQuestionCandidates");

            migrationBuilder.DropColumn(
                name: "HasValidatedCurriculumMapping",
                table: "AppAiQuestionCandidates");

            migrationBuilder.DropColumn(
                name: "IsEdited",
                table: "AppAiQuestionCandidates");

            migrationBuilder.DropColumn(
                name: "OutputOrdinal",
                table: "AppAiQuestionCandidates");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "AppAiQuestionCandidates");

            migrationBuilder.DropColumn(
                name: "RunNumber",
                table: "AppAiQuestionCandidates");

            migrationBuilder.DropColumn(
                name: "Score",
                table: "AppAiQuestionCandidates");

            migrationBuilder.DropColumn(
                name: "SubmittedCitationChunkIdsJson",
                table: "AppAiQuestionCandidates");

            migrationBuilder.DropColumn(
                name: "CurriculumBindingIdsJson",
                table: "AppAiQuestionCandidateCitations");

            migrationBuilder.DropColumn(
                name: "SchoolKnowledgeShareId",
                table: "AppAiQuestionCandidateCitations");

            migrationBuilder.DropColumn(
                name: "TextbookKnowledgePointMappingIdsJson",
                table: "AppAiQuestionCandidateCitations");
        }
    }
}
