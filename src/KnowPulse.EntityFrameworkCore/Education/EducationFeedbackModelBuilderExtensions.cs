using System;
using KnowPulse.Education;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace KnowPulse.EntityFrameworkCore;

public static class EducationFeedbackModelBuilderExtensions
{
    public static void ConfigureEducationFeedback(this ModelBuilder builder)
    {
        var utc = new ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        var nullableUtc = new ValueConverter<DateTime?, DateTime?>(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null);
        builder.Entity<EducationFeedbackPhrase>(b =>
        {
            Table(b, "EducationFeedbackPhrases");
            b.HasAlternateKey(x => new { x.Id, x.OwnerUserId }).HasName("AK_EduPhrases_Owner");
            b.HasIndex(x => new { x.TenantId, x.OwnerUserId, x.IsArchived }).HasDatabaseName("IX_EduPhrases_Owner");
            b.HasOne<EducationFeedbackPhraseRevision>().WithMany().HasForeignKey(x => new { x.CurrentRevisionId, x.Id, x.OwnerUserId })
                .HasPrincipalKey(x => new { x.Id, x.PhraseId, x.OwnerUserId }).HasConstraintName("FK_EduPhrases_Current").OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_EduPhrases_Revision", "(\"RevisionNumber\" = 0 AND \"CurrentRevisionId\" IS NULL AND NOT \"IsArchived\") OR (\"RevisionNumber\" > 0 AND \"CurrentRevisionId\" IS NOT NULL)"));
        });
        builder.Entity<EducationFeedbackPhraseRevision>(b =>
        {
            Table(b, "EducationFeedbackPhraseRevisions");
            b.Property(x => x.Text).IsRequired().HasMaxLength(4000); b.Property(x => x.RequestFingerprint).IsRequired().HasMaxLength(64);
            b.Property(x => x.RecordedAtUtc).HasConversion(utc);
            b.HasAlternateKey(x => new { x.Id, x.PhraseId, x.OwnerUserId }).HasName("AK_EduPhraseVersions_Owner");
            b.HasIndex(x => new { x.PhraseId, x.RevisionNumber }).IsUnique().HasDatabaseName("IX_EduPhraseVersions_Sequence");
            b.HasOne<EducationFeedbackPhrase>().WithMany().HasForeignKey(x => new { x.PhraseId, x.OwnerUserId })
                .HasPrincipalKey(x => new { x.Id, x.OwnerUserId }).HasConstraintName("FK_EduPhraseVersions_Root").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationFeedbackPhraseRevision>().WithMany().HasForeignKey(x => new { x.BasedOnRevisionId, x.PhraseId, x.OwnerUserId })
                .HasPrincipalKey(x => new { x.Id, x.PhraseId, x.OwnerUserId }).HasConstraintName("FK_EduPhraseVersions_Previous").OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_EduPhraseVersions_Revision", "\"RevisionNumber\" > 0"));
        });
        builder.Entity<EducationTeacherObservation>(b =>
        {
            Table(b, "EducationTeacherObservations"); Scope(b, "EduObservations");
            b.Property(x => x.Text).IsRequired().HasMaxLength(4000); b.Property(x => x.RequestFingerprint).IsRequired().HasMaxLength(64);
            b.Property(x => x.ObservedAtUtc).HasConversion(utc);
            b.HasIndex(x => new { x.TenantId, x.OwnerUserId, x.TeachingAssignmentId, x.StudentEnrollmentId, x.ObservedAtUtc }).HasDatabaseName("IX_EduObservations_OwnerStudent");
        });
        builder.Entity<EducationStudentFeedback>(b =>
        {
            Table(b, "EducationStudentFeedback"); Scope(b, "EduFeedback");
            b.HasAlternateKey(x => new { x.Id, x.StudentUserId, x.OwnerUserId }).HasName("AK_EduFeedback_StudentOwner");
            b.Property(x => x.TeacherName).IsRequired().HasMaxLength(256); b.Property(x => x.StudentName).IsRequired().HasMaxLength(256);
            b.Property(x => x.AccountName).IsRequired().HasMaxLength(256); b.Property(x => x.ClassName).IsRequired().HasMaxLength(128);
            b.Property(x => x.SubjectName).IsRequired().HasMaxLength(128);
            b.HasOne<EducationSubject>().WithMany().HasForeignKey(x => x.EducationSubjectId).HasConstraintName("FK_EduFeedback_Subject").OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.OwnerUserId, x.TeachingAssignmentId }).HasDatabaseName("IX_EduFeedback_Teacher");
            b.HasIndex(x => new { x.TenantId, x.StudentUserId, x.PublishedRevisionId }).HasDatabaseName("IX_EduFeedback_Student");
            foreach (var pointer in new[] { "CurrentRevisionId", "CurrentDraftRevisionId", "PublishedRevisionId", "LastPublishedRevisionId" })
                b.HasOne<EducationStudentFeedbackRevision>().WithMany().HasForeignKey(pointer, "Id", "StudentUserId")
                    .HasPrincipalKey(x => new { x.Id, x.FeedbackId, x.StudentUserId }).HasConstraintName("FK_EduFeedback_" + pointer).OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_EduFeedback_Revision", "(\"RevisionNumber\" = 0 AND \"CurrentRevisionId\" IS NULL) OR (\"RevisionNumber\" > 0 AND \"CurrentRevisionId\" IS NOT NULL)"));
        });
        builder.Entity<EducationStudentFeedbackRevision>(b =>
        {
            Table(b, "EducationStudentFeedbackRevisions"); b.Ignore(x => x.Content);
            b.Property(x => x.ContentJson).HasMaxLength(32000); b.Property(x => x.Reason).HasMaxLength(2048);
            b.Property(x => x.RequestFingerprint).IsRequired().HasMaxLength(64); b.Property(x => x.ExpectedConcurrencyStamp).IsRequired().HasMaxLength(40);
            b.Property(x => x.RecordedAtUtc).HasConversion(utc);
            b.HasAlternateKey(x => new { x.Id, x.FeedbackId, x.StudentUserId }).HasName("AK_EduFeedbackVersions_FeedbackStudent");
            b.HasIndex(x => new { x.FeedbackId, x.RevisionNumber }).IsUnique().HasDatabaseName("IX_EduFeedbackVersions_Sequence");
            b.HasOne<EducationStudentFeedback>().WithMany().HasForeignKey(x => new { x.FeedbackId, x.StudentUserId, x.OwnerUserId })
                .HasPrincipalKey(x => new { x.Id, x.StudentUserId, x.OwnerUserId }).HasConstraintName("FK_EduFeedbackVersions_Root").OnDelete(DeleteBehavior.Restrict);
            foreach (var pointer in new[] { "BasedOnRevisionId", "DraftRevisionId", "PreviousPublishedRevisionId" })
                b.HasOne<EducationStudentFeedbackRevision>().WithMany().HasForeignKey(pointer, "FeedbackId", "StudentUserId")
                    .HasPrincipalKey(x => new { x.Id, x.FeedbackId, x.StudentUserId }).HasConstraintName("FK_EduFeedbackVersions_" + pointer).OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_EduFeedbackVersions_Kind", "\"RevisionNumber\" > 0 AND " +
                "((\"Kind\" = 0 AND \"ContentJson\" IS NOT NULL AND \"DraftRevisionId\" IS NULL) OR " +
                "(\"Kind\" = 1 AND \"ContentJson\" IS NOT NULL AND \"DraftRevisionId\" IS NOT NULL) OR " +
                "(\"Kind\" = 2 AND \"ContentJson\" IS NULL AND \"DraftRevisionId\" IS NULL AND \"PreviousPublishedRevisionId\" IS NOT NULL AND \"Reason\" IS NOT NULL))"));
        });
        builder.Entity<EducationFeedbackAssessment>(b =>
        {
            Table(b, "EducationFeedbackAssessments");
            b.HasIndex(x => new { x.FeedbackRevisionId, x.TaskId }).IsUnique().HasDatabaseName("IX_EduFeedbackAssessments_Unique");
            b.HasOne<EducationStudentFeedbackRevision>().WithMany().HasForeignKey(x => new { x.FeedbackRevisionId, x.FeedbackId, x.StudentUserId })
                .HasPrincipalKey(x => new { x.Id, x.FeedbackId, x.StudentUserId }).HasConstraintName("FK_EduFeedbackAssessments_Version").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentRecipient>().WithMany().HasForeignKey(x => new { x.RecipientId, x.TaskId, x.TaskRevisionId, x.StudentUserId })
                .HasPrincipalKey(x => new { x.Id, x.TaskId, x.TaskRevisionId, x.StudentUserId }).HasConstraintName("FK_EduFeedbackAssessments_Recipient").OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<EducationFeedbackRead>(b =>
        {
            Table(b, "EducationFeedbackReads"); b.Property(x => x.OpenedAtUtc).HasConversion(utc);
            b.HasOne<EducationStudentFeedbackRevision>().WithMany().HasForeignKey(x => new { x.Id, x.FeedbackId, x.StudentUserId })
                .HasPrincipalKey(x => new { x.Id, x.FeedbackId, x.StudentUserId }).HasConstraintName("FK_EduFeedbackReads_Version").OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<EducationFeedbackNotice>(b =>
        {
            Table(b, "EducationFeedbackNotices"); b.Property(x => x.RecordedAtUtc).HasConversion(utc);
            b.Property(x => x.MessageReadAtUtc).HasConversion(nullableUtc);
            b.ToTable(t => t.HasCheckConstraint("CK_EduFeedbackNotices_MessageReadTime", "\"MessageReadAtUtc\" IS NULL OR \"MessageReadAtUtc\" >= \"RecordedAtUtc\""));
            b.HasIndex(x => new { x.TenantId, x.StudentUserId, x.RecordedAtUtc }).HasDatabaseName("IX_EduFeedbackNotices_Inbox");
            b.HasOne<EducationStudentFeedbackRevision>().WithMany().HasForeignKey(x => new { x.Id, x.FeedbackId, x.StudentUserId })
                .HasPrincipalKey(x => new { x.Id, x.FeedbackId, x.StudentUserId }).HasConstraintName("FK_EduFeedbackNotices_Version").OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_EduFeedbackNotices_Kind", "\"Kind\" IN (1, 2)"));
        });
    }

    private static void Table<T>(EntityTypeBuilder<T> builder, string name) where T : class
    { builder.ToTable(KnowPulseConsts.DbTablePrefix + name, KnowPulseConsts.DbSchema); builder.ConfigureByConvention(); }
    private static void Scope<T>(EntityTypeBuilder<T> b, string prefix) where T : class
    {
        b.HasOne<TeachingAssignment>().WithMany().HasForeignKey("TeachingAssignmentId", "SchoolId", "AcademicTermId", "SchoolClassId", "OwnerUserId")
            .HasPrincipalKey(x => new { x.Id, x.SchoolId, x.AcademicTermId, x.SchoolClassId, x.TeacherUserId }).HasConstraintName("FK_" + prefix + "_Teaching").OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StudentEnrollment>().WithMany().HasForeignKey("StudentEnrollmentId", "SchoolId", "SchoolClassId", "StudentUserId")
            .HasPrincipalKey(x => new { x.Id, x.SchoolId, x.SchoolClassId, x.StudentUserId }).HasConstraintName("FK_" + prefix + "_Enrollment").OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SchoolMembership>().WithMany().HasForeignKey("SchoolMembershipId", "SchoolId", "StudentUserId")
            .HasPrincipalKey(x => new { x.Id, x.SchoolId, x.UserId }).HasConstraintName("FK_" + prefix + "_Membership").OnDelete(DeleteBehavior.Restrict);
    }
}
