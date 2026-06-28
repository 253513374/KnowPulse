using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class Mvp1RagEvaluation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppRagEvaluationCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Question = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ExpectedRefusal = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    PermissionContextJson = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    ExpectedCitationsJson = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    ForbiddenCitationsJson = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
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
                    table.PrimaryKey("PK_AppRagEvaluationCases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppRagEvaluationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OperatorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CompletedTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    TotalCaseCount = table.Column<int>(type: "integer", nullable: false),
                    PassedCaseCount = table.Column<int>(type: "integer", nullable: false),
                    FailedCaseCount = table.Column<int>(type: "integer", nullable: false),
                    HitRate = table.Column<double>(type: "double precision", nullable: false),
                    CitationAccuracyRate = table.Column<double>(type: "double precision", nullable: false),
                    NoAnswerRejectionRate = table.Column<double>(type: "double precision", nullable: false),
                    UnauthorizedRecallRate = table.Column<double>(type: "double precision", nullable: false),
                    UnauthorizedRecallCount = table.Column<int>(type: "integer", nullable: false),
                    HardFailed = table.Column<bool>(type: "boolean", nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppRagEvaluationRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppRagEvaluationRunCaseResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    EvaluationRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluationCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionSummary = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ExpectedRefusal = table.Column<bool>(type: "boolean", nullable: false),
                    ActualRefusal = table.Column<bool>(type: "boolean", nullable: false),
                    ExpectedCitationCount = table.Column<int>(type: "integer", nullable: false),
                    ActualCitationCount = table.Column<int>(type: "integer", nullable: false),
                    MatchedExpectedCitationCount = table.Column<int>(type: "integer", nullable: false),
                    UnauthorizedCitationCount = table.Column<int>(type: "integer", nullable: false),
                    Hit = table.Column<bool>(type: "boolean", nullable: false),
                    CitationAccuracyRate = table.Column<double>(type: "double precision", nullable: false),
                    Passed = table.Column<bool>(type: "boolean", nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    AnswerSummary = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ActualCitationsJson = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppRagEvaluationRunCaseResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppRagEvaluationRunCaseResults_AppRagEvaluationCases_Evalua~",
                        column: x => x.EvaluationCaseId,
                        principalTable: "AppRagEvaluationCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppRagEvaluationRunCaseResults_AppRagEvaluationRuns_Evaluat~",
                        column: x => x.EvaluationRunId,
                        principalTable: "AppRagEvaluationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppRagEvaluationCases_Name",
                table: "AppRagEvaluationCases",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_AppRagEvaluationCases_TenantId_IsActive_CreationTime",
                table: "AppRagEvaluationCases",
                columns: new[] { "TenantId", "IsActive", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AppRagEvaluationRunCaseResults_EvaluationCaseId",
                table: "AppRagEvaluationRunCaseResults",
                column: "EvaluationCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_AppRagEvaluationRunCaseResults_EvaluationRunId_EvaluationCa~",
                table: "AppRagEvaluationRunCaseResults",
                columns: new[] { "EvaluationRunId", "EvaluationCaseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppRagEvaluationRunCaseResults_TenantId_Passed_CreationTime",
                table: "AppRagEvaluationRunCaseResults",
                columns: new[] { "TenantId", "Passed", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AppRagEvaluationRunCaseResults_UnauthorizedCitationCount",
                table: "AppRagEvaluationRunCaseResults",
                column: "UnauthorizedCitationCount");

            migrationBuilder.CreateIndex(
                name: "IX_AppRagEvaluationRuns_TenantId_HardFailed_CreationTime",
                table: "AppRagEvaluationRuns",
                columns: new[] { "TenantId", "HardFailed", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AppRagEvaluationRuns_TenantId_Status_CreationTime",
                table: "AppRagEvaluationRuns",
                columns: new[] { "TenantId", "Status", "CreationTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppRagEvaluationRunCaseResults");

            migrationBuilder.DropTable(
                name: "AppRagEvaluationCases");

            migrationBuilder.DropTable(
                name: "AppRagEvaluationRuns");
        }
    }
}
