using System;
using KnowPulse.Education;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Volo.Abp.Identity;

namespace KnowPulse.EntityFrameworkCore;

public static class EducationStudyTaskModelBuilderExtensions
{
    public static void ConfigureEducationStudyTasks(this ModelBuilder builder)
    {
        builder.Entity<EducationStudyTask>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "EducationStudyTasks", KnowPulseConsts.DbSchema); b.ConfigureByConvention();
            b.HasAlternateKey(x => new { x.Id, x.OwnerUserId, x.SchoolId, x.AcademicTermId, x.SchoolClassId }).HasName("AK_EduStudyTasks_Authority");
            b.HasIndex(x => new { x.TenantId, x.OwnerUserId, x.CreationTime }).HasDatabaseName("IX_EduStudyTasks_Owner");
            b.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<TeachingAssignment>().WithMany().HasForeignKey(x => new { x.TeachingAssignmentId, x.SchoolId, x.AcademicTermId, x.SchoolClassId, TeacherUserId = x.OwnerUserId, x.EducationSubjectId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId, x.AcademicTermId, x.SchoolClassId, x.TeacherUserId, x.EducationSubjectId }).HasConstraintName("FK_EduStudyTasks_Teaching").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<TextbookVersion>().WithMany().HasForeignKey(x => new { x.TextbookVersionId, x.SchoolId, x.EducationSubjectId }).HasPrincipalKey(x => new { x.Id, x.SchoolId, x.EducationSubjectId })
                .HasConstraintName("FK_EduStudyTasks_Textbook").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationStudyTaskRevision>().WithMany().HasForeignKey(x => new { x.CurrentRevisionId, TaskId = x.Id, x.RevisionNumber })
                .HasPrincipalKey(x => new { x.Id, x.TaskId, x.RevisionNumber }).HasConstraintName("FK_EduStudyTasks_Current").OnDelete(DeleteBehavior.Restrict);
            foreach (var pointer in new[] { "PublishedRevisionId", "LastPublishedRevisionId" })
                b.HasOne<EducationStudyTaskRevision>().WithMany().HasForeignKey(pointer, "Id", "PublicationKind").HasPrincipalKey(x => new { x.Id, x.TaskId, x.Kind })
                    .HasConstraintName("FK_EduStudyTasks_" + pointer).OnDelete(DeleteBehavior.Restrict);
            b.Property(x => x.PublicationKind).HasDefaultValue(EducationStudyTaskRevisionKind.Published);
            b.ToTable(t => t.HasCheckConstraint("CK_EduStudyTasks_State", "\"PublicationKind\" = 1 AND ((\"RevisionNumber\" = 0 AND \"CurrentRevisionId\" IS NULL) OR (\"RevisionNumber\" > 0 AND \"CurrentRevisionId\" IS NOT NULL)) AND (\"PublishedRevisionId\" IS NULL OR \"PublishedRevisionId\" = \"LastPublishedRevisionId\")"));
        });
        builder.Entity<EducationStudyTaskRevision>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "EducationStudyTaskRevisions", KnowPulseConsts.DbSchema); b.ConfigureByConvention();
            b.HasAlternateKey(x => new { x.Id, x.TaskId }).HasName("AK_EduStudyVersions_Root");
            b.HasAlternateKey(x => new { x.Id, x.TaskId, x.RevisionNumber }).HasName("AK_EduStudyVersions_Sequence");
            b.HasAlternateKey(x => new { x.Id, x.TaskId, x.Kind }).HasName("AK_EduStudyVersions_Kind");
            b.HasAlternateKey(x => new { x.Id, x.TaskId, x.OwnerUserId, x.SchoolId, x.AcademicTermId, x.SchoolClassId, x.Kind }).HasName("AK_EduStudyVersions_Recipient");
            b.HasIndex(x => new { x.TaskId, x.RevisionNumber }).IsUnique().HasDatabaseName("IX_EduStudyVersions_Sequence");
            b.Property(x => x.Title).IsRequired().HasMaxLength(256); b.Property(x => x.TeacherName).IsRequired().HasMaxLength(256); b.Property(x => x.ClassName).IsRequired().HasMaxLength(128);
            b.Property(x => x.ScopeJson).IsRequired().HasMaxLength(EducationLearningReportJson.MaxSourceBytes); b.Property(x => x.ContentJson).HasMaxLength(256 * 1024);
            b.Property(x => x.PublicReason).HasMaxLength(2000); b.Property(x => x.RosterFingerprint).HasMaxLength(64);
            b.Property(x => x.RequestFingerprint).IsRequired().HasMaxLength(64); b.Property(x => x.ExpectedConcurrencyStamp).IsRequired().HasMaxLength(40);
            foreach (var name in new[] { "StartsAtUtc", "EndsAtUtc", "RecordedAtUtc" }) Utc(b, name);
            b.HasOne<EducationStudyTask>().WithMany().HasForeignKey(x => new { x.TaskId, x.OwnerUserId, x.SchoolId, x.AcademicTermId, x.SchoolClassId })
                .HasPrincipalKey(x => new { x.Id, x.OwnerUserId, x.SchoolId, x.AcademicTermId, x.SchoolClassId }).HasConstraintName("FK_EduStudyVersions_Root").OnDelete(DeleteBehavior.Restrict);
            foreach (var pointer in new[] { "BasedOnRevisionId", "SourceRevisionId" }) b.HasOne<EducationStudyTaskRevision>().WithMany()
                .HasForeignKey(pointer, "TaskId").HasPrincipalKey(x => new { x.Id, x.TaskId }).HasConstraintName("FK_EduStudyVersions_" + pointer).OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_EduStudyVersions_Content", "\"RevisionNumber\" > 0 AND \"Kind\" IN (0,1,2) AND \"StartsAtUtc\" < \"EndsAtUtc\" AND length(\"RequestFingerprint\") = 64 AND " +
                "((\"Kind\" = 0 AND \"ContentJson\" IS NOT NULL AND \"SourceRevisionId\" IS NULL AND \"RosterFingerprint\" IS NULL) OR " +
                "(\"Kind\" = 1 AND \"ContentJson\" IS NOT NULL AND \"SourceRevisionId\" IS NOT NULL AND \"RosterFingerprint\" IS NOT NULL AND length(\"RosterFingerprint\") = 64) OR " +
                "(\"Kind\" = 2 AND \"ContentJson\" IS NULL AND \"SourceRevisionId\" IS NOT NULL AND \"RosterFingerprint\" IS NULL AND \"PublicReason\" IS NOT NULL AND length(trim(\"PublicReason\")) BETWEEN 1 AND 2000))"));
        });
        builder.Entity<EducationStudyRecipient>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "EducationStudyRecipients", KnowPulseConsts.DbSchema); b.ConfigureByConvention();
            b.HasAlternateKey(x => new { x.Id, x.StudentUserId, x.OwnerUserId }).HasName("AK_EduStudyRecipients_Actors");
            b.HasIndex(x => new { x.TaskRevisionId, x.StudentUserId }).IsUnique().HasDatabaseName("IX_EduStudyRecipients_Assigned");
            b.HasIndex(x => new { x.TenantId, x.StudentUserId, x.AssignedAtUtc }).HasDatabaseName("IX_EduStudyRecipients_Student");
            b.Property(x => x.StudentName).IsRequired().HasMaxLength(256); Utc(b, "AssignedAtUtc");
            b.Property(x => x.FirstProvidedAtUtc).HasConversion(new ValueConverter<DateTime?, DateTime?>(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null));
            b.HasOne<EducationStudyTaskRevision>().WithMany().HasForeignKey(x => new { x.TaskRevisionId, x.TaskId, x.OwnerUserId, x.SchoolId, x.AcademicTermId, x.SchoolClassId, Kind = x.PublicationKind })
                .HasPrincipalKey(x => new { x.Id, x.TaskId, x.OwnerUserId, x.SchoolId, x.AcademicTermId, x.SchoolClassId, x.Kind }).HasConstraintName("FK_EduStudyRecipients_Publication").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<SchoolMembership>().WithMany().HasForeignKey(x => new { x.SchoolMembershipId, x.SchoolId, UserId = x.StudentUserId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId, x.UserId }).HasConstraintName("FK_EduStudyRecipients_Membership").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<StudentEnrollment>().WithMany().HasForeignKey(x => new { x.StudentEnrollmentId, x.SchoolId, x.SchoolClassId, x.StudentUserId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId, x.SchoolClassId, x.StudentUserId }).HasConstraintName("FK_EduStudyRecipients_Enrollment").OnDelete(DeleteBehavior.Restrict);
            foreach (var pointer in new[] { "CurrentRevisionId", "CurrentStudentRevisionId", "CurrentFollowUpRevisionId" }) b.HasOne<EducationStudyEvent>().WithMany()
                .HasForeignKey(pointer, "Id").HasPrincipalKey(x => new { x.Id, x.RecipientId }).HasConstraintName("FK_EduStudyRecipients_" + pointer).OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_EduStudyRecipients_State", "\"PublicationKind\" = 1 AND ((\"RevisionNumber\" = 0 AND \"CurrentRevisionId\" IS NULL) OR (\"RevisionNumber\" > 0 AND \"CurrentRevisionId\" IS NOT NULL)) AND " +
                "((\"StudentRevisionNumber\" = 0 AND \"CurrentStudentRevisionId\" IS NULL) OR (\"StudentRevisionNumber\" > 0 AND \"CurrentStudentRevisionId\" IS NOT NULL)) AND \"StudentRevisionNumber\" <= \"RevisionNumber\" AND (\"FirstProvidedAtUtc\" IS NULL OR \"FirstProvidedAtUtc\" >= \"AssignedAtUtc\")"));
        });
        builder.Entity<EducationStudyEvent>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "EducationStudyEvents", KnowPulseConsts.DbSchema); b.ConfigureByConvention(); Utc(b, "RecordedAtUtc");
            b.HasAlternateKey(x => new { x.Id, x.RecipientId }).HasName("AK_EduStudyEvents_Recipient");
            b.HasIndex(x => new { x.RecipientId, x.RevisionNumber }).IsUnique().HasDatabaseName("IX_EduStudyEvents_Sequence");
            b.HasIndex(x => new { x.RecipientId, x.StudentRevisionNumber }).IsUnique().HasDatabaseName("IX_EduStudyEvents_StudentSequence");
            b.Property(x => x.SharedText).HasMaxLength(2000); b.Property(x => x.InternalTeacherNote).HasMaxLength(2000);
            b.Property(x => x.RequestFingerprint).IsRequired().HasMaxLength(64); b.Property(x => x.ExpectedConcurrencyStamp).IsRequired().HasMaxLength(40);
            b.HasOne<EducationStudyRecipient>().WithMany().HasForeignKey(x => new { x.RecipientId, x.StudentUserId, x.OwnerUserId })
                .HasPrincipalKey(x => new { x.Id, x.StudentUserId, x.OwnerUserId }).HasConstraintName("FK_EduStudyEvents_Recipient").OnDelete(DeleteBehavior.Restrict);
            foreach (var pointer in new[] { "BasedOnRevisionId", "ReplyToStudentRevisionId" }) b.HasOne<EducationStudyEvent>().WithMany()
                .HasForeignKey(pointer, "RecipientId").HasPrincipalKey(x => new { x.Id, x.RecipientId }).HasConstraintName("FK_EduStudyEvents_" + pointer).OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_EduStudyEvents_Action", "\"RevisionNumber\" > 0 AND length(\"RequestFingerprint\") = 64 AND " +
                "((\"Action\" = 3 AND \"ActorUserId\" = \"OwnerUserId\" AND \"StudentRevisionNumber\" IS NULL AND \"ReplyToStudentRevisionId\" IS NOT NULL AND \"InternalTeacherNote\" IS NOT NULL AND length(trim(\"InternalTeacherNote\")) BETWEEN 1 AND 2000 AND \"SharedText\" IS NULL AND \"Difficulty\" IS NULL AND \"ReportedMinutes\" IS NULL) OR " +
                "(\"Action\" IN (0,1,2) AND \"ActorUserId\" = \"StudentUserId\" AND \"StudentRevisionNumber\" IS NOT NULL AND \"StudentRevisionNumber\" > 0 AND \"ReplyToStudentRevisionId\" IS NULL AND \"InternalTeacherNote\" IS NULL AND (\"SharedText\" IS NULL OR length(\"SharedText\") <= 2000) AND " +
                "((\"Action\" = 2 AND \"Difficulty\" IS NOT NULL AND \"Difficulty\" IN (0,1,2,3) AND \"SharedText\" IS NOT NULL AND length(trim(\"SharedText\")) > 0) OR (\"Action\" IN (0,1) AND \"Difficulty\" IS NULL)) AND " +
                "((\"Action\" = 1 AND (\"ReportedMinutes\" IS NULL OR \"ReportedMinutes\" BETWEEN 1 AND 1440)) OR (\"Action\" IN (0,2) AND \"ReportedMinutes\" IS NULL))))"));
        });
    }
    private static void Utc<T>(EntityTypeBuilder<T> b, string name) where T : class => b.Property<DateTime>(name)
        .HasConversion(new ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc)));
}
