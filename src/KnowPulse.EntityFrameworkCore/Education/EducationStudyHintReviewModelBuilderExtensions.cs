using System;
using KnowPulse.Education;
using KnowPulse.Exam;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace KnowPulse.EntityFrameworkCore;

public static class EducationStudyHintReviewModelBuilderExtensions
{
    public static void ConfigureEducationStudyHintReviews(this ModelBuilder builder)
    {
        builder.Entity<EducationStudyHintReview>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "EducationStudyHintReviews", KnowPulseConsts.DbSchema); b.ConfigureByConvention();
            b.HasAlternateKey(x => new { x.Id, x.RecipientId, x.PaperQuestionId, x.StudentUserId, x.OwnerUserId }).HasName("AK_EduStudyHints_Context");
            b.HasIndex(x => new { x.RecipientId, x.PaperQuestionId }).IsUnique().HasDatabaseName("IX_EduStudyHints_Question");
            b.Property(x => x.ExposedAtUtc).HasConversion(new ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc)));
            b.HasOne<EducationStudyRecipient>().WithMany().HasForeignKey(x => new { x.RecipientId, x.StudentUserId, x.OwnerUserId }).HasPrincipalKey(x => new { x.Id, x.StudentUserId, x.OwnerUserId })
                .HasConstraintName("FK_EduStudyHints_Recipient").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ExamPaperQuestion>().WithMany().HasForeignKey(x => new { x.PaperQuestionId, PaperId = x.ExamPaperId }).HasPrincipalKey(x => new { x.Id, x.PaperId })
                .HasConstraintName("FK_EduStudyHints_Question").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationStudyHintReviewRevision>().WithMany().HasForeignKey(x => new { x.CurrentRevisionId, ReviewId = x.Id, x.RevisionNumber }).HasPrincipalKey(x => new { x.Id, x.ReviewId, x.RevisionNumber })
                .HasConstraintName("FK_EduStudyHints_Current").OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_EduStudyHints_State", "(\"RevisionNumber\" = 0 AND \"CurrentRevisionId\" IS NULL) OR (\"RevisionNumber\" > 0 AND \"CurrentRevisionId\" IS NOT NULL)"));
        });
        builder.Entity<EducationStudyHintReviewRevision>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "EducationStudyHintReviewRevisions", KnowPulseConsts.DbSchema); b.ConfigureByConvention();
            b.HasAlternateKey(x => new { x.Id, x.ReviewId }).HasName("AK_EduStudyHintVersions_Root");
            b.HasAlternateKey(x => new { x.Id, x.ReviewId, x.RevisionNumber }).HasName("AK_EduStudyHintVersions_Head");
            b.HasIndex(x => new { x.ReviewId, x.RevisionNumber }).IsUnique().HasDatabaseName("IX_EduStudyHintVersions_Sequence");
            b.Property(x => x.Reason).IsRequired().HasMaxLength(2048); b.Property(x => x.RequestFingerprint).IsRequired().HasMaxLength(64); b.Property(x => x.ExpectedConcurrencyStamp).IsRequired().HasMaxLength(40);
            b.Property(x => x.RecordedAtUtc).HasConversion(new ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc)));
            b.HasOne<EducationStudyHintReview>().WithMany().HasForeignKey(x => new { x.ReviewId, x.RecipientId, x.PaperQuestionId, x.StudentUserId, x.OwnerUserId })
                .HasPrincipalKey(x => new { x.Id, x.RecipientId, x.PaperQuestionId, x.StudentUserId, x.OwnerUserId }).HasConstraintName("FK_EduStudyHintVersions_Root").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationStudyHintReviewRevision>().WithMany().HasForeignKey(x => new { x.BasedOnRevisionId, x.ReviewId }).HasPrincipalKey(x => new { x.Id, x.ReviewId })
                .HasConstraintName("FK_EduStudyHintVersions_Previous").OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_EduStudyHintVersions_State", "\"RevisionNumber\" > 0 AND \"Use\" IN (0,1,2) AND length(trim(\"Reason\")) BETWEEN 1 AND 2048 AND length(\"RequestFingerprint\") = 64"));
        });
    }
}
