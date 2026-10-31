using System;
using KnowPulse.Education;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace KnowPulse.EntityFrameworkCore;

public static class EducationAssessmentGradingModelBuilderExtensions
{
    public static void ConfigureEducationAssessmentGrading(this ModelBuilder builder)
    {
        var utc = new ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        var nullableUtc = new ValueConverter<DateTime?, DateTime?>(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null);
        builder.Entity<EducationAssessmentSubmission>().HasAlternateKey(x => new { x.Id, x.AttemptId, x.StudentUserId, x.ExamPaperId }).HasName("AK_EduSubmissions_AttemptStudentPaper");
        builder.Entity<EducationAssessmentSubmission>().HasAlternateKey(x => new { x.Id, x.AttemptId, x.StudentUserId }).HasName("AK_EduSubmissions_AttemptStudent");
        builder.Entity<EducationAssessmentRecipient>().HasAlternateKey(x => new { x.Id, x.TaskId, x.StudentUserId }).HasName("AK_EduRecipients_TaskStudent");
        builder.Entity<EducationAssessmentResponseRevision>().HasAlternateKey(x => new { x.Id, x.AttemptId, x.ExamPaperId, x.PaperQuestionId }).HasName("AK_EduResponses_AttemptPaperQuestion");
        builder.Entity<EducationAssessmentTask>(b =>
        {
            b.HasOne<EducationAssessmentClosure>().WithMany().HasForeignKey(x => new { x.ClosureId, TaskId = x.Id })
                .HasPrincipalKey(x => new { x.Id, x.TaskId }).HasConstraintName("FK_EduTasks_Closure").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentResultRelease>().WithMany().HasForeignKey(x => new { x.CurrentResultReleaseId, TaskId = x.Id })
                .HasPrincipalKey(x => new { x.Id, x.TaskId }).HasConstraintName("FK_EduTasks_ResultRelease").OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<EducationAssessmentQuestionGrade>(b =>
        {
            Table(b, "EducationAssessmentQuestionGrades");
            b.HasAlternateKey(x => new { x.Id, x.SubmissionId, x.PaperQuestionId }).HasName("AK_EduGrades_SubmissionQuestion");
            b.HasIndex(x => new { x.SubmissionId, x.PaperQuestionId }).IsUnique().HasDatabaseName("IX_EduGrades_OnePerQuestion");
            b.HasOne<EducationAssessmentSubmission>().WithMany().HasForeignKey(x => new { x.SubmissionId, x.AttemptId, x.StudentUserId, x.ExamPaperId })
                .HasPrincipalKey(x => new { x.Id, x.AttemptId, x.StudentUserId, x.ExamPaperId }).HasConstraintName("FK_EduGrades_Submission").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ExamPaperQuestion>().WithMany().HasForeignKey(x => new { x.PaperQuestionId, PaperId = x.ExamPaperId })
                .HasPrincipalKey(x => new { x.Id, x.PaperId }).HasConstraintName("FK_EduGrades_PaperQuestion").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentTask>().WithMany().HasForeignKey(x => new { x.TaskId, x.OwnerUserId })
                .HasPrincipalKey(x => new { x.Id, x.OwnerUserId }).HasConstraintName("FK_EduGrades_TaskOwner").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentTaskRevision>().WithMany().HasForeignKey(x => new { x.TaskRevisionId, x.TaskId, x.ExamPaperId })
                .HasPrincipalKey(x => new { x.Id, x.TaskId, x.ExamPaperId }).HasConstraintName("FK_EduGrades_TaskRevision").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentGradeRevision>().WithMany().HasForeignKey(x => new { x.CurrentRevisionId, QuestionGradeId = x.Id })
                .HasPrincipalKey(x => new { x.Id, x.QuestionGradeId }).HasConstraintName("FK_EduGrades_Current").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentGradeRevision>().WithMany().HasForeignKey(x => new { x.CurrentConfirmedRevisionId, QuestionGradeId = x.Id })
                .HasPrincipalKey(x => new { x.Id, x.QuestionGradeId }).HasConstraintName("FK_EduGrades_Confirmed").OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<EducationAssessmentGradeRevision>(b =>
        {
            Table(b, "EducationAssessmentGradeRevisions"); b.Ignore(x => x.Content);
            b.Property(x => x.ContentJson).IsRequired().HasMaxLength(EducationAssessmentGradeContent.MaxJsonLength);
            b.Property(x => x.ExpectedConcurrencyStamp).IsRequired().HasMaxLength(40); b.Property(x => x.RequestFingerprint).IsRequired().HasMaxLength(64);
            b.Property(x => x.ResponseHash).HasMaxLength(64); b.Property(x => x.ScoringDefinitionHash).IsRequired().HasMaxLength(64);
            b.Property(x => x.CalculationVersion).IsRequired().HasMaxLength(64); b.Property(x => x.Feedback).HasMaxLength(4000); b.Property(x => x.Reason).HasMaxLength(2048);
            b.Property(x => x.MaxScore).HasPrecision(18, 2); b.Property(x => x.ObtainedScore).HasPrecision(18, 2);
            b.Property(x => x.RecordedAtUtc).HasConversion(utc); b.Property(x => x.ConfirmedAtUtc).HasConversion(nullableUtc);
            b.HasAlternateKey(x => new { x.Id, x.QuestionGradeId }).HasName("AK_EduGradeRevisions_Grade");
            b.HasIndex(x => new { x.QuestionGradeId, x.RevisionNumber }).IsUnique().HasDatabaseName("IX_EduGradeRevisions_Number");
            b.HasIndex(x => new { x.TaskId, x.ResultGradeSetVersion }).IsUnique().HasFilter("\"ResultGradeSetVersion\" IS NOT NULL").HasDatabaseName("IX_EduGradeRevisions_ConfirmedSet");
            b.HasOne<EducationAssessmentQuestionGrade>().WithMany().HasForeignKey(x => new { x.QuestionGradeId, x.SubmissionId, x.PaperQuestionId })
                .HasPrincipalKey(x => new { x.Id, x.SubmissionId, x.PaperQuestionId }).HasConstraintName("FK_EduGradeRevisions_Grade").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentSubmission>().WithMany().HasForeignKey(x => new { x.SubmissionId, x.AttemptId, x.StudentUserId, x.ExamPaperId })
                .HasPrincipalKey(x => new { x.Id, x.AttemptId, x.StudentUserId, x.ExamPaperId }).HasConstraintName("FK_EduGradeRevisions_Submission").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentResponseRevision>().WithMany().HasForeignKey(x => new { x.ResponseRevisionId, x.AttemptId, x.ExamPaperId, x.PaperQuestionId })
                .HasPrincipalKey(x => new { x.Id, x.AttemptId, x.ExamPaperId, x.PaperQuestionId }).HasConstraintName("FK_EduGradeRevisions_Response").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<QuestionDefinitionRevision>().WithMany().HasForeignKey(x => x.QuestionDefinitionRevisionId).HasConstraintName("FK_EduGradeRevisions_Definition").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentGradeRevision>().WithMany().HasForeignKey(x => new { x.BasedOnRevisionId, x.QuestionGradeId })
                .HasPrincipalKey(x => new { x.Id, x.QuestionGradeId }).HasConstraintName("FK_EduGradeRevisions_Previous").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentGradeRevision>().WithMany().HasForeignKey(x => new { x.SupersedesRevisionId, x.QuestionGradeId })
                .HasPrincipalKey(x => new { x.Id, x.QuestionGradeId }).HasConstraintName("FK_EduGradeRevisions_Superseded").OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_EduGradeRevisions_Marks",
                "\"RevisionNumber\" > 0 AND CAST(\"MaxScore\" AS numeric) > 0 AND (\"ObtainedScore\" IS NULL OR " +
                "(CAST(\"ObtainedScore\" AS numeric) >= 0 AND CAST(\"ObtainedScore\" AS numeric) <= CAST(\"MaxScore\" AS numeric))) AND " +
                "((\"Kind\" = 0 AND \"ConfirmedAtUtc\" IS NULL AND \"ResultGradeSetVersion\" IS NULL AND \"ExpectedGradeSetVersion\" IS NULL) OR " +
                "(\"Kind\" = 1 AND \"ConfirmedAtUtc\" IS NOT NULL AND \"ObtainedScore\" IS NOT NULL AND \"ExpectedGradeSetVersion\" IS NOT NULL AND " +
                "\"ResultGradeSetVersion\" IS NOT NULL AND \"ExpectedGradeSetVersion\" >= 0 AND CAST(\"ResultGradeSetVersion\" AS bigint) = CAST(\"ExpectedGradeSetVersion\" AS bigint) + 1))"));
        });
        builder.Entity<EducationAssessmentClosure>(b =>
        {
            Table(b, "EducationAssessmentClosures"); b.Ignore(x => x.Manifest);
            b.Property(x => x.ManifestJson).IsRequired().HasMaxLength(EducationAssessmentClosureValue.MaxJsonLength);
            b.Property(x => x.RequestFingerprint).IsRequired().HasMaxLength(64); b.Property(x => x.ExpectedConcurrencyStamp).IsRequired().HasMaxLength(40);
            b.Property(x => x.SubmissionSetFingerprint).IsRequired().HasMaxLength(64);
            b.Property(x => x.EffectiveAtUtc).HasConversion(utc); b.Property(x => x.ClosedAtUtc).HasConversion(utc);
            b.HasAlternateKey(x => new { x.Id, x.TaskId }).HasName("AK_EduClosures_Task");
            b.HasIndex(x => x.TaskId).IsUnique().HasDatabaseName("IX_EduClosures_OnePerTask");
            b.HasOne<EducationAssessmentTaskRevision>().WithMany().HasForeignKey(x => new { x.TaskRevisionId, x.TaskId })
                .HasPrincipalKey(x => new { x.Id, x.TaskId }).HasConstraintName("FK_EduClosures_PublishedTask").OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<EducationAssessmentResultRelease>(b =>
        {
            Table(b, "EducationAssessmentResultReleases");
            b.Property(x => x.RequestFingerprint).IsRequired().HasMaxLength(64); b.Property(x => x.ExpectedConcurrencyStamp).IsRequired().HasMaxLength(40);
            b.Property(x => x.SubmissionSetFingerprint).IsRequired().HasMaxLength(64); b.Property(x => x.Reason).HasMaxLength(2048);
            b.Property(x => x.ScoreOpensAtUtc).HasConversion(nullableUtc); b.Property(x => x.ReviewOpensAtUtc).HasConversion(nullableUtc); b.Property(x => x.ReleasedAtUtc).HasConversion(utc);
            b.HasAlternateKey(x => new { x.Id, x.TaskId }).HasName("AK_EduReleases_Task");
            b.HasOne<EducationAssessmentClosure>().WithMany().HasForeignKey(x => new { x.ClosureId, x.TaskId })
                .HasPrincipalKey(x => new { x.Id, x.TaskId }).HasConstraintName("FK_EduReleases_Closure").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentResultRelease>().WithMany().HasForeignKey(x => new { x.PreviousReleaseId, x.TaskId })
                .HasPrincipalKey(x => new { x.Id, x.TaskId }).HasConstraintName("FK_EduReleases_Previous").OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<EducationAssessmentResultReleaseItem>(b =>
        {
            Table(b, "EducationAssessmentResultReleaseItems"); b.Ignore(x => x.Grades);
            b.Property(x => x.GradesJson).IsRequired().HasMaxLength(EducationAssessmentReleasedGradesValue.MaxJsonLength);
            b.Property(x => x.TotalScore).HasPrecision(18, 2); b.Property(x => x.MaxScore).HasPrecision(18, 2);
            b.HasIndex(x => new { x.ResultReleaseId, x.RecipientId }).IsUnique().HasDatabaseName("IX_EduReleaseItems_Recipient");
            b.HasOne<EducationAssessmentResultRelease>().WithMany().HasForeignKey(x => new { x.ResultReleaseId, x.TaskId })
                .HasPrincipalKey(x => new { x.Id, x.TaskId }).HasConstraintName("FK_EduReleaseItems_Release").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentSubmission>().WithMany().HasForeignKey(x => new { x.SubmissionId, x.AttemptId, x.StudentUserId })
                .HasPrincipalKey(x => new { x.Id, x.AttemptId, x.StudentUserId }).HasConstraintName("FK_EduReleaseItems_Submission").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentRecipient>().WithMany().HasForeignKey(x => new { x.RecipientId, x.TaskId, x.StudentUserId })
                .HasPrincipalKey(x => new { x.Id, x.TaskId, x.StudentUserId }).HasConstraintName("FK_EduReleaseItems_Recipient").OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<EducationAssessmentScoreReviewRequest>(b =>
        {
            Table(b, "EducationAssessmentScoreReviewRequests");
            b.Property(x => x.Reason).IsRequired().HasMaxLength(2048); b.Property(x => x.ResolutionReason).HasMaxLength(2048);
            b.Property(x => x.RequestFingerprint).IsRequired().HasMaxLength(64); b.Property(x => x.ResolutionRequestFingerprint).HasMaxLength(64);
            b.Property(x => x.ExpectedResolutionConcurrencyStamp).HasMaxLength(40); b.Property(x => x.RequestedAtUtc).HasConversion(utc); b.Property(x => x.ResolvedAtUtc).HasConversion(nullableUtc);
            b.HasIndex(x => x.ResolutionOperationId).IsUnique().HasFilter("\"ResolutionOperationId\" IS NOT NULL").HasDatabaseName("IX_EduScoreReviews_Resolution");
            b.HasOne<EducationAssessmentResultRelease>().WithMany().HasForeignKey(x => new { x.ResultReleaseId, x.TaskId })
                .HasPrincipalKey(x => new { x.Id, x.TaskId }).HasConstraintName("FK_EduScoreReviews_Release").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentGradeRevision>().WithMany().HasForeignKey(x => x.CorrectedGradeRevisionId).HasConstraintName("FK_EduScoreReviews_Correction").OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void Table<T>(EntityTypeBuilder<T> b, string name) where T : class
    { b.ToTable(KnowPulseConsts.DbTablePrefix + name, KnowPulseConsts.DbSchema); b.ConfigureByConvention(); }
}
