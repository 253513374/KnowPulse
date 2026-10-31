using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddEducationTeachingFormalAssessments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_EduPlans_FormalScope",
                table: "AppEducationTeachingPlans",
                columns: new[] { "Id", "SchoolId", "EducationSubjectId", "TeachingAssignmentId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_EduActivities_FormalScope",
                table: "AppEducationTeachingActivities",
                columns: new[] { "Id", "PlanId", "PlanRevisionId", "StudentUserId", "OwnerUserId", "StepKind" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_EduAssessmentRevisions_FormalScope",
                table: "AppEducationAssessmentTaskRevisions",
                columns: new[] { "Id", "TaskId", "ExamPaperId", "OwnerUserId", "TeachingAssignmentId" });

            migrationBuilder.CreateTable(
                name: "AppEducationTeachingFormalAssessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActivityId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    EducationSubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeachingAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepKind = table.Column<int>(type: "integer", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamPaperId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaperSnapshotVersion = table.Column<int>(type: "integer", nullable: false),
                    TaskTitle = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CurriculumJson = table.Column<string>(type: "text", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PreviewFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ReviewRevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    CurrentReviewId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_AppEducationTeachingFormalAssessments", x => x.Id);
                    table.UniqueConstraint("AK_EduFormalBindings_Actors", x => new { x.Id, x.OwnerUserId, x.StudentUserId });
                    table.UniqueConstraint("AK_EduFormalBindings_Paper", x => new { x.Id, x.ExamPaperId });
                    table.CheckConstraint("CK_EduFormalBindings_State", "\"StepKind\" IN (0,4) AND \"PaperSnapshotVersion\" > 0 AND length(\"CurriculumJson\") BETWEEN 1 AND 8388608 AND ((\"ReviewRevisionNumber\" = 0 AND \"CurrentReviewId\" IS NULL) OR (\"ReviewRevisionNumber\" > 0 AND \"CurrentReviewId\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_EduFormalBindings_Activity",
                        columns: x => new { x.ActivityId, x.PlanId, x.PlanRevisionId, x.StudentUserId, x.OwnerUserId, x.StepKind },
                        principalTable: "AppEducationTeachingActivities",
                        principalColumns: new[] { "Id", "PlanId", "PlanRevisionId", "StudentUserId", "OwnerUserId", "StepKind" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduFormalBindings_PlanScope",
                        columns: x => new { x.PlanId, x.SchoolId, x.EducationSubjectId, x.TeachingAssignmentId },
                        principalTable: "AppEducationTeachingPlans",
                        principalColumns: new[] { "Id", "SchoolId", "EducationSubjectId", "TeachingAssignmentId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduFormalBindings_Recipient",
                        columns: x => new { x.RecipientId, x.TaskId, x.TaskRevisionId, x.StudentUserId },
                        principalTable: "AppEducationAssessmentRecipients",
                        principalColumns: new[] { "Id", "TaskId", "TaskRevisionId", "StudentUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduFormalBindings_TaskScope",
                        columns: x => new { x.TaskRevisionId, x.TaskId, x.ExamPaperId, x.OwnerUserId, x.TeachingAssignmentId },
                        principalTable: "AppEducationAssessmentTaskRevisions",
                        principalColumns: new[] { "Id", "TaskId", "ExamPaperId", "OwnerUserId", "TeachingAssignmentId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationTeachingFormalQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    BindingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamPaperId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaperQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationTeachingFormalQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EduFormalQuestions_Binding",
                        columns: x => new { x.BindingId, x.ExamPaperId },
                        principalTable: "AppEducationTeachingFormalAssessments",
                        principalColumns: new[] { "Id", "ExamPaperId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduFormalQuestions_Definition",
                        columns: x => new { x.DefinitionRevisionId, x.OriginalQuestionId },
                        principalTable: "AppQuestionDefinitionRevisions",
                        principalColumns: new[] { "Id", "QuestionId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduFormalQuestions_Question",
                        columns: x => new { x.PaperQuestionId, x.ExamPaperId },
                        principalTable: "AppExamPaperQuestions",
                        principalColumns: new[] { "Id", "PaperId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEducationTeachingFormalReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    BindingId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    BasedOnReviewId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    PlanContextUse = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ExpectedConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEducationTeachingFormalReviews", x => x.Id);
                    table.UniqueConstraint("AK_EduFormalReviews_Binding", x => new { x.Id, x.BindingId });
                    table.CheckConstraint("CK_EduFormalReviews_State", "\"RevisionNumber\" > 0 AND \"PlanContextUse\" IN (0,1,2) AND (NOT \"IsEnabled\" OR \"PlanContextUse\" = 2) AND length(trim(\"Reason\")) BETWEEN 1 AND 2048");
                    table.ForeignKey(
                        name: "FK_EduFormalReviews_Binding",
                        columns: x => new { x.BindingId, x.OwnerUserId, x.StudentUserId },
                        principalTable: "AppEducationTeachingFormalAssessments",
                        principalColumns: new[] { "Id", "OwnerUserId", "StudentUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EduFormalReviews_Previous",
                        columns: x => new { x.BasedOnReviewId, x.BindingId },
                        principalTable: "AppEducationTeachingFormalReviews",
                        principalColumns: new[] { "Id", "BindingId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingFormalAssessments_ActivityId_PlanId_Pla~",
                table: "AppEducationTeachingFormalAssessments",
                columns: new[] { "ActivityId", "PlanId", "PlanRevisionId", "StudentUserId", "OwnerUserId", "StepKind" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingFormalAssessments_CurrentReviewId_Id",
                table: "AppEducationTeachingFormalAssessments",
                columns: new[] { "CurrentReviewId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingFormalAssessments_PlanId_SchoolId_Educa~",
                table: "AppEducationTeachingFormalAssessments",
                columns: new[] { "PlanId", "SchoolId", "EducationSubjectId", "TeachingAssignmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingFormalAssessments_RecipientId_TaskId_Ta~",
                table: "AppEducationTeachingFormalAssessments",
                columns: new[] { "RecipientId", "TaskId", "TaskRevisionId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingFormalAssessments_TaskRevisionId_TaskId~",
                table: "AppEducationTeachingFormalAssessments",
                columns: new[] { "TaskRevisionId", "TaskId", "ExamPaperId", "OwnerUserId", "TeachingAssignmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduFormalBindings_Activity",
                table: "AppEducationTeachingFormalAssessments",
                column: "ActivityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EduFormalBindings_History",
                table: "AppEducationTeachingFormalAssessments",
                columns: new[] { "TenantId", "SchoolId", "EducationSubjectId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduFormalBindings_Recipient",
                table: "AppEducationTeachingFormalAssessments",
                column: "RecipientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingFormalQuestions_BindingId_ExamPaperId",
                table: "AppEducationTeachingFormalQuestions",
                columns: new[] { "BindingId", "ExamPaperId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingFormalQuestions_DefinitionRevisionId_Or~",
                table: "AppEducationTeachingFormalQuestions",
                columns: new[] { "DefinitionRevisionId", "OriginalQuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingFormalQuestions_PaperQuestionId_ExamPap~",
                table: "AppEducationTeachingFormalQuestions",
                columns: new[] { "PaperQuestionId", "ExamPaperId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduFormalQuestions_Original",
                table: "AppEducationTeachingFormalQuestions",
                columns: new[] { "TenantId", "OriginalQuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduFormalQuestions_Question",
                table: "AppEducationTeachingFormalQuestions",
                columns: new[] { "BindingId", "PaperQuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingFormalReviews_BasedOnReviewId_BindingId",
                table: "AppEducationTeachingFormalReviews",
                columns: new[] { "BasedOnReviewId", "BindingId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppEducationTeachingFormalReviews_BindingId_OwnerUserId_Stu~",
                table: "AppEducationTeachingFormalReviews",
                columns: new[] { "BindingId", "OwnerUserId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_EduFormalReviews_Sequence",
                table: "AppEducationTeachingFormalReviews",
                columns: new[] { "BindingId", "RevisionNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_EduFormalBindings_CurrentReview",
                table: "AppEducationTeachingFormalAssessments",
                columns: new[] { "CurrentReviewId", "Id" },
                principalTable: "AppEducationTeachingFormalReviews",
                principalColumns: new[] { "Id", "BindingId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EduFormalBindings_CurrentReview",
                table: "AppEducationTeachingFormalAssessments");

            migrationBuilder.DropTable(
                name: "AppEducationTeachingFormalQuestions");

            migrationBuilder.DropTable(
                name: "AppEducationTeachingFormalReviews");

            migrationBuilder.DropTable(
                name: "AppEducationTeachingFormalAssessments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_EduPlans_FormalScope",
                table: "AppEducationTeachingPlans");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_EduActivities_FormalScope",
                table: "AppEducationTeachingActivities");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_EduAssessmentRevisions_FormalScope",
                table: "AppEducationAssessmentTaskRevisions");
        }
    }
}
