using System;
using KnowPulse.KnowledgeBase;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace KnowPulse.EntityFrameworkCore;

public static class PersonalNotebookModelBuilderExtensions
{
    public static void ConfigurePersonalNotebook(this ModelBuilder builder)
    {
        builder.Entity<PersonalNotebookCommand>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "PersonalNotebookCommands", KnowPulseConsts.DbSchema); b.ConfigureByConvention();
            b.Property(x => x.RequestFingerprint).IsRequired().HasMaxLength(64); b.Property(x => x.ContentHash).HasMaxLength(64);
            b.Property(x => x.RecordedAtUtc).HasConversion(new ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc)));
            b.HasIndex(x => new { x.TenantId, x.OwnerUserId, x.NoteId, x.RecordedAtUtc }).HasDatabaseName("IX_PersonalNotebookCommands_Owner");
            b.HasOne<KnowledgeItem>().WithMany().HasForeignKey(x => x.NoteId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<KnowledgeVersion>().WithMany().HasForeignKey(x => x.VersionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<KnowledgeAttachment>().WithMany().HasForeignKey(x => x.AttachmentId).OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_PersonalNotebookCommands_Receipt", "length(\"RequestFingerprint\") = 64 AND " +
                "((\"Kind\" IN (0,1,2) AND \"VersionId\" IS NOT NULL AND \"VersionNumber\" IS NOT NULL AND \"VersionNumber\" > 0) OR (\"Kind\" = 3 AND \"VersionId\" IS NULL AND \"VersionNumber\" IS NULL)) AND " +
                "((\"Kind\" = 2 AND \"AttachmentId\" IS NOT NULL AND \"ContentHash\" IS NOT NULL AND length(\"ContentHash\") = 64 AND \"FileSize\" IS NOT NULL AND \"FileSize\" >= 0) OR (\"Kind\" <> 2 AND \"AttachmentId\" IS NULL AND \"ContentHash\" IS NULL AND \"FileSize\" IS NULL))"));
        });
    }
}
