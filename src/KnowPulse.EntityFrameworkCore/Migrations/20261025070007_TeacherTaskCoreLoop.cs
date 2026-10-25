using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class TeacherTaskCoreLoop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OwnerTeacherId",
                table: "AppQuestions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerTeacherId",
                table: "AppKnowledgeItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "StartTime",
                table: "AppExamPlans",
                type: "timestamp without time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");

            migrationBuilder.AlterColumn<decimal>(
                name: "PassingScore",
                table: "AppExamPlans",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<DateTime>(
                name: "EndTime",
                table: "AppExamPlans",
                type: "timestamp without time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");

            migrationBuilder.AlterColumn<int>(
                name: "DurationMinutes",
                table: "AppExamPlans",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "AppExamPlans",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "ExpiresTime",
                table: "AppExamAttempts",
                type: "timestamp without time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");

            migrationBuilder.AddColumn<string>(
                name: "KnowledgeItemIdsJson",
                table: "AppAiQuestionGenerationTasks",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerTeacherId",
                table: "AppAiQuestionGenerationTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppTeacherInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TeacherId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvitationCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
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
                    table.PrimaryKey("PK_AppTeacherInvitations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppTeachingRelationships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TeacherId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
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
                    table.PrimaryKey("PK_AppTeachingRelationships", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppQuestions_TenantId_OwnerTeacherId",
                table: "AppQuestions",
                columns: new[] { "TenantId", "OwnerTeacherId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppKnowledgeItems_TenantId_OwnerTeacherId",
                table: "AppKnowledgeItems",
                columns: new[] { "TenantId", "OwnerTeacherId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppExamPlans_TenantId_OwnerId",
                table: "AppExamPlans",
                columns: new[] { "TenantId", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppExamAssignments_PlanId_TargetId",
                table: "AppExamAssignments",
                columns: new[] { "PlanId", "TargetId" },
                unique: true,
                filter: "\"ScopeType\" = 3");

            migrationBuilder.CreateIndex(
                name: "IX_AppAiQuestionGenerationTasks_TenantId_OwnerTeacherId",
                table: "AppAiQuestionGenerationTasks",
                columns: new[] { "TenantId", "OwnerTeacherId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppTeacherInvitations_InvitationCode",
                table: "AppTeacherInvitations",
                column: "InvitationCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppTeacherInvitations_TeacherId",
                table: "AppTeacherInvitations",
                column: "TeacherId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppTeachingRelationships_TeacherId_StudentId",
                table: "AppTeachingRelationships",
                columns: new[] { "TeacherId", "StudentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppTeachingRelationships_TenantId_StudentId_Status",
                table: "AppTeachingRelationships",
                columns: new[] { "TenantId", "StudentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AppTeachingRelationships_TenantId_TeacherId_Status",
                table: "AppTeachingRelationships",
                columns: new[] { "TenantId", "TeacherId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppTeacherInvitations");

            migrationBuilder.DropTable(
                name: "AppTeachingRelationships");

            migrationBuilder.DropIndex(
                name: "IX_AppQuestions_TenantId_OwnerTeacherId",
                table: "AppQuestions");

            migrationBuilder.DropIndex(
                name: "IX_AppKnowledgeItems_TenantId_OwnerTeacherId",
                table: "AppKnowledgeItems");

            migrationBuilder.DropIndex(
                name: "IX_AppExamPlans_TenantId_OwnerId",
                table: "AppExamPlans");

            migrationBuilder.DropIndex(
                name: "IX_AppExamAssignments_PlanId_TargetId",
                table: "AppExamAssignments");

            migrationBuilder.DropIndex(
                name: "IX_AppAiQuestionGenerationTasks_TenantId_OwnerTeacherId",
                table: "AppAiQuestionGenerationTasks");

            migrationBuilder.DropColumn(
                name: "OwnerTeacherId",
                table: "AppQuestions");

            migrationBuilder.DropColumn(
                name: "OwnerTeacherId",
                table: "AppKnowledgeItems");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "AppExamPlans");

            migrationBuilder.DropColumn(
                name: "KnowledgeItemIdsJson",
                table: "AppAiQuestionGenerationTasks");

            migrationBuilder.DropColumn(
                name: "OwnerTeacherId",
                table: "AppAiQuestionGenerationTasks");

            migrationBuilder.AlterColumn<DateTime>(
                name: "StartTime",
                table: "AppExamPlans",
                type: "timestamp without time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "PassingScore",
                table: "AppExamPlans",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "EndTime",
                table: "AppExamPlans",
                type: "timestamp without time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "DurationMinutes",
                table: "AppExamPlans",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "ExpiresTime",
                table: "AppExamAttempts",
                type: "timestamp without time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone",
                oldNullable: true);
        }
    }
}
