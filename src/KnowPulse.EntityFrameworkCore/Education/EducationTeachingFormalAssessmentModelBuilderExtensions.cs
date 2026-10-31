using System;
using KnowPulse.Education;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace KnowPulse.EntityFrameworkCore;

public static class EducationTeachingFormalAssessmentModelBuilderExtensions
{
    public static void ConfigureEducationTeachingFormalAssessments(this ModelBuilder builder)
    {
        builder.Entity<EducationTeachingActivity>().HasAlternateKey(x => new { x.Id, x.PlanId, x.PlanRevisionId, x.StudentUserId, x.OwnerUserId, x.StepKind }).HasName("AK_EduActivities_FormalScope");
        builder.Entity<EducationTeachingPlan>().HasAlternateKey(x => new { x.Id, x.SchoolId, x.EducationSubjectId, x.TeachingAssignmentId }).HasName("AK_EduPlans_FormalScope");
        builder.Entity<EducationAssessmentTaskRevision>().HasAlternateKey(x => new { x.Id, x.TaskId, x.ExamPaperId, x.OwnerUserId, x.TeachingAssignmentId }).HasName("AK_EduAssessmentRevisions_FormalScope");
        builder.Entity<EducationTeachingFormalAssessment>(b =>
        {
            Table(b, "EducationTeachingFormalAssessments"); Utc(b, "RecordedAtUtc");
            b.HasAlternateKey(x => new { x.Id, x.ExamPaperId }).HasName("AK_EduFormalBindings_Paper");
            b.HasAlternateKey(x => new { x.Id, x.OwnerUserId, x.StudentUserId }).HasName("AK_EduFormalBindings_Actors");
            b.HasIndex(x => x.ActivityId).IsUnique().HasDatabaseName("IX_EduFormalBindings_Activity");
            b.HasIndex(x => x.RecipientId).IsUnique().HasDatabaseName("IX_EduFormalBindings_Recipient");
            b.HasIndex(x => new { x.TenantId, x.SchoolId, x.EducationSubjectId, x.StudentUserId }).HasDatabaseName("IX_EduFormalBindings_History");
            b.Property(x => x.TaskTitle).IsRequired().HasMaxLength(256); b.Property(x => x.CurriculumJson).IsRequired().HasColumnType("text");
            b.Property(x => x.RequestFingerprint).IsRequired().HasMaxLength(64); b.Property(x => x.PreviewFingerprint).IsRequired().HasMaxLength(64);
            b.HasOne<EducationTeachingActivity>().WithMany().HasForeignKey(x => new { x.ActivityId, x.PlanId, x.PlanRevisionId, x.StudentUserId, x.OwnerUserId, x.StepKind })
                .HasPrincipalKey(x => new { x.Id, x.PlanId, x.PlanRevisionId, x.StudentUserId, x.OwnerUserId, x.StepKind }).HasConstraintName("FK_EduFormalBindings_Activity").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationTeachingPlan>().WithMany().HasForeignKey(x => new { x.PlanId, x.SchoolId, x.EducationSubjectId, x.TeachingAssignmentId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId, x.EducationSubjectId, x.TeachingAssignmentId }).HasConstraintName("FK_EduFormalBindings_PlanScope").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentTaskRevision>().WithMany().HasForeignKey(x => new { x.TaskRevisionId, x.TaskId, x.ExamPaperId, x.OwnerUserId, x.TeachingAssignmentId })
                .HasPrincipalKey(x => new { x.Id, x.TaskId, x.ExamPaperId, x.OwnerUserId, x.TeachingAssignmentId }).HasConstraintName("FK_EduFormalBindings_TaskScope").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentRecipient>().WithMany().HasForeignKey(x => new { x.RecipientId, x.TaskId, x.TaskRevisionId, x.StudentUserId })
                .HasPrincipalKey(x => new { x.Id, x.TaskId, x.TaskRevisionId, x.StudentUserId }).HasConstraintName("FK_EduFormalBindings_Recipient").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationTeachingFormalReview>().WithMany().HasForeignKey(x => new { x.CurrentReviewId, BindingId = x.Id })
                .HasPrincipalKey(x => new { x.Id, x.BindingId }).HasConstraintName("FK_EduFormalBindings_CurrentReview").OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_EduFormalBindings_State", "\"StepKind\" IN (0,4) AND \"PaperSnapshotVersion\" > 0 AND length(\"CurriculumJson\") BETWEEN 1 AND 8388608 AND " +
                "((\"ReviewRevisionNumber\" = 0 AND \"CurrentReviewId\" IS NULL) OR (\"ReviewRevisionNumber\" > 0 AND \"CurrentReviewId\" IS NOT NULL))"));
        });
        builder.Entity<EducationTeachingFormalQuestion>(b =>
        {
            Table(b, "EducationTeachingFormalQuestions"); b.Ignore(x => x.ExposureId);
            b.HasIndex(x => new { x.BindingId, x.PaperQuestionId }).IsUnique().HasDatabaseName("IX_EduFormalQuestions_Question");
            b.HasIndex(x => new { x.TenantId, x.OriginalQuestionId }).HasDatabaseName("IX_EduFormalQuestions_Original");
            b.HasOne<EducationTeachingFormalAssessment>().WithMany().HasForeignKey(x => new { x.BindingId, x.ExamPaperId })
                .HasPrincipalKey(x => new { x.Id, x.ExamPaperId }).HasConstraintName("FK_EduFormalQuestions_Binding").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ExamPaperQuestion>().WithMany().HasForeignKey(x => new { x.PaperQuestionId, x.ExamPaperId })
                .HasPrincipalKey(x => new { x.Id, x.PaperId }).HasConstraintName("FK_EduFormalQuestions_Question").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<QuestionDefinitionRevision>().WithMany().HasForeignKey(x => new { x.DefinitionRevisionId, x.OriginalQuestionId })
                .HasPrincipalKey(x => new { x.Id, x.QuestionId }).HasConstraintName("FK_EduFormalQuestions_Definition").OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<EducationTeachingFormalReview>(b =>
        {
            Table(b, "EducationTeachingFormalReviews"); Utc(b, "RecordedAtUtc");
            b.HasAlternateKey(x => new { x.Id, x.BindingId }).HasName("AK_EduFormalReviews_Binding");
            b.HasIndex(x => new { x.BindingId, x.RevisionNumber }).IsUnique().HasDatabaseName("IX_EduFormalReviews_Sequence");
            b.Property(x => x.Reason).IsRequired().HasMaxLength(2048); b.Property(x => x.ExpectedConcurrencyStamp).IsRequired().HasMaxLength(40);
            b.Property(x => x.RequestFingerprint).IsRequired().HasMaxLength(64);
            b.HasOne<EducationTeachingFormalAssessment>().WithMany().HasForeignKey(x => new { x.BindingId, x.OwnerUserId, x.StudentUserId })
                .HasPrincipalKey(x => new { x.Id, x.OwnerUserId, x.StudentUserId }).HasConstraintName("FK_EduFormalReviews_Binding").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationTeachingFormalReview>().WithMany().HasForeignKey(x => new { x.BasedOnReviewId, x.BindingId })
                .HasPrincipalKey(x => new { x.Id, x.BindingId }).HasConstraintName("FK_EduFormalReviews_Previous").OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_EduFormalReviews_State", "\"RevisionNumber\" > 0 AND \"PlanContextUse\" IN (0,1,2) AND " +
                "(NOT \"IsEnabled\" OR \"PlanContextUse\" = 2) AND length(trim(\"Reason\")) BETWEEN 1 AND 2048"));
        });
    }
    private static void Table<T>(EntityTypeBuilder<T> b, string name) where T : class { b.ToTable(KnowPulseConsts.DbTablePrefix + name, KnowPulseConsts.DbSchema); b.ConfigureByConvention(); }
    private static void Utc<T>(EntityTypeBuilder<T> b, string name) where T : class => b.Property<DateTime>(name).HasConversion(new ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc)));
}
