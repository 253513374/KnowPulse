using System;
using KnowPulse.Education;
using KnowPulse.Exam;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace KnowPulse.EntityFrameworkCore;

public static class EducationAssessmentResponseModelBuilderExtensions
{
    public static void ConfigureEducationAssessmentResponses(this ModelBuilder builder)
    {
        var utc = new ValueConverter<DateTime, DateTime>(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        var nullableUtc = new ValueConverter<DateTime?, DateTime?>(value => value,
            value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null);
        builder.Entity<ExamPaper>().HasAlternateKey(x => new { x.Id, x.SnapshotVersion }).HasName("AK_ExamPapers_Snapshot");
        builder.Entity<ExamPaperQuestion>().HasAlternateKey(x => new { x.Id, x.PaperId }).HasName("AK_ExamPaperQuestions_Paper");
        builder.Entity<EducationAssessmentAttempt>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "EducationAssessmentAttempts", KnowPulseConsts.DbSchema, table =>
                table.HasCheckConstraint("CK_EduAttempts_ResponseVersion",
                    "(\"ResponseSetVersion\" = 0 AND \"LastSavedAtUtc\" IS NULL) OR " +
                    "(\"ResponseSetVersion\" > 0 AND \"LastSavedAtUtc\" IS NOT NULL AND \"LastSavedAtUtc\" >= \"StartedAtUtc\" AND \"LastSavedAtUtc\" < \"ExpiresAtUtc\")"));
            b.Ignore(x => x.Status);
            b.Property(x => x.LastSavedAtUtc).HasConversion(nullableUtc);
            b.HasAlternateKey(x => new { x.Id, x.StudentUserId, x.ExamPaperId }).HasName("AK_EduAttempts_StudentPaper");
            b.HasIndex(x => new { x.SubmissionId, x.ExpiresAtUtc }).HasDatabaseName("IX_EduAttempts_DueCutoff");
            b.HasOne<EducationAssessmentSubmission>().WithMany()
                .HasForeignKey(x => new { x.SubmissionId, AttemptId = x.Id, x.ResponseSetVersion })
                .HasPrincipalKey(x => new { x.Id, x.AttemptId, x.ResponseSetVersion })
                .HasConstraintName("FK_EduAttempts_SubmissionVersion").OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<EducationAssessmentResponseRevision>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "EducationAssessmentResponseRevisions", KnowPulseConsts.DbSchema, table =>
                table.HasCheckConstraint("CK_EduResponseRevisions_Version",
                    "\"PaperSnapshotVersion\" > 0 AND \"ExpectedResponseSetVersion\" >= 0 AND \"ResultResponseSetVersion\" > 0 AND " +
                    "CAST(\"ResultResponseSetVersion\" AS bigint) = CAST(\"ExpectedResponseSetVersion\" AS bigint) + 1"));
            b.ConfigureByConvention();
            b.Property(x => x.RequestFingerprint).IsRequired().HasMaxLength(64);
            b.Property(x => x.ResponseHash).IsRequired().HasMaxLength(64);
            b.Property(x => x.ResponseJson).IsRequired().HasMaxLength(EducationAssessmentResponseValue.MaxJsonLength);
            b.Property(x => x.SavedAtUtc).HasConversion(utc);
            b.HasAlternateKey(x => new { x.Id, x.AttemptId, x.ExamPaperId, x.PaperQuestionId, x.ResultResponseSetVersion })
                .HasName("AK_EduResponseRevisions_QuestionVersion");
            b.HasIndex(x => new { x.AttemptId, x.ResultResponseSetVersion }).IsUnique().HasDatabaseName("IX_EduResponseRevisions_AttemptVersion");
            b.HasOne<EducationAssessmentAttempt>().WithMany()
                .HasForeignKey(x => new { x.AttemptId, x.StudentUserId, x.ExamPaperId })
                .HasPrincipalKey(x => new { x.Id, x.StudentUserId, x.ExamPaperId })
                .HasConstraintName("FK_EduResponseRevisions_AttemptStudentPaper").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ExamPaperQuestion>().WithMany().HasForeignKey(x => new { x.PaperQuestionId, PaperId = x.ExamPaperId })
                .HasPrincipalKey(x => new { x.Id, x.PaperId })
                .HasConstraintName("FK_EduResponseRevisions_PaperQuestion").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ExamPaper>().WithMany().HasForeignKey(x => new { x.ExamPaperId, SnapshotVersion = x.PaperSnapshotVersion })
                .HasPrincipalKey(x => new { x.Id, x.SnapshotVersion })
                .HasConstraintName("FK_EduResponseRevisions_PaperSnapshot").OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<EducationAssessmentCurrentResponse>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "EducationAssessmentCurrentResponses", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.HasIndex(x => new { x.AttemptId, x.PaperQuestionId }).IsUnique().HasDatabaseName("IX_EduCurrentResponses_AttemptQuestion");
            b.HasOne<EducationAssessmentResponseRevision>().WithMany()
                .HasForeignKey(x => new { x.CurrentRevisionId, x.AttemptId, x.ExamPaperId, x.PaperQuestionId, x.ResponseSetVersion })
                .HasPrincipalKey(x => new { x.Id, x.AttemptId, x.ExamPaperId, x.PaperQuestionId, x.ResultResponseSetVersion })
                .HasConstraintName("FK_EduCurrentResponses_RevisionQuestionVersion").OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<EducationAssessmentSubmission>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "EducationAssessmentSubmissions", KnowPulseConsts.DbSchema, table =>
                table.HasCheckConstraint("CK_EduSubmissions_Receipt",
                    "\"PaperSnapshotVersion\" > 0 AND \"ResponseSetVersion\" >= 0 AND \"Cause\" IN (0, 1) AND " +
                    "\"EffectiveAtUtc\" <= \"SealedAtUtc\" AND (\"Cause\" = 0 OR NOT \"ConfirmedSavedOnly\")"));
            b.ConfigureByConvention();
            b.Property(x => x.RequestFingerprint).IsRequired().HasMaxLength(64);
            b.Property(x => x.ManifestJson).IsRequired().HasMaxLength(EducationAssessmentSubmission.MaxManifestJsonLength);
            b.Property(x => x.EffectiveAtUtc).HasConversion(utc);
            b.Property(x => x.SealedAtUtc).HasConversion(utc);
            b.HasAlternateKey(x => new { x.Id, x.AttemptId, x.ResponseSetVersion }).HasName("AK_EduSubmissions_AttemptVersion");
            b.HasIndex(x => x.AttemptId).IsUnique().HasDatabaseName("IX_EduSubmissions_OnePerAttempt");
            b.HasOne<EducationAssessmentAttempt>().WithMany()
                .HasForeignKey(x => new { x.AttemptId, x.StudentUserId, x.ExamPaperId })
                .HasPrincipalKey(x => new { x.Id, x.StudentUserId, x.ExamPaperId })
                .HasConstraintName("FK_EduSubmissions_AttemptStudentPaper").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ExamPaper>().WithMany().HasForeignKey(x => new { x.ExamPaperId, SnapshotVersion = x.PaperSnapshotVersion })
                .HasPrincipalKey(x => new { x.Id, x.SnapshotVersion })
                .HasConstraintName("FK_EduSubmissions_PaperSnapshot").OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<EducationAssessmentAnswerAttachment>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "EducationAssessmentAnswerAttachments", KnowPulseConsts.DbSchema, table =>
                table.HasCheckConstraint("CK_EduAnswerAttachments_Upload",
                    "\"Size\" > 0 AND \"Size\" <= 20971520 AND \"ContentType\" IN ('image/jpeg', 'image/png', 'image/webp') AND " +
                    "(\"UploadedAtUtc\" IS NULL OR \"UploadedAtUtc\" >= \"RequestedAtUtc\")"));
            b.ConfigureByConvention();
            b.Ignore(x => x.IsUploaded);
            b.Property(x => x.FileName).IsRequired().HasMaxLength(256);
            b.Property(x => x.ContentType).IsRequired().HasMaxLength(128);
            b.Property(x => x.ContentHash).IsRequired().HasMaxLength(64);
            b.Property(x => x.BlobName).IsRequired().HasMaxLength(512);
            b.Property(x => x.RequestFingerprint).IsRequired().HasMaxLength(64);
            b.Property(x => x.RequestedAtUtc).HasConversion(utc);
            b.Property(x => x.UploadedAtUtc).HasConversion(nullableUtc);
            b.HasIndex(x => new { x.AttemptId, x.PaperQuestionId }).HasDatabaseName("IX_EduAnswerAttachments_AttemptQuestion");
            b.HasOne<EducationAssessmentAttempt>().WithMany()
                .HasForeignKey(x => new { x.AttemptId, x.StudentUserId, x.ExamPaperId })
                .HasPrincipalKey(x => new { x.Id, x.StudentUserId, x.ExamPaperId })
                .HasConstraintName("FK_EduAnswerAttachments_AttemptStudentPaper").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ExamPaperQuestion>().WithMany().HasForeignKey(x => new { x.PaperQuestionId, PaperId = x.ExamPaperId })
                .HasPrincipalKey(x => new { x.Id, x.PaperId })
                .HasConstraintName("FK_EduAnswerAttachments_PaperQuestion").OnDelete(DeleteBehavior.Restrict);
        });
    }
}
