using System;
using KnowPulse.Education;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace KnowPulse.EntityFrameworkCore;

public static class EducationCorrectionModelBuilderExtensions
{
    public static void ConfigureEducationCorrections(this ModelBuilder builder)
    {
        var utc = new ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        var nullableUtc = new ValueConverter<DateTime?, DateTime?>(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null);
        builder.Entity<EducationAssessmentQuestionGrade>().HasAlternateKey(x => new { x.Id, x.OwnerUserId, x.StudentUserId, x.TaskId, x.TaskRevisionId, x.SubmissionId, x.PaperQuestionId }).HasName("AK_EduGrades_CorrectionSource");
        builder.Entity<EducationCorrectionTask>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "EducationCorrectionTasks", KnowPulseConsts.DbSchema); b.ConfigureByConvention();
            b.HasAlternateKey(x => new { x.Id, x.OwnerUserId, x.StudentUserId, x.QuestionGradeId }).HasName("AK_EduCorrections_Source");
            b.HasIndex(x => new { x.TenantId, x.OwnerUserId, x.CreationTime }).HasDatabaseName("IX_EduCorrections_Owner");
            b.HasOne<EducationAssessmentQuestionGrade>().WithMany().HasForeignKey(x => new { x.QuestionGradeId, x.OwnerUserId, x.StudentUserId, TaskId = x.SourceTaskId, TaskRevisionId = x.SourceTaskRevisionId, x.SubmissionId, x.PaperQuestionId })
                .HasPrincipalKey(x => new { x.Id, x.OwnerUserId, x.StudentUserId, x.TaskId, x.TaskRevisionId, x.SubmissionId, x.PaperQuestionId }).HasConstraintName("FK_EduCorrections_Grade").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentRecipient>().WithMany().HasForeignKey(x => new { x.AssessmentRecipientId, TaskId = x.SourceTaskId, TaskRevisionId = x.SourceTaskRevisionId, x.StudentUserId })
                .HasPrincipalKey(x => new { x.Id, x.TaskId, x.TaskRevisionId, x.StudentUserId }).HasConstraintName("FK_EduCorrections_Recipient").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<TeachingAssignment>().WithMany().HasForeignKey(x => new { x.TeachingAssignmentId, x.SchoolId, x.AcademicTermId, x.SchoolClassId, TeacherUserId = x.OwnerUserId, x.EducationSubjectId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId, x.AcademicTermId, x.SchoolClassId, x.TeacherUserId, x.EducationSubjectId }).HasConstraintName("FK_EduCorrections_Teaching").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<SchoolMembership>().WithMany().HasForeignKey(x => new { x.SchoolMembershipId, x.SchoolId, UserId = x.StudentUserId }).HasPrincipalKey(x => new { x.Id, x.SchoolId, x.UserId }).HasConstraintName("FK_EduCorrections_Member").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<StudentEnrollment>().WithMany().HasForeignKey(x => new { x.StudentEnrollmentId, x.SchoolId, x.SchoolClassId, x.StudentUserId }).HasPrincipalKey(x => new { x.Id, x.SchoolId, x.SchoolClassId, x.StudentUserId }).HasConstraintName("FK_EduCorrections_Enrollment").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationCorrectionTaskRevision>().WithMany().HasForeignKey(x => new { x.CurrentRevisionId, TaskId = x.Id, x.RevisionNumber }).HasPrincipalKey(x => new { x.Id, x.TaskId, x.RevisionNumber }).HasConstraintName("FK_EduCorrections_Current").OnDelete(DeleteBehavior.Restrict);
            foreach (var pointer in new[] { "PublishedRevisionId", "LastPublishedRevisionId" }) b.HasOne<EducationCorrectionTaskRevision>().WithMany().HasForeignKey(pointer, "Id", "PublicationKind").HasPrincipalKey(x => new { x.Id, x.TaskId, x.Kind }).HasConstraintName("FK_EduCorrections_" + pointer).OnDelete(DeleteBehavior.Restrict);
            b.Property(x => x.PublicationKind).HasDefaultValue(EducationCorrectionRevisionKind.Published);
            b.ToTable(t => t.HasCheckConstraint("CK_EduCorrections_State", "\"PublicationKind\" = 1 AND ((\"RevisionNumber\" = 0 AND \"CurrentRevisionId\" IS NULL) OR (\"RevisionNumber\" > 0 AND \"CurrentRevisionId\" IS NOT NULL)) AND (\"PublishedRevisionId\" IS NULL OR (\"LastPublishedRevisionId\" IS NOT NULL AND \"PublishedRevisionId\" = \"LastPublishedRevisionId\"))"));
        });
        builder.Entity<EducationCorrectionTaskRevision>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "EducationCorrectionTaskRevisions", KnowPulseConsts.DbSchema); b.ConfigureByConvention();
            b.HasAlternateKey(x => new { x.Id, x.TaskId }).HasName("AK_EduCorrectionVersions_Root");
            b.HasAlternateKey(x => new { x.Id, x.TaskId, x.RevisionNumber }).HasName("AK_EduCorrectionVersions_Sequence");
            b.HasAlternateKey(x => new { x.Id, x.TaskId, x.Kind }).HasName("AK_EduCorrectionVersions_Kind");
            b.HasAlternateKey(x => new { x.Id, x.TaskId, x.OwnerUserId, x.StudentUserId, x.Kind }).HasName("AK_EduCorrectionVersions_Delivery");
            b.HasIndex(x => new { x.TaskId, x.RevisionNumber }).IsUnique().HasDatabaseName("IX_EduCorrectionVersions_Sequence");
            b.HasOne<EducationCorrectionTask>().WithMany().HasForeignKey(x => new { x.TaskId, x.OwnerUserId, x.StudentUserId, x.QuestionGradeId }).HasPrincipalKey(x => new { x.Id, x.OwnerUserId, x.StudentUserId, x.QuestionGradeId }).HasConstraintName("FK_EduCorrectionVersions_Root").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentGradeRevision>().WithMany().HasForeignKey(x => new { x.GradeRevisionId, x.QuestionGradeId }).HasPrincipalKey(x => new { x.Id, x.QuestionGradeId }).HasConstraintName("FK_EduCorrectionVersions_Grade").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentResultRelease>().WithMany().HasForeignKey(x => x.ResultReleaseId).HasConstraintName("FK_EduCorrectionVersions_Release").OnDelete(DeleteBehavior.Restrict);
            foreach (var pointer in new[] { "BasedOnRevisionId", "SourceRevisionId" }) b.HasOne<EducationCorrectionTaskRevision>().WithMany().HasForeignKey(pointer, "TaskId").HasPrincipalKey(x => new { x.Id, x.TaskId }).HasConstraintName("FK_EduCorrectionVersions_" + pointer).OnDelete(DeleteBehavior.Restrict);
            b.Property(x => x.ContentJson).HasMaxLength(64 * 1024); b.Property(x => x.PublicReason).HasMaxLength(2000);
            b.Property(x => x.StudentName).IsRequired().HasMaxLength(256); b.Property(x => x.TeacherName).IsRequired().HasMaxLength(256);
            b.Property(x => x.ClassName).IsRequired().HasMaxLength(128); b.Property(x => x.SubjectName).IsRequired().HasMaxLength(128);
            b.Property(x => x.RequestFingerprint).IsRequired().HasMaxLength(64); b.Property(x => x.ExpectedConcurrencyStamp).IsRequired().HasMaxLength(40); b.Property(x => x.RecordedAtUtc).HasConversion(utc);
            b.ToTable(t => t.HasCheckConstraint("CK_EduCorrectionVersions_State", "\"RevisionNumber\" > 0 AND length(\"RequestFingerprint\") = 64 AND ((\"Kind\" = 0 AND \"SourceRevisionId\" IS NULL AND \"ContentJson\" IS NOT NULL) OR (\"Kind\" = 1 AND \"SourceRevisionId\" IS NOT NULL AND \"ContentJson\" IS NOT NULL) OR (\"Kind\" = 2 AND \"SourceRevisionId\" IS NOT NULL AND \"ContentJson\" IS NULL AND \"PublicReason\" IS NOT NULL AND length(trim(\"PublicReason\")) > 0))"));
        });
        builder.Entity<EducationCorrectionDelivery>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "EducationCorrectionDeliveries", KnowPulseConsts.DbSchema); b.ConfigureByConvention();
            b.HasAlternateKey(x => new { x.Id, x.StudentUserId, x.OwnerUserId }).HasName("AK_EduCorrectionDeliveries_Actors");
            b.HasAlternateKey(x => new { x.Id, x.StudentUserId }).HasName("AK_EduCorrectionDeliveries_Student");
            b.HasIndex(x => new { x.PublicationId, x.StudentUserId }).IsUnique().HasDatabaseName("IX_EduCorrectionDeliveries_Publication");
            b.HasIndex(x => new { x.TenantId, x.StudentUserId, x.AssignedAtUtc }).HasDatabaseName("IX_EduCorrectionDeliveries_Student");
            b.Property(x => x.AssignedAtUtc).HasConversion(utc); b.Property(x => x.FirstProvidedAtUtc).HasConversion(nullableUtc);
            b.Property(x => x.PublicationKind).HasDefaultValue(EducationCorrectionRevisionKind.Published);
            b.HasOne<EducationCorrectionTaskRevision>().WithMany().HasForeignKey(x => new { x.PublicationId, x.TaskId, x.OwnerUserId, x.StudentUserId, Kind = x.PublicationKind }).HasPrincipalKey(x => new { x.Id, x.TaskId, x.OwnerUserId, x.StudentUserId, x.Kind }).HasConstraintName("FK_EduCorrectionDeliveries_Publication").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationCorrectionSubmission>().WithMany().HasForeignKey(x => new { x.CurrentSubmissionId, DeliveryId = x.Id, x.SubmissionNumber }).HasPrincipalKey(x => new { x.Id, x.DeliveryId, x.SubmissionNumber }).HasConstraintName("FK_EduCorrectionDeliveries_Submission").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationCorrectionReview>().WithMany().HasForeignKey(x => new { x.CurrentReviewId, DeliveryId = x.Id, x.ReviewNumber }).HasPrincipalKey(x => new { x.Id, x.DeliveryId, x.ReviewNumber }).HasConstraintName("FK_EduCorrectionDeliveries_Review").OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_EduCorrectionDeliveries_State", "\"PublicationKind\" = 1 AND (\"FirstProvidedAtUtc\" IS NULL OR \"FirstProvidedAtUtc\" >= \"AssignedAtUtc\") AND ((\"SubmissionNumber\" = 0 AND \"CurrentSubmissionId\" IS NULL) OR (\"SubmissionNumber\" > 0 AND \"CurrentSubmissionId\" IS NOT NULL AND \"FirstProvidedAtUtc\" IS NOT NULL)) AND ((\"ReviewNumber\" = 0 AND \"CurrentReviewId\" IS NULL) OR (\"ReviewNumber\" > 0 AND \"CurrentReviewId\" IS NOT NULL AND \"CurrentSubmissionId\" IS NOT NULL))"));
        });
        builder.Entity<EducationCorrectionSubmission>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "EducationCorrectionSubmissions", KnowPulseConsts.DbSchema); b.ConfigureByConvention();
            b.HasAlternateKey(x => new { x.Id, x.DeliveryId }).HasName("AK_EduCorrectionSubmissions_Delivery");
            b.HasAlternateKey(x => new { x.Id, x.DeliveryId, x.SubmissionNumber }).HasName("AK_EduCorrectionSubmissions_Sequence");
            b.HasIndex(x => new { x.DeliveryId, x.SubmissionNumber }).IsUnique().HasDatabaseName("IX_EduCorrectionSubmissions_Sequence");
            b.HasOne<EducationCorrectionDelivery>().WithMany().HasForeignKey(x => new { x.DeliveryId, x.StudentUserId, x.OwnerUserId }).HasPrincipalKey(x => new { x.Id, x.StudentUserId, x.OwnerUserId }).HasConstraintName("FK_EduCorrectionSubmissions_Delivery").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationCorrectionSubmission>().WithMany().HasForeignKey(x => new { x.BasedOnSubmissionId, x.DeliveryId }).HasPrincipalKey(x => new { x.Id, x.DeliveryId }).HasConstraintName("FK_EduCorrectionSubmissions_Previous").OnDelete(DeleteBehavior.Restrict);
            b.Property(x => x.ContentJson).IsRequired().HasMaxLength(128 * 1024); b.Property(x => x.PreviewFingerprint).IsRequired().HasMaxLength(64);
            b.Property(x => x.RequestFingerprint).IsRequired().HasMaxLength(64); b.Property(x => x.ExpectedConcurrencyStamp).IsRequired().HasMaxLength(40); b.Property(x => x.SubmittedAtUtc).HasConversion(utc);
            b.ToTable(t => t.HasCheckConstraint("CK_EduCorrectionSubmissions_State", "\"SubmissionNumber\" > 0 AND length(\"PreviewFingerprint\") = 64 AND length(\"RequestFingerprint\") = 64 AND ((\"SubmissionNumber\" = 1 AND \"BasedOnSubmissionId\" IS NULL) OR (\"SubmissionNumber\" > 1 AND \"BasedOnSubmissionId\" IS NOT NULL))"));
        });
        builder.Entity<EducationCorrectionReview>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "EducationCorrectionReviews", KnowPulseConsts.DbSchema); b.ConfigureByConvention();
            b.HasAlternateKey(x => new { x.Id, x.DeliveryId }).HasName("AK_EduCorrectionReviews_Delivery");
            b.HasAlternateKey(x => new { x.Id, x.DeliveryId, x.ReviewNumber }).HasName("AK_EduCorrectionReviews_Sequence");
            b.HasIndex(x => new { x.DeliveryId, x.ReviewNumber }).IsUnique().HasDatabaseName("IX_EduCorrectionReviews_Sequence");
            b.HasOne<EducationCorrectionDelivery>().WithMany().HasForeignKey(x => new { x.DeliveryId, x.StudentUserId, x.OwnerUserId }).HasPrincipalKey(x => new { x.Id, x.StudentUserId, x.OwnerUserId }).HasConstraintName("FK_EduCorrectionReviews_Delivery").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationCorrectionSubmission>().WithMany().HasForeignKey(x => new { x.SubmissionId, x.DeliveryId }).HasPrincipalKey(x => new { x.Id, x.DeliveryId }).HasConstraintName("FK_EduCorrectionReviews_Submission").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationCorrectionReview>().WithMany().HasForeignKey(x => new { x.BasedOnReviewId, x.DeliveryId }).HasPrincipalKey(x => new { x.Id, x.DeliveryId }).HasConstraintName("FK_EduCorrectionReviews_Previous").OnDelete(DeleteBehavior.Restrict);
            b.Property(x => x.StudentFeedback).IsRequired().HasMaxLength(2000); b.Property(x => x.RequestFingerprint).IsRequired().HasMaxLength(64); b.Property(x => x.ExpectedConcurrencyStamp).IsRequired().HasMaxLength(40); b.Property(x => x.PublishedAtUtc).HasConversion(utc);
            b.ToTable(t => t.HasCheckConstraint("CK_EduCorrectionReviews_State", "\"ReviewNumber\" > 0 AND \"Decision\" IN (0,1) AND length(trim(\"StudentFeedback\")) BETWEEN 1 AND 2000 AND length(\"RequestFingerprint\") = 64 AND ((\"ReviewNumber\" = 1 AND \"BasedOnReviewId\" IS NULL) OR (\"ReviewNumber\" > 1 AND \"BasedOnReviewId\" IS NOT NULL))"));
        });
        builder.Entity<EducationCorrectionImage>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "EducationCorrectionImages", KnowPulseConsts.DbSchema); b.ConfigureByConvention();
            b.HasOne<EducationCorrectionDelivery>().WithMany().HasForeignKey(x => new { x.DeliveryId, x.StudentUserId }).HasPrincipalKey(x => new { x.Id, x.StudentUserId }).HasConstraintName("FK_EduCorrectionImages_Student").OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.BlobName).IsUnique().HasDatabaseName("IX_EduCorrectionImages_Blob"); b.HasIndex(x => x.DeliveryId).HasDatabaseName("IX_EduCorrectionImages_Delivery");
            b.Property(x => x.FileName).IsRequired().HasMaxLength(256); b.Property(x => x.ContentType).IsRequired().HasMaxLength(64); b.Property(x => x.BlobName).IsRequired().HasMaxLength(256);
            b.Property(x => x.ContentHash).IsRequired().HasMaxLength(64); b.Property(x => x.RequestFingerprint).IsRequired().HasMaxLength(64); b.Property(x => x.ReservedAtUtc).HasConversion(utc); b.Property(x => x.UploadedAtUtc).HasConversion(nullableUtc);
            b.ToTable(t => t.HasCheckConstraint("CK_EduCorrectionImages_State", "\"Size\" BETWEEN 1 AND 5242880 AND \"ContentType\" IN ('image/png','image/jpeg','image/webp') AND length(\"ContentHash\") = 64 AND length(\"RequestFingerprint\") = 64 AND (\"UploadedAtUtc\" IS NULL OR \"UploadedAtUtc\" >= \"ReservedAtUtc\")"));
        });
    }
}
