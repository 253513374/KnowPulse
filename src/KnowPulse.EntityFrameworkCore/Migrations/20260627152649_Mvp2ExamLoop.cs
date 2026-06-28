using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class Mvp2ExamLoop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppExamPapers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    BuildMode = table.Column<int>(type: "integer", nullable: false),
                    ApplicablePositionId = table.Column<Guid>(type: "uuid", nullable: true),
                    QuestionCount = table.Column<int>(type: "integer", nullable: false),
                    TotalScore = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SnapshotVersion = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_AppExamPapers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppExamPaperQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaperId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    OriginalQuestionCreationSource = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Stem = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    OptionsJson = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    CorrectAnswersJson = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    Analysis = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    KnowledgePointsSnapshot = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    SourceSnapshot = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Score = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    MultipleChoiceScoringMode = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_AppExamPaperQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppExamPaperQuestions_AppExamPapers_PaperId",
                        column: x => x.PaperId,
                        principalTable: "AppExamPapers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AppExamPaperQuestions_AppQuestions_OriginalQuestionId",
                        column: x => x.OriginalQuestionId,
                        principalTable: "AppQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppExamPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    PaperId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EndTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    PassingScore = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    ResultVisibility = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_AppExamPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppExamPlans_AppExamPapers_PaperId",
                        column: x => x.PaperId,
                        principalTable: "AppExamPapers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppExamAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeType = table.Column<int>(type: "integer", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_AppExamAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppExamAssignments_AppExamPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "AppExamPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AppExamAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaperId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ExpiresTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    LastSavedTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    SubmittedTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ScoredTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    SubmitIdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
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
                    table.PrimaryKey("PK_AppExamAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppExamAttempts_AppExamPapers_PaperId",
                        column: x => x.PaperId,
                        principalTable: "AppExamPapers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppExamAttempts_AppExamPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "AppExamPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppExamAnswers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaperQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnswerJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    SaveVersion = table.Column<int>(type: "integer", nullable: false),
                    LastSaveIdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SavedTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
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
                    table.PrimaryKey("PK_AppExamAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppExamAnswers_AppExamAttempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "AppExamAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AppExamAnswers_AppExamPaperQuestions_PaperQuestionId",
                        column: x => x.PaperQuestionId,
                        principalTable: "AppExamPaperQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppExamScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaperId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TotalScore = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ObtainedScore = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PassingScore = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IsPassed = table.Column<bool>(type: "boolean", nullable: false),
                    ScoredTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ScoringVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
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
                    table.PrimaryKey("PK_AppExamScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppExamScores_AppExamAttempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "AppExamAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AppWrongQuestionRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaperId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaperQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionSnapshot = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    UserAnswerJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CorrectAnswerJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    AnalysisSnapshot = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    MaxScore = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ObtainedScore = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
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
                    table.PrimaryKey("PK_AppWrongQuestionRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppWrongQuestionRecords_AppExamAttempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "AppExamAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppWrongQuestionRecords_AppExamPaperQuestions_PaperQuestion~",
                        column: x => x.PaperQuestionId,
                        principalTable: "AppExamPaperQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppWrongQuestionRecords_AppExamScores_ScoreId",
                        column: x => x.ScoreId,
                        principalTable: "AppExamScores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppExamAnswers_AttemptId_PaperQuestionId",
                table: "AppExamAnswers",
                columns: new[] { "AttemptId", "PaperQuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppExamAnswers_PaperQuestionId",
                table: "AppExamAnswers",
                column: "PaperQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_AppExamAssignments_PlanId_ScopeType_TargetId",
                table: "AppExamAssignments",
                columns: new[] { "PlanId", "ScopeType", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppExamAssignments_TenantId_ScopeType_TargetId_IsActive",
                table: "AppExamAssignments",
                columns: new[] { "TenantId", "ScopeType", "TargetId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_AppExamAttempts_PaperId",
                table: "AppExamAttempts",
                column: "PaperId");

            migrationBuilder.CreateIndex(
                name: "IX_AppExamAttempts_PlanId_UserId_AttemptNumber",
                table: "AppExamAttempts",
                columns: new[] { "PlanId", "UserId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppExamAttempts_UserId_Status_StartedTime",
                table: "AppExamAttempts",
                columns: new[] { "UserId", "Status", "StartedTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AppExamPaperQuestions_OriginalQuestionId",
                table: "AppExamPaperQuestions",
                column: "OriginalQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_AppExamPaperQuestions_PaperId_DisplayOrder",
                table: "AppExamPaperQuestions",
                columns: new[] { "PaperId", "DisplayOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppExamPaperQuestions_Type_Difficulty",
                table: "AppExamPaperQuestions",
                columns: new[] { "Type", "Difficulty" });

            migrationBuilder.CreateIndex(
                name: "IX_AppExamPapers_ApplicablePositionId",
                table: "AppExamPapers",
                column: "ApplicablePositionId");

            migrationBuilder.CreateIndex(
                name: "IX_AppExamPapers_TenantId_BuildMode_CreationTime",
                table: "AppExamPapers",
                columns: new[] { "TenantId", "BuildMode", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AppExamPlans_PaperId",
                table: "AppExamPlans",
                column: "PaperId");

            migrationBuilder.CreateIndex(
                name: "IX_AppExamPlans_TenantId_Status_StartTime",
                table: "AppExamPlans",
                columns: new[] { "TenantId", "Status", "StartTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AppExamScores_AttemptId",
                table: "AppExamScores",
                column: "AttemptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppExamScores_IsPassed_ScoredTime",
                table: "AppExamScores",
                columns: new[] { "IsPassed", "ScoredTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AppExamScores_PlanId_UserId",
                table: "AppExamScores",
                columns: new[] { "PlanId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppWrongQuestionRecords_AttemptId",
                table: "AppWrongQuestionRecords",
                column: "AttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_AppWrongQuestionRecords_PaperQuestionId",
                table: "AppWrongQuestionRecords",
                column: "PaperQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_AppWrongQuestionRecords_ScoreId_PaperQuestionId",
                table: "AppWrongQuestionRecords",
                columns: new[] { "ScoreId", "PaperQuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppWrongQuestionRecords_UserId_OriginalQuestionId",
                table: "AppWrongQuestionRecords",
                columns: new[] { "UserId", "OriginalQuestionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppExamAnswers");

            migrationBuilder.DropTable(
                name: "AppExamAssignments");

            migrationBuilder.DropTable(
                name: "AppWrongQuestionRecords");

            migrationBuilder.DropTable(
                name: "AppExamPaperQuestions");

            migrationBuilder.DropTable(
                name: "AppExamScores");

            migrationBuilder.DropTable(
                name: "AppExamAttempts");

            migrationBuilder.DropTable(
                name: "AppExamPlans");

            migrationBuilder.DropTable(
                name: "AppExamPapers");
        }
    }
}
