using System;
using KnowPulse.Education;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace KnowPulse.EntityFrameworkCore;

public static class EducationAssessmentReminderModelBuilderExtensions
{
    public static void ConfigureEducationAssessmentReminders(this ModelBuilder builder)
    {
        var utc = new ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        var nullableUtc = new ValueConverter<DateTime?, DateTime?>(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null);
        builder.Entity<EducationAssessmentTaskRevision>().HasAlternateKey(x => new { x.Id, x.TaskId, x.SchoolId, x.OwnerUserId }).HasName("AK_EduAssessmentRevision_ReminderOwner");
        builder.Entity<EducationAssessmentResultRelease>().HasAlternateKey(x => new { x.Id, x.TaskId, x.TaskRevisionId }).HasName("AK_EduResultRelease_NoticePublication");
        builder.Entity<EducationAssessmentReminder>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "EducationAssessmentReminders", KnowPulseConsts.DbSchema, table => {
                table.HasCheckConstraint("CK_EduReminders_ReadTime", "\"ReadAtUtc\" IS NULL OR \"ReadAtUtc\" >= \"AvailableAtUtc\"");
                table.HasCheckConstraint("CK_EduReminders_AvailableTime", "\"AvailableAtUtc\" >= \"RecordedAtUtc\"");
                table.HasCheckConstraint("CK_EduReminders_Kind", "(\"Kind\" IN (0, 1) AND \"ResultReleaseId\" IS NULL) OR (\"Kind\" = 2 AND \"ResultReleaseId\" IS NOT NULL)");
                table.HasCheckConstraint("CK_EduReminders_Fingerprint", "length(\"RequestFingerprint\") = 64");
            });
            b.ConfigureByConvention();
            b.Property(x => x.RequestFingerprint).IsRequired().HasMaxLength(64);
            b.Property(x => x.RecordedAtUtc).HasConversion(utc); b.Property(x => x.AvailableAtUtc).HasConversion(utc); b.Property(x => x.ReadAtUtc).HasConversion(nullableUtc);
            b.HasIndex(x => new { x.TenantId, x.StudentUserId, x.SchoolId, x.RecordedAtUtc });
            b.HasIndex(x => new { x.RecipientId, x.RecordedAtUtc });
            b.HasIndex(x => x.RecipientId).IsUnique().HasFilter("\"Kind\" = 1");
            b.HasIndex(x => new { x.ResultReleaseId, x.RecipientId }).IsUnique().HasFilter("\"Kind\" = 2");
            b.HasOne<EducationAssessmentResultRelease>().WithMany().HasForeignKey(x => new { x.ResultReleaseId, x.TaskId, x.TaskRevisionId })
                .HasPrincipalKey(x => new { x.Id, x.TaskId, x.TaskRevisionId }).HasConstraintName("FK_EduReminders_ResultPublication").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentRecipient>().WithMany().HasForeignKey(x => new { x.RecipientId, x.TaskId, x.TaskRevisionId, x.StudentUserId })
                .HasPrincipalKey(x => new { x.Id, x.TaskId, x.TaskRevisionId, x.StudentUserId }).HasConstraintName("FK_EduReminders_Recipient").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentTaskRevision>().WithMany().HasForeignKey(x => new { x.TaskRevisionId, x.TaskId, x.SchoolId, OwnerUserId = x.TeacherUserId })
                .HasPrincipalKey(x => new { x.Id, x.TaskId, x.SchoolId, x.OwnerUserId }).HasConstraintName("FK_EduReminders_OwnerRevision").OnDelete(DeleteBehavior.Restrict);
        });
    }
}
