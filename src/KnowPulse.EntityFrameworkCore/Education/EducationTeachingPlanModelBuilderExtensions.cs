using System;
using KnowPulse.Education;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace KnowPulse.EntityFrameworkCore;

public static class EducationTeachingPlanModelBuilderExtensions
{
    public static void ConfigureEducationTeachingPlans(this ModelBuilder builder)
    {
        builder.Entity<EducationTeachingPlan>(b =>
        {
            Table(b, "EducationTeachingPlans");
            b.HasAlternateKey(x => new { x.Id, x.StudentUserId, x.OwnerUserId }).HasName("AK_EduPlans_Recipient");
            foreach (var name in new[] { "TeacherName", "StudentName" }) b.Property<string>(name).IsRequired().HasMaxLength(256);
            foreach (var name in new[] { "ClassName", "SubjectName" }) b.Property<string>(name).IsRequired().HasMaxLength(128);
            b.HasIndex(x => new { x.TenantId, x.OwnerUserId, x.TeachingAssignmentId }).HasDatabaseName("IX_EduPlans_Teacher");
            b.HasIndex(x => new { x.TenantId, x.StudentUserId, x.PublicRevisionId }).HasDatabaseName("IX_EduPlans_Student");
            b.HasOne<TeachingAssignment>().WithMany().HasForeignKey(x => new { x.TeachingAssignmentId, x.SchoolId, x.AcademicTermId, x.SchoolClassId, TeacherUserId = x.OwnerUserId, x.EducationSubjectId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId, x.AcademicTermId, x.SchoolClassId, x.TeacherUserId, x.EducationSubjectId }).HasConstraintName("FK_EduPlans_Teaching").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<StudentEnrollment>().WithMany().HasForeignKey(x => new { x.StudentEnrollmentId, x.SchoolId, x.SchoolClassId, x.StudentUserId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId, x.SchoolClassId, x.StudentUserId }).HasConstraintName("FK_EduPlans_Enrollment").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<SchoolMembership>().WithMany().HasForeignKey(x => new { x.SchoolMembershipId, x.SchoolId, x.StudentUserId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId, x.UserId }).HasConstraintName("FK_EduPlans_Member").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<TextbookVersion>().WithMany().HasForeignKey(x => new { x.TextbookVersionId, x.SchoolId, x.EducationSubjectId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId, x.EducationSubjectId }).HasConstraintName("FK_EduPlans_Book").OnDelete(DeleteBehavior.Restrict);
            foreach (var pointer in new[] { "CurrentRevisionId", "PublishedRevisionId", "LastPublishedRevisionId", "PublicRevisionId" })
                b.HasOne<EducationTeachingPlanRevision>().WithMany().HasForeignKey(pointer, "Id", "StudentUserId", "OwnerUserId")
                    .HasPrincipalKey(x => new { x.Id, x.PlanId, x.StudentUserId, x.OwnerUserId }).HasConstraintName("FK_EduPlans_" + pointer).OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_EduPlans_State", "((\"RevisionNumber\" = 0 AND \"CurrentRevisionId\" IS NULL) OR (\"RevisionNumber\" > 0 AND \"CurrentRevisionId\" IS NOT NULL)) AND " +
                "((\"LastPublishedRevisionId\" IS NULL AND \"PublicRevisionId\" IS NULL AND \"PublishedRevisionId\" IS NULL) OR (\"LastPublishedRevisionId\" IS NOT NULL AND \"PublicRevisionId\" IS NOT NULL)) AND " +
                "(\"PublishedRevisionId\" IS NULL OR (\"PublishedRevisionId\" = \"LastPublishedRevisionId\" AND \"PublicRevisionId\" = \"PublishedRevisionId\"))"));
        });
        builder.Entity<EducationTeachingPlanRevision>(b =>
        {
            Table(b, "EducationTeachingPlanRevisions");
            b.HasAlternateKey(x => new { x.Id, x.PlanId, x.StudentUserId, x.OwnerUserId }).HasName("AK_EduPlanVersions_Recipient");
            b.HasAlternateKey(x => new { x.Id, x.PlanId, x.StudentUserId }).HasName("AK_EduPlanVersions_Student");
            b.HasIndex(x => new { x.PlanId, x.RevisionNumber }).IsUnique().HasDatabaseName("IX_EduPlanVersions_Sequence");
            b.Property(x => x.BasisJson).HasColumnType("text"); b.Property(x => x.ContentJson).HasColumnType("text");
            b.Property(x => x.InternalNotes).HasMaxLength(4000); b.Property(x => x.Reason).HasMaxLength(2048);
            b.Property(x => x.RequestFingerprint).IsRequired().HasMaxLength(64); b.Property(x => x.ExpectedConcurrencyStamp).IsRequired().HasMaxLength(40);
            Utc(b, "RecordedAtUtc");
            b.HasOne<EducationTeachingPlan>().WithMany().HasForeignKey(x => new { x.PlanId, x.StudentUserId, x.OwnerUserId }).HasPrincipalKey(x => new { x.Id, x.StudentUserId, x.OwnerUserId })
                .HasConstraintName("FK_EduPlanVersions_Plan").OnDelete(DeleteBehavior.Restrict);
            foreach (var pointer in new[] { "BasedOnRevisionId", "SourceRevisionId" })
                b.HasOne<EducationTeachingPlanRevision>().WithMany().HasForeignKey(pointer, "PlanId", "StudentUserId", "OwnerUserId")
                    .HasPrincipalKey(x => new { x.Id, x.PlanId, x.StudentUserId, x.OwnerUserId }).HasConstraintName("FK_EduPlanVersions_" + pointer).OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_EduPlanVersions_Content", "\"RevisionNumber\" > 0 AND ((\"Kind\" IN (0,1,2) AND \"BasisJson\" IS NOT NULL AND length(\"BasisJson\") BETWEEN 1 AND 8388608 AND \"ContentJson\" IS NOT NULL AND length(\"ContentJson\") BETWEEN 1 AND 262144) OR " +
                "(\"Kind\" = 3 AND \"BasisJson\" IS NULL AND \"ContentJson\" IS NULL AND \"InternalNotes\" IS NULL AND \"Reason\" IS NOT NULL AND length(trim(\"Reason\")) > 0)) AND " +
                "((\"Kind\" = 0 AND \"SourceRevisionId\" IS NULL) OR (\"Kind\" IN (1,2,3) AND \"SourceRevisionId\" IS NOT NULL AND \"BasedOnRevisionId\" IS NOT NULL AND \"SourceRevisionId\" = \"BasedOnRevisionId\"))"));
        });
        builder.Entity<EducationTeachingPlanQuestionExposure>(b =>
        {
            Table(b, "EducationTeachingPlanQuestionExposures"); Utc(b, "ExposedAtUtc");
            b.HasAlternateKey(x => new { x.Id, x.PlanId, x.PlanRevisionId, x.StudentUserId }).HasName("AK_EduPlanContexts_Recipient");
            b.HasIndex(x => new { x.PlanRevisionId, x.RecipientId, x.PaperQuestionId, x.StudentUserId }).IsUnique().HasDatabaseName("IX_EduPlanContexts_Once");
            b.HasIndex(x => new { x.TenantId, x.StudentUserId, x.EducationSubjectId, x.OriginalQuestionId, x.ExposedAtUtc }).HasDatabaseName("IX_EduPlanContexts_Original");
            b.HasOne<EducationTeachingPlanRevision>().WithMany().HasForeignKey(x => new { x.PlanRevisionId, x.PlanId, x.StudentUserId })
                .HasPrincipalKey(x => new { x.Id, x.PlanId, x.StudentUserId }).HasConstraintName("FK_EduPlanContexts_Version").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentRecipient>().WithMany().HasForeignKey(x => new { x.RecipientId, x.TaskId, x.TaskRevisionId, x.StudentUserId })
                .HasPrincipalKey(x => new { x.Id, x.TaskId, x.TaskRevisionId, x.StudentUserId }).HasConstraintName("FK_EduPlanContexts_Recipient").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentTaskRevision>().WithMany().HasForeignKey(x => new { x.TaskRevisionId, x.TaskId, x.ExamPaperId })
                .HasPrincipalKey(x => new { x.Id, x.TaskId, x.ExamPaperId }).HasConstraintName("FK_EduPlanContexts_PaperRevision").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ExamPaperQuestion>().WithMany().HasForeignKey(x => new { x.PaperQuestionId, PaperId = x.ExamPaperId })
                .HasPrincipalKey(x => new { x.Id, x.PaperId }).HasConstraintName("FK_EduPlanContexts_Question").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<QuestionDefinitionRevision>().WithMany().HasForeignKey(x => new { x.DefinitionRevisionId, QuestionId = x.OriginalQuestionId })
                .HasPrincipalKey(x => new { x.Id, x.QuestionId }).HasConstraintName("FK_EduPlanContexts_Definition").OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<EducationTeachingPlanHintReview>(b =>
        {
            Table(b, "EducationTeachingPlanHintReviews");
            b.HasAlternateKey(x => new { x.Id, x.StudentUserId, x.OwnerUserId, x.PlanId, x.PlanRevisionId }).HasName("AK_EduPlanHintReviews_Scope");
            b.HasOne<EducationTeachingPlanQuestionExposure>().WithMany().HasForeignKey(x => new { x.Id, x.PlanId, x.PlanRevisionId, x.StudentUserId })
                .HasPrincipalKey(x => new { x.Id, x.PlanId, x.PlanRevisionId, x.StudentUserId }).HasConstraintName("FK_EduPlanHintReviews_Context").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationTeachingPlanRevision>().WithMany().HasForeignKey(x => new { x.PlanRevisionId, x.PlanId, x.StudentUserId, x.OwnerUserId })
                .HasPrincipalKey(x => new { x.Id, x.PlanId, x.StudentUserId, x.OwnerUserId }).HasConstraintName("FK_EduPlanHintReviews_PublicVersion").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationTeachingPlanHintReviewRevision>().WithMany().HasForeignKey(x => new { x.CurrentRevisionId, ExposureId = x.Id })
                .HasPrincipalKey(x => new { x.Id, x.ExposureId }).HasConstraintName("FK_EduPlanHintReviews_Current").OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_EduPlanHintReviews_State", "(\"RevisionNumber\" = 0 AND \"CurrentRevisionId\" IS NULL) OR (\"RevisionNumber\" > 0 AND \"CurrentRevisionId\" IS NOT NULL)"));
        });
        builder.Entity<EducationTeachingPlanHintReviewRevision>(b =>
        {
            Table(b, "EducationTeachingPlanHintReviewRevisions"); Utc(b, "RecordedAtUtc");
            b.HasAlternateKey(x => new { x.Id, x.ExposureId }).HasName("AK_EduPlanHintVersions_Root");
            b.HasIndex(x => new { x.ExposureId, x.RevisionNumber }).IsUnique().HasDatabaseName("IX_EduPlanHintVersions_Sequence");
            b.Property(x => x.Reason).IsRequired().HasMaxLength(2048); b.Property(x => x.ExpectedConcurrencyStamp).IsRequired().HasMaxLength(40);
            b.Property(x => x.RequestFingerprint).IsRequired().HasMaxLength(64);
            b.HasOne<EducationTeachingPlanHintReview>().WithMany().HasForeignKey(x => new { x.ExposureId, x.StudentUserId, x.OwnerUserId, x.PlanId, x.PlanRevisionId })
                .HasPrincipalKey(x => new { x.Id, x.StudentUserId, x.OwnerUserId, x.PlanId, x.PlanRevisionId }).HasConstraintName("FK_EduPlanHintVersions_Root").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationTeachingPlanHintReviewRevision>().WithMany().HasForeignKey(x => new { x.BasedOnRevisionId, x.ExposureId })
                .HasPrincipalKey(x => new { x.Id, x.ExposureId }).HasConstraintName("FK_EduPlanHintVersions_Previous").OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_EduPlanHintVersions_State", "\"RevisionNumber\" > 0 AND \"Use\" IN (0,1,2) AND length(trim(\"Reason\")) BETWEEN 1 AND 2048"));
        });
    }
    private static void Table<T>(EntityTypeBuilder<T> b, string table) where T : class { b.ToTable(KnowPulseConsts.DbTablePrefix + table, KnowPulseConsts.DbSchema); b.ConfigureByConvention(); }
    private static void Utc<T>(EntityTypeBuilder<T> b, string name) where T : class => b.Property<DateTime>(name).HasConversion(new ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc)));
}
