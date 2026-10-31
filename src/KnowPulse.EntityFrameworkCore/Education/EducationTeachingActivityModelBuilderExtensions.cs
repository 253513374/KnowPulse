using System;
using KnowPulse.Education;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace KnowPulse.EntityFrameworkCore;

public static class EducationTeachingActivityModelBuilderExtensions
{
    public static void ConfigureEducationTeachingActivities(this ModelBuilder builder)
    {
        builder.Entity<EducationTeachingActivity>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "EducationTeachingActivities", KnowPulseConsts.DbSchema); b.ConfigureByConvention();
            b.HasAlternateKey(x => new { x.Id, x.StudentUserId, x.OwnerUserId, x.StepKind }).HasName("AK_EduActivities_Recipient");
            b.HasIndex(x => new { x.PlanRevisionId, x.StepId, x.StudentUserId }).IsUnique().HasDatabaseName("IX_EduActivities_AssignedStep");
            b.HasIndex(x => new { x.TenantId, x.StudentUserId, x.PlanId }).HasDatabaseName("IX_EduActivities_Student");
            foreach (var name in new[] { "StartsAtUtc", "DueAtUtc", "AssignedAtUtc" }) Utc(b, name);
            b.Property(x => x.FirstProvidedAtUtc).HasConversion(new ValueConverter<DateTime?, DateTime?>(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null));
            b.HasOne<EducationTeachingPlanRevision>().WithMany().HasForeignKey(x => new { x.PlanRevisionId, x.PlanId, x.StudentUserId, x.OwnerUserId })
                .HasPrincipalKey(x => new { x.Id, x.PlanId, x.StudentUserId, x.OwnerUserId }).HasConstraintName("FK_EduActivities_Publication").OnDelete(DeleteBehavior.Restrict);
            foreach (var pointer in new[] { "CurrentRevisionId", "CurrentStudentRevisionId", "CurrentFollowUpRevisionId" })
                b.HasOne<EducationTeachingActivityRevision>().WithMany().HasForeignKey(pointer, "Id").HasPrincipalKey(x => new { x.Id, x.ActivityId })
                    .HasConstraintName("FK_EduActivities_" + pointer).OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_EduActivities_State",
                "((\"RevisionNumber\" = 0 AND \"CurrentRevisionId\" IS NULL) OR (\"RevisionNumber\" > 0 AND \"CurrentRevisionId\" IS NOT NULL)) AND " +
                "((\"StudentRevisionNumber\" = 0 AND \"CurrentStudentRevisionId\" IS NULL) OR (\"StudentRevisionNumber\" > 0 AND \"CurrentStudentRevisionId\" IS NOT NULL)) AND " +
                "\"StudentRevisionNumber\" <= \"RevisionNumber\" AND \"StepKind\" IN (0,1,2,3,4,5) AND \"StepOrder\" BETWEEN 1 AND 50 AND \"EstimatedMinutes\" BETWEEN 1 AND 1440 AND " +
                "\"StartsAtUtc\" <= \"DueAtUtc\" AND (\"FirstProvidedAtUtc\" IS NULL OR \"FirstProvidedAtUtc\" >= \"AssignedAtUtc\")"));
        });
        builder.Entity<EducationTeachingActivityRevision>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "EducationTeachingActivityRevisions", KnowPulseConsts.DbSchema); b.ConfigureByConvention(); Utc(b, "RecordedAtUtc");
            b.HasAlternateKey(x => new { x.Id, x.ActivityId }).HasName("AK_EduActivityVersions_Root");
            b.HasIndex(x => new { x.ActivityId, x.RevisionNumber }).IsUnique().HasDatabaseName("IX_EduActivityVersions_Sequence");
            b.HasIndex(x => new { x.ActivityId, x.StudentRevisionNumber }).IsUnique().HasDatabaseName("IX_EduActivityVersions_StudentSequence");
            b.Property(x => x.SharedText).HasMaxLength(2000); b.Property(x => x.InternalTeacherNote).HasMaxLength(2000);
            b.Property(x => x.ExpectedConcurrencyStamp).IsRequired().HasMaxLength(40); b.Property(x => x.RequestFingerprint).IsRequired().HasMaxLength(64);
            b.HasOne<EducationTeachingActivity>().WithMany().HasForeignKey(x => new { x.ActivityId, x.StudentUserId, x.OwnerUserId, x.StepKind })
                .HasPrincipalKey(x => new { x.Id, x.StudentUserId, x.OwnerUserId, x.StepKind }).HasConstraintName("FK_EduActivityVersions_Recipient").OnDelete(DeleteBehavior.Restrict);
            foreach (var pointer in new[] { "BasedOnRevisionId", "ReplyToStudentRevisionId" })
                b.HasOne<EducationTeachingActivityRevision>().WithMany().HasForeignKey(pointer, "ActivityId").HasPrincipalKey(x => new { x.Id, x.ActivityId })
                    .HasConstraintName("FK_EduActivityVersions_" + pointer).OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_EduActivityVersions_Action",
                "\"RevisionNumber\" > 0 AND \"Action\" IN (0,1,2,3) AND " +
                "((\"Action\" = 3 AND \"ActorUserId\" = \"OwnerUserId\" AND \"StudentRevisionNumber\" IS NULL AND \"ReplyToStudentRevisionId\" IS NOT NULL AND " +
                "\"InternalTeacherNote\" IS NOT NULL AND length(trim(\"InternalTeacherNote\")) BETWEEN 1 AND 2000 AND \"SharedText\" IS NULL AND \"Difficulty\" IS NULL AND \"ReportedMinutes\" IS NULL) OR " +
                "(\"Action\" IN (0,1,2) AND \"ActorUserId\" = \"StudentUserId\" AND \"StudentRevisionNumber\" IS NOT NULL AND \"StudentRevisionNumber\" > 0 AND \"InternalTeacherNote\" IS NULL AND \"ReplyToStudentRevisionId\" IS NULL AND " +
                "(\"SharedText\" IS NULL OR length(\"SharedText\") <= 2000) AND " +
                "((\"Action\" = 2 AND \"Difficulty\" IS NOT NULL AND \"Difficulty\" IN (0,1,2,3) AND \"SharedText\" IS NOT NULL AND length(trim(\"SharedText\")) > 0) OR (\"Action\" IN (0,1) AND \"Difficulty\" IS NULL)) AND " +
                "((\"Action\" = 1 AND \"StepKind\" NOT IN (0,4) AND (\"ReportedMinutes\" IS NULL OR \"ReportedMinutes\" BETWEEN 1 AND 1440)) OR (\"Action\" IN (0,2) AND \"ReportedMinutes\" IS NULL))))"));
        });
    }
    private static void Utc<T>(EntityTypeBuilder<T> b, string name) where T : class =>
        b.Property<DateTime>(name).HasConversion(new ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc)));
}
