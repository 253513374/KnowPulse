using System;
using KnowPulse.Education;
using KnowPulse.QuestionBank;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace KnowPulse.EntityFrameworkCore;

public static class EducationEvidenceDecisionModelBuilderExtensions
{
    public static void ConfigureEducationEvidenceDecisions(this ModelBuilder builder)
    {
        builder.Entity<EducationAssessmentQuestionGrade>().HasAlternateKey(x => new { x.Id, x.TaskId, x.SubmissionId, x.AttemptId, x.StudentUserId, x.PaperQuestionId, x.OwnerUserId }).HasName("AK_EduGrades_EvidenceScope");
        builder.Entity<EducationAssessmentQuestionGrade>().HasAlternateKey(x => new { x.Id, x.OwnerUserId }).HasName("AK_EduGrades_EvidenceOwner");
        builder.Entity<EducationQuestionExposure>().HasAlternateKey(x => new { x.Id, x.StudentUserId }).HasName("AK_EduExposures_Student");
        builder.Entity<EducationStudentFeedbackRevision>().HasAlternateKey(x => new { x.Id, x.StudentUserId, x.OwnerUserId }).HasName("AK_EduFeedbackVersions_StudentOwner");
        builder.Entity<EducationEvidenceQualification>(b =>
        {
            Root(b, "EducationEvidenceQualifications", "EduQualifications");
            b.HasAlternateKey(x => new { x.Id, x.StudentUserId, x.OwnerUserId, x.QuestionDefinitionRevisionId }).HasName("AK_EduQualifications_Scope");
            b.HasOne<EducationAssessmentQuestionGrade>().WithMany().HasForeignKey(x => new { x.Id, x.TaskId, x.SubmissionId, x.AttemptId, x.StudentUserId, x.PaperQuestionId, x.OwnerUserId })
                .HasPrincipalKey(x => new { x.Id, x.TaskId, x.SubmissionId, x.AttemptId, x.StudentUserId, x.PaperQuestionId, x.OwnerUserId }).HasConstraintName("FK_EduQualifications_Grade").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<QuestionDefinitionRevision>().WithMany().HasForeignKey(x => new { x.QuestionDefinitionRevisionId, QuestionId = x.OriginalQuestionId })
                .HasPrincipalKey(x => new { x.Id, x.QuestionId }).HasConstraintName("FK_EduQualifications_Definition").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationEvidenceQualificationRevision>().WithMany().HasForeignKey(x => new { x.CurrentRevisionId, QuestionGradeId = x.Id })
                .HasPrincipalKey(x => new { x.Id, x.QuestionGradeId }).HasConstraintName("FK_EduQualifications_Current").OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<EducationEvidenceQualificationRevision>(b =>
        {
            Revision(b, "EducationEvidenceQualificationRevisions", "EduQualificationVersions", "QuestionGradeId");
            b.HasAlternateKey(x => new { x.Id, x.QuestionGradeId }).HasName("AK_EduQualificationVersions_Root");
            b.HasOne<EducationEvidenceQualification>().WithMany().HasForeignKey(x => new { x.QuestionGradeId, x.StudentUserId, x.OwnerUserId, x.QuestionDefinitionRevisionId })
                .HasPrincipalKey(x => new { x.Id, x.StudentUserId, x.OwnerUserId, x.QuestionDefinitionRevisionId }).HasConstraintName("FK_EduQualificationVersions_Root").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationEvidenceQualificationRevision>().WithMany().HasForeignKey(x => new { x.BasedOnRevisionId, x.QuestionGradeId })
                .HasPrincipalKey(x => new { x.Id, x.QuestionGradeId }).HasConstraintName("FK_EduQualificationVersions_Previous").OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_EduQualificationVersions_Use", "\"Use\" IN (0, 1, 2, 3) AND (\"Use\" <> 1 OR (\"NoHintsOrAnswersConfirmed\" AND \"NewRequirementsConfirmed\" AND \"OriginalRelationshipsChecked\"))"));
        });
        builder.Entity<EducationExposureHintReview>(b =>
        {
            Root(b, "EducationExposureHintReviews", "EduHintReviews");
            b.HasAlternateKey(x => new { x.Id, x.StudentUserId, x.OwnerUserId, x.FeedbackRevisionId }).HasName("AK_EduHintReviews_Scope");
            b.HasOne<EducationQuestionExposure>().WithMany().HasForeignKey(x => new { x.Id, x.StudentUserId })
                .HasPrincipalKey(x => new { x.Id, x.StudentUserId }).HasConstraintName("FK_EduHintReviews_Exposure").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationStudentFeedbackRevision>().WithMany().HasForeignKey(x => new { x.FeedbackRevisionId, x.StudentUserId, x.OwnerUserId })
                .HasPrincipalKey(x => new { x.Id, x.StudentUserId, x.OwnerUserId }).HasConstraintName("FK_EduHintReviews_Feedback").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationExposureHintReviewRevision>().WithMany().HasForeignKey(x => new { x.CurrentRevisionId, ExposureId = x.Id })
                .HasPrincipalKey(x => new { x.Id, x.ExposureId }).HasConstraintName("FK_EduHintReviews_Current").OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<EducationExposureHintReviewRevision>(b =>
        {
            Revision(b, "EducationExposureHintReviewRevisions", "EduHintReviewVersions", "ExposureId");
            b.HasAlternateKey(x => new { x.Id, x.ExposureId }).HasName("AK_EduHintReviewVersions_Root");
            b.HasOne<EducationExposureHintReview>().WithMany().HasForeignKey(x => new { x.ExposureId, x.StudentUserId, x.OwnerUserId, x.FeedbackRevisionId })
                .HasPrincipalKey(x => new { x.Id, x.StudentUserId, x.OwnerUserId, x.FeedbackRevisionId }).HasConstraintName("FK_EduHintReviewVersions_Root").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationExposureHintReviewRevision>().WithMany().HasForeignKey(x => new { x.BasedOnRevisionId, x.ExposureId })
                .HasPrincipalKey(x => new { x.Id, x.ExposureId }).HasConstraintName("FK_EduHintReviewVersions_Previous").OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_EduHintReviewVersions_Use", "\"Use\" IN (0, 1, 2)"));
        });
        builder.Entity<EducationQuestionOriginRelation>(b =>
        {
            Root(b, "EducationQuestionOriginRelations", "EduOriginRelations");
            b.HasAlternateKey(x => new { x.Id, x.FirstOriginalQuestionId, x.SecondOriginalQuestionId, x.OwnerUserId }).HasName("AK_EduOriginRelations_Pair");
            b.HasIndex(x => new { x.OwnerUserId, x.SchoolId, x.EducationSubjectId, x.FirstOriginalQuestionId, x.SecondOriginalQuestionId }).IsUnique().HasDatabaseName("IX_EduOriginRelations_OnePerPair");
            b.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).HasConstraintName("FK_EduOriginRelations_School").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationSubject>().WithMany().HasForeignKey(x => x.EducationSubjectId).HasConstraintName("FK_EduOriginRelations_Subject").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationQuestionOriginRelationRevision>().WithMany().HasForeignKey(x => new { x.CurrentRevisionId, RelationId = x.Id })
                .HasPrincipalKey(x => new { x.Id, x.RelationId }).HasConstraintName("FK_EduOriginRelations_Current").OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_EduOriginRelations_Pair", "\"FirstOriginalQuestionId\" < \"SecondOriginalQuestionId\""));
        });
        builder.Entity<EducationQuestionOriginRelationRevision>(b =>
        {
            Revision(b, "EducationQuestionOriginRelationRevisions", "EduOriginVersions", "RelationId");
            b.HasAlternateKey(x => new { x.Id, x.RelationId }).HasName("AK_EduOriginVersions_Root");
            b.HasOne<EducationQuestionOriginRelation>().WithMany().HasForeignKey(x => new { x.RelationId, x.FirstOriginalQuestionId, x.SecondOriginalQuestionId, x.OwnerUserId })
                .HasPrincipalKey(x => new { x.Id, x.FirstOriginalQuestionId, x.SecondOriginalQuestionId, x.OwnerUserId }).HasConstraintName("FK_EduOriginVersions_Root").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationQuestionOriginRelationRevision>().WithMany().HasForeignKey(x => new { x.BasedOnRevisionId, x.RelationId })
                .HasPrincipalKey(x => new { x.Id, x.RelationId }).HasConstraintName("FK_EduOriginVersions_Previous").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentQuestionGrade>().WithMany().HasForeignKey(x => new { x.FirstQuestionGradeId, x.OwnerUserId })
                .HasPrincipalKey(x => new { x.Id, x.OwnerUserId }).HasConstraintName("FK_EduOriginVersions_FirstGrade").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentQuestionGrade>().WithMany().HasForeignKey(x => new { x.SecondQuestionGradeId, x.OwnerUserId })
                .HasPrincipalKey(x => new { x.Id, x.OwnerUserId }).HasConstraintName("FK_EduOriginVersions_SecondGrade").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<QuestionDefinitionRevision>().WithMany().HasForeignKey(x => new { x.FirstDefinitionRevisionId, QuestionId = x.FirstOriginalQuestionId })
                .HasPrincipalKey(x => new { x.Id, x.QuestionId }).HasConstraintName("FK_EduOriginVersions_FirstDefinition").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<QuestionDefinitionRevision>().WithMany().HasForeignKey(x => new { x.SecondDefinitionRevisionId, QuestionId = x.SecondOriginalQuestionId })
                .HasPrincipalKey(x => new { x.Id, x.QuestionId }).HasConstraintName("FK_EduOriginVersions_SecondDefinition").OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void Root<T>(EntityTypeBuilder<T> b, string table, string prefix) where T : class
    {
        b.ToTable(KnowPulseConsts.DbTablePrefix + table, KnowPulseConsts.DbSchema); b.ConfigureByConvention();
        b.ToTable(t => t.HasCheckConstraint("CK_" + prefix + "_Revision", "(\"RevisionNumber\" = 0 AND \"CurrentRevisionId\" IS NULL) OR (\"RevisionNumber\" > 0 AND \"CurrentRevisionId\" IS NOT NULL)"));
    }
    private static void Revision<T>(EntityTypeBuilder<T> b, string table, string prefix, string rootProperty) where T : class
    {
        b.ToTable(KnowPulseConsts.DbTablePrefix + table, KnowPulseConsts.DbSchema); b.ConfigureByConvention();
        b.Property<string>("Reason").IsRequired().HasMaxLength(2048); b.Property<string>("RequestFingerprint").IsRequired().HasMaxLength(64);
        b.Property<string>("ExpectedConcurrencyStamp").IsRequired().HasMaxLength(40);
        b.Property<DateTime>("RecordedAtUtc").HasConversion(new ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc)));
        b.HasIndex(rootProperty, "RevisionNumber").IsUnique().HasDatabaseName("IX_" + prefix + "_Sequence");
        b.ToTable(t => t.HasCheckConstraint("CK_" + prefix + "_Revision", "\"RevisionNumber\" > 0 AND length(trim(\"Reason\")) > 0 AND length(\"Reason\") <= 2048"));
    }
}
