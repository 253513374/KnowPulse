using System;
using KnowPulse.WeChat;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Volo.Abp.Identity;

namespace KnowPulse.EntityFrameworkCore;

public static class WeChatBindingModelBuilderExtensions
{
    public static void ConfigureWeChatBindings(this ModelBuilder builder)
    {
        var utc = new ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        var nullableUtc = new ValueConverter<DateTime?, DateTime?>(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null);
        builder.Entity<WeChatAccountBinding>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "WeChatAccountBindings", KnowPulseConsts.DbSchema, t =>
            {
                t.HasCheckConstraint("CK_WeChatBinding_Time", "\"ExpiresAtUtc\" > \"IssuedAtUtc\" AND (\"BoundAtUtc\" IS NULL OR \"BoundAtUtc\" >= \"IssuedAtUtc\") AND (\"RevokedAtUtc\" IS NULL OR \"RevokedAtUtc\" >= \"IssuedAtUtc\")");
                t.HasCheckConstraint("CK_WeChatBinding_State", "(\"BoundAtUtc\" IS NULL AND \"SubjectHash\" IS NULL AND \"ActiveAccountKey\" IS NULL AND \"ActiveSubjectKey\" IS NULL) OR " +
                    "(\"BoundAtUtc\" IS NOT NULL AND \"SubjectHash\" IS NOT NULL AND length(\"SubjectHash\") = 64 AND " +
                    "((\"RevokedAtUtc\" IS NULL AND \"ActiveAccountKey\" IS NOT NULL AND length(\"ActiveAccountKey\") = 64 AND \"ActiveSubjectKey\" IS NOT NULL AND length(\"ActiveSubjectKey\") = 64) OR " +
                    "(\"RevokedAtUtc\" IS NOT NULL AND \"ActiveAccountKey\" IS NULL AND \"ActiveSubjectKey\" IS NULL)))");
            });
            b.ConfigureByConvention();
            b.Ignore(x => x.IsActive);
            b.Property(x => x.AppKey).IsRequired().HasMaxLength(64);
            b.Property(x => x.AppId).IsRequired().HasMaxLength(64);
            b.Property(x => x.ClientId).IsRequired().HasMaxLength(128);
            b.Property(x => x.CodeSecretHash).IsRequired().HasMaxLength(64);
            b.Property(x => x.AccountSecurityStampHash).IsRequired().HasMaxLength(64);
            b.Property(x => x.SubjectHash).HasMaxLength(64);
            b.Property(x => x.ActiveAccountKey).HasMaxLength(64);
            b.Property(x => x.ActiveSubjectKey).HasMaxLength(64);
            b.Property(x => x.IssuedAtUtc).HasConversion(utc);
            b.Property(x => x.ExpiresAtUtc).HasConversion(utc);
            b.Property(x => x.BoundAtUtc).HasConversion(nullableUtc);
            b.Property(x => x.RevokedAtUtc).HasConversion(nullableUtc);
            b.HasIndex(x => x.ActiveAccountKey).IsUnique().HasDatabaseName("IX_WeChatBinding_ActiveAccount");
            b.HasIndex(x => x.ActiveSubjectKey).IsUnique().HasDatabaseName("IX_WeChatBinding_ActiveSubject");
            b.HasIndex(x => new { x.TenantId, x.AccountUserId, x.IssuedAtUtc }).HasDatabaseName("IX_WeChatBinding_Owner");
            b.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.AccountUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
