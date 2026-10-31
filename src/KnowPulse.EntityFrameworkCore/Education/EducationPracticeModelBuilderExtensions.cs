using System;
using KnowPulse.Education;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace KnowPulse.EntityFrameworkCore;

public static class EducationPracticeModelBuilderExtensions
{
    public static void ConfigureEducationPractice(this ModelBuilder builder)
    {
        var utc = new ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        var nullableUtc = new ValueConverter<DateTime?, DateTime?>(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null);
        builder.Entity<EducationQuestionCollection>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "EducationQuestionCollections", KnowPulseConsts.DbSchema); b.ConfigureByConvention();
            b.Property(x => x.RequestFingerprint).IsRequired().HasMaxLength(64); b.Property(x => x.RecordedAtUtc).HasConversion(utc);
            b.HasAlternateKey(x => new { x.Id, x.RecipientId, x.PaperQuestionId, x.StudentUserId }).HasName("AK_EduCollection_StudentQuestion");
            b.HasIndex(x => new { x.RecipientId, x.PaperQuestionId, x.RevisionNumber }).IsUnique().HasDatabaseName("IX_EduCollection_Sequence");
            b.HasIndex(x => new { x.TenantId, x.StudentUserId, x.SchoolId, x.RecordedAtUtc }).HasDatabaseName("IX_EduCollection_Student");
            b.HasOne<EducationQuestionCollection>().WithMany().HasForeignKey(x => new { x.PreviousOperationId, x.RecipientId, x.PaperQuestionId, x.StudentUserId })
                .HasPrincipalKey(x => new { x.Id, x.RecipientId, x.PaperQuestionId, x.StudentUserId }).HasConstraintName("FK_EduCollection_Previous").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentRecipient>().WithMany().HasForeignKey(x => new { x.RecipientId, x.TaskId, x.TaskRevisionId, x.StudentUserId })
                .HasPrincipalKey(x => new { x.Id, x.TaskId, x.TaskRevisionId, x.StudentUserId }).HasConstraintName("FK_EduCollection_Recipient").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentTaskRevision>().WithMany().HasForeignKey(x => new { x.TaskRevisionId, x.TaskId, x.ExamPaperId })
                .HasPrincipalKey(x => new { x.Id, x.TaskId, x.ExamPaperId }).HasConstraintName("FK_EduCollection_TaskRevision").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ExamPaperQuestion>().WithMany().HasForeignKey(x => new { x.PaperQuestionId, x.ExamPaperId })
                .HasPrincipalKey(x => new { x.Id, x.PaperId }).HasConstraintName("FK_EduCollection_FrozenQuestion").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentResultRelease>().WithMany().HasForeignKey(x => new { x.ResultReleaseId, x.TaskId })
                .HasPrincipalKey(x => new { x.Id, x.TaskId }).HasConstraintName("FK_EduCollection_SourceRelease").OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_EduCollection_Revision", "length(\"RequestFingerprint\") = 64 AND ((\"RevisionNumber\" = 1 AND \"PreviousOperationId\" IS NULL AND \"IsCollected\") OR (\"RevisionNumber\" > 1 AND \"PreviousOperationId\" IS NOT NULL)) AND ((\"IsCollected\" AND \"ResultReleaseId\" IS NOT NULL) OR (NOT \"IsCollected\" AND \"ResultReleaseId\" IS NULL))"));
        });
        builder.Entity<EducationQuestionPractice>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "EducationQuestionPractices", KnowPulseConsts.DbSchema); b.ConfigureByConvention();
            b.HasAlternateKey(x => new { x.Id, x.StudentUserId }).HasName("AK_EduPractice_Student");
            b.HasIndex(x => new { x.TenantId, x.StudentUserId, x.RecipientId, x.StartedAtUtc }).HasDatabaseName("IX_EduPractice_Student");
            b.HasIndex(x => new { x.TenantId, x.TeacherUserId, x.SourceSubmissionId, x.SubmittedAtUtc }).HasDatabaseName("IX_EduPractice_Teacher");
            b.HasIndex(x => x.SubmissionOperationId).IsUnique().HasFilter("\"SubmissionOperationId\" IS NOT NULL").HasDatabaseName("IX_EduPractice_SubmissionOperation");
            b.HasOne<EducationAssessmentRecipient>().WithMany().HasForeignKey(x => new { x.RecipientId, x.TaskId, x.TaskRevisionId, x.StudentUserId })
                .HasPrincipalKey(x => new { x.Id, x.TaskId, x.TaskRevisionId, x.StudentUserId }).HasConstraintName("FK_EduPractice_Recipient").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentTaskRevision>().WithMany().HasForeignKey(x => new { x.TaskRevisionId, x.TaskId, x.ExamPaperId })
                .HasPrincipalKey(x => new { x.Id, x.TaskId, x.ExamPaperId }).HasConstraintName("FK_EduPractice_TaskRevision").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentQuestionGrade>().WithMany().HasForeignKey(x => new { x.QuestionGradeId, x.TeacherUserId, x.StudentUserId, x.TaskId, x.TaskRevisionId, x.SourceSubmissionId, x.PaperQuestionId })
                .HasPrincipalKey(x => new { x.Id, x.OwnerUserId, x.StudentUserId, x.TaskId, x.TaskRevisionId, x.SubmissionId, x.PaperQuestionId }).HasConstraintName("FK_EduPractice_SourceGrade").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentGradeRevision>().WithMany().HasForeignKey(x => x.GradeRevisionId).HasConstraintName("FK_EduPractice_SourceGradeRevision").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentResultRelease>().WithMany().HasForeignKey(x => new { x.ResultReleaseId, x.TaskId })
                .HasPrincipalKey(x => new { x.Id, x.TaskId }).HasConstraintName("FK_EduPractice_SourceRelease").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ExamPaperQuestion>().WithMany().HasForeignKey(x => new { x.PaperQuestionId, x.ExamPaperId })
                .HasPrincipalKey(x => new { x.Id, x.PaperId }).HasConstraintName("FK_EduPractice_FrozenQuestion").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<QuestionDefinitionRevision>().WithMany().HasForeignKey(x => x.QuestionDefinitionRevisionId).HasConstraintName("FK_EduPractice_Definition").OnDelete(DeleteBehavior.Restrict);
            b.Property(x => x.StartFingerprint).IsRequired().HasMaxLength(64); b.Property(x => x.SubmissionFingerprint).HasMaxLength(64); b.Property(x => x.PreparationFingerprint).HasMaxLength(64);
            b.Property(x => x.ResponseJson).HasMaxLength(EducationAssessmentResponseValue.MaxJsonLength); b.Property(x => x.EvaluationJson).HasMaxLength(64000);
            b.Property(x => x.StartedAtUtc).HasConversion(utc); b.Property(x => x.SubmittedAtUtc).HasConversion(nullableUtc);
            b.ToTable(t => t.HasCheckConstraint("CK_EduPractice_Submission", "length(\"StartFingerprint\") = 64 AND ((\"SubmissionOperationId\" IS NULL AND \"SubmissionFingerprint\" IS NULL AND \"PreparationFingerprint\" IS NULL AND \"SubmittedAtUtc\" IS NULL AND \"ResponseJson\" IS NULL AND \"EvaluationJson\" IS NULL) OR (\"SubmissionOperationId\" IS NOT NULL AND \"SubmissionOperationId\" <> \"Id\" AND \"SubmissionFingerprint\" IS NOT NULL AND \"PreparationFingerprint\" IS NOT NULL AND \"SubmittedAtUtc\" IS NOT NULL AND length(\"SubmissionFingerprint\") = 64 AND length(\"PreparationFingerprint\") = 64 AND \"SubmittedAtUtc\" >= \"StartedAtUtc\" AND \"ResponseJson\" IS NOT NULL AND \"EvaluationJson\" IS NOT NULL))"));
        });
        builder.Entity<EducationPracticeImage>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "EducationPracticeImages", KnowPulseConsts.DbSchema); b.ConfigureByConvention();
            b.HasIndex(x => new { x.PracticeId, x.StudentUserId }).HasDatabaseName("IX_EduPracticeImages_Student");
            b.HasOne<EducationQuestionPractice>().WithMany().HasForeignKey(x => new { x.PracticeId, x.StudentUserId })
                .HasPrincipalKey(x => new { x.Id, x.StudentUserId }).HasConstraintName("FK_EduPracticeImages_Practice").OnDelete(DeleteBehavior.Restrict);
            b.Property(x => x.FileName).IsRequired().HasMaxLength(256); b.Property(x => x.ContentType).IsRequired().HasMaxLength(128); b.Property(x => x.BlobName).IsRequired().HasMaxLength(512);
            b.Property(x => x.ContentHash).IsRequired().HasMaxLength(64); b.Property(x => x.RequestFingerprint).IsRequired().HasMaxLength(64);
            b.Property(x => x.ReservedAtUtc).HasConversion(utc); b.Property(x => x.UploadedAtUtc).HasConversion(nullableUtc);
            b.ToTable(t => t.HasCheckConstraint("CK_EduPracticeImages_Upload", "\"Size\" BETWEEN 1 AND 5242880 AND length(\"ContentHash\") = 64 AND length(\"RequestFingerprint\") = 64 AND (\"UploadedAtUtc\" IS NULL OR \"UploadedAtUtc\" >= \"ReservedAtUtc\")"));
        });
    }
}
