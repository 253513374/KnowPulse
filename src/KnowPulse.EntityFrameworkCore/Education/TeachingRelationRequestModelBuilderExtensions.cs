using System;
using KnowPulse.Education;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Volo.Abp.Identity;

namespace KnowPulse.EntityFrameworkCore;

public static class TeachingRelationRequestModelBuilderExtensions
{
    public static void ConfigureTeachingRelationRequests(this ModelBuilder builder)
    {
        builder.Entity<TeachingRelationRequest>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "TeachingRelationRequests", KnowPulseConsts.DbSchema, t => {
                t.HasCheckConstraint("CK_RelationRequests_Original", "(\"OriginalAssignmentId\" IS NULL AND \"OriginalAssignmentStamp\" IS NULL AND \"OriginalEndsAtUtc\" IS NULL) OR (\"OriginalAssignmentId\" IS NOT NULL AND \"OriginalAssignmentStamp\" IS NOT NULL)");
                t.HasCheckConstraint("CK_RelationRequests_Fingerprints", "length(\"RequestFingerprint\") = 64 AND (\"CompletionFingerprint\" IS NULL OR length(\"CompletionFingerprint\") = 64)");
                t.HasCheckConstraint("CK_RelationRequests_Completion", "(\"Status\" = 0 AND \"CompletedByUserId\" IS NULL AND \"CompletionOperationId\" IS NULL AND \"CompletionFingerprint\" IS NULL AND \"CompletedAtUtc\" IS NULL AND \"Resolution\" IS NULL AND \"ResolutionText\" IS NULL AND \"ResultingAssignmentId\" IS NULL AND \"EndedAtUtc\" IS NULL) OR " +
                    "(\"CompletedByUserId\" IS NOT NULL AND \"CompletionOperationId\" IS NOT NULL AND \"CompletionFingerprint\" IS NOT NULL AND \"CompletedAtUtc\" IS NOT NULL AND \"CompletedAtUtc\" >= \"RecordedAtUtc\" AND (" +
                    "(\"Status\" = 2 AND \"CompletedByUserId\" = \"TeacherUserId\" AND \"Resolution\" IS NULL AND \"ResolutionText\" IS NULL AND \"ResultingAssignmentId\" IS NULL AND \"EndedAtUtc\" IS NULL) OR " +
                    "(\"Status\" = 1 AND \"Resolution\" IS NOT NULL AND \"ResolutionText\" IS NOT NULL AND length(\"ResolutionText\") > 0 AND (" +
                    "(\"Resolution\" = 0 AND \"ResultingAssignmentId\" IS NULL AND \"EndedAtUtc\" IS NULL) OR " +
                    "(\"Resolution\" = 1 AND \"OriginalAssignmentId\" IS NOT NULL AND \"ResultingAssignmentId\" IS NULL AND \"EndedAtUtc\" IS NOT NULL) OR " +
                    "(\"Resolution\" = 2 AND \"OriginalAssignmentId\" IS NULL AND \"ResultingAssignmentId\" IS NOT NULL AND \"EndedAtUtc\" IS NULL) OR " +
                    "(\"Resolution\" = 3 AND \"OriginalAssignmentId\" IS NOT NULL AND \"ResultingAssignmentId\" IS NOT NULL AND \"OriginalAssignmentId\" <> \"ResultingAssignmentId\" AND \"EndedAtUtc\" IS NOT NULL)))) )");
                t.HasCheckConstraint("CK_RelationRequests_EndTime", "\"EndedAtUtc\" IS NULL OR (\"CompletedAtUtc\" IS NOT NULL AND \"EndedAtUtc\" >= \"CompletedAtUtc\")");
            });
            b.ConfigureByConvention(); var utc = new ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            var nullableUtc = new ValueConverter<DateTime?, DateTime?>(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null);
            b.Property(x => x.RecordedAtUtc).HasConversion(utc); b.Property(x => x.OriginalEndsAtUtc).HasConversion(nullableUtc); b.Property(x => x.EndedAtUtc).HasConversion(nullableUtc); b.Property(x => x.CompletedAtUtc).HasConversion(nullableUtc);
            b.Property(x => x.RequestText).IsRequired().HasMaxLength(2000); b.Property(x => x.ResolutionText).HasMaxLength(2000);
            b.Property(x => x.OriginalAssignmentStamp).HasMaxLength(40); b.Property(x => x.RequestFingerprint).IsRequired().HasMaxLength(64); b.Property(x => x.CompletionFingerprint).HasMaxLength(64);
            b.HasIndex(x => new { x.TenantId, x.TeacherUserId, x.SchoolId, x.RecordedAtUtc }).HasDatabaseName("IX_RelationRequests_Teacher");
            b.HasIndex(x => new { x.SchoolId, x.RecordedAtUtc }).HasDatabaseName("IX_RelationRequests_School");
            b.HasIndex(x => x.CompletionOperationId).IsUnique().HasFilter("\"CompletionOperationId\" IS NOT NULL");
            b.HasOne<SchoolMembership>().WithMany().HasForeignKey(x => new { x.SchoolMembershipId, x.SchoolId, UserId = x.TeacherUserId }).HasPrincipalKey(x => new { x.Id, x.SchoolId, x.UserId }).OnDelete(DeleteBehavior.Restrict);
            foreach (var pointer in new[] { "OriginalAssignmentId", "ResultingAssignmentId" }) b.HasOne<TeachingAssignment>().WithMany().HasForeignKey(pointer, "SchoolId", "TeacherUserId")
                .HasPrincipalKey(x => new { x.Id, x.SchoolId, x.TeacherUserId }).HasConstraintName("FK_RelationRequests_" + pointer).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.CompletedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
