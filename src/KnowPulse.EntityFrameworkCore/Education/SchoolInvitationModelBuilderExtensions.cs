using System;
using KnowPulse.Education;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Volo.Abp.Identity;

namespace KnowPulse.EntityFrameworkCore;

public static class SchoolInvitationModelBuilderExtensions
{
    public static void ConfigureSchoolInvitations(this ModelBuilder builder)
    {
        builder.Entity<SchoolMembership>().HasAlternateKey(x => new { x.Id, x.SchoolId, x.UserId, x.Role }).HasName("AK_SchoolMembers_InvitationRole");
        builder.Entity<SchoolInvitation>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "SchoolInvitations", KnowPulseConsts.DbSchema, t => {
                t.HasCheckConstraint("CK_SchoolInvites_Role", "\"Role\" IN (1, 2) AND \"InvitedByUserId\" <> \"TargetUserId\"");
                t.HasCheckConstraint("CK_SchoolInvites_Period", "\"RecordedAtUtc\" < \"ExpiresAtUtc\" AND (\"EffectiveToUtc\" IS NULL OR (\"EffectiveFromUtc\" < \"EffectiveToUtc\" AND \"RecordedAtUtc\" < \"EffectiveToUtc\"))");
                t.HasCheckConstraint("CK_SchoolInvites_Fingerprints", "length(\"RequestFingerprint\") = 64 AND (\"CompletionFingerprint\" IS NULL OR length(\"CompletionFingerprint\") = 64)");
                t.HasCheckConstraint("CK_SchoolInvites_Completion", "(\"Status\" = 0 AND \"CompletedByUserId\" IS NULL AND \"CompletedAtUtc\" IS NULL AND \"CompletionOperationId\" IS NULL AND \"CompletionFingerprint\" IS NULL AND \"MembershipId\" IS NULL AND \"DeclineReason\" IS NULL AND \"RevocationReason\" IS NULL) OR " +
                    "(\"CompletedByUserId\" IS NOT NULL AND \"CompletedAtUtc\" IS NOT NULL AND \"CompletedAtUtc\" >= \"RecordedAtUtc\" AND \"CompletedAtUtc\" < \"ExpiresAtUtc\" AND \"CompletionOperationId\" IS NOT NULL AND \"CompletionFingerprint\" IS NOT NULL AND (" +
                    "(\"Status\" = 1 AND \"CompletedByUserId\" = \"TargetUserId\" AND \"MembershipId\" IS NOT NULL AND \"DeclineReason\" IS NULL AND \"RevocationReason\" IS NULL) OR " +
                    "(\"Status\" = 2 AND \"CompletedByUserId\" = \"TargetUserId\" AND \"MembershipId\" IS NULL AND \"DeclineReason\" IS NOT NULL AND \"DeclineReason\" IN (0, 1, 2) AND \"RevocationReason\" IS NULL) OR " +
                    "(\"Status\" = 3 AND \"MembershipId\" IS NULL AND \"DeclineReason\" IS NULL AND \"RevocationReason\" IS NOT NULL AND length(\"RevocationReason\") > 0)))");
            });
            b.ConfigureByConvention();
            var utc = new ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            var nullableUtc = new ValueConverter<DateTime?, DateTime?>(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null);
            b.Property(x => x.EffectiveFromUtc).HasConversion(utc); b.Property(x => x.EffectiveToUtc).HasConversion(nullableUtc);
            b.Property(x => x.RecordedAtUtc).HasConversion(utc); b.Property(x => x.ExpiresAtUtc).HasConversion(utc); b.Property(x => x.CompletedAtUtc).HasConversion(nullableUtc);
            b.Property(x => x.RequestFingerprint).IsRequired().HasMaxLength(64); b.Property(x => x.CompletionFingerprint).HasMaxLength(64);
            b.Property(x => x.RevocationReason).HasMaxLength(1000);
            b.HasIndex(x => new { x.TenantId, x.TargetUserId, x.RecordedAtUtc }).HasDatabaseName("IX_SchoolInvites_Recipient");
            b.HasIndex(x => new { x.SchoolId, x.RecordedAtUtc }).HasDatabaseName("IX_SchoolInvites_Manager");
            b.HasIndex(x => x.CompletionOperationId).IsUnique().HasFilter("\"CompletionOperationId\" IS NOT NULL");
            b.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.InvitedByUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.TargetUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.CompletedByUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<SchoolMembership>().WithMany().HasForeignKey(x => new { x.MembershipId, x.SchoolId, UserId = x.TargetUserId, x.Role })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId, x.UserId, x.Role }).HasConstraintName("FK_SchoolInvites_MembershipRecipientRole").OnDelete(DeleteBehavior.Restrict);
        });
    }
}
