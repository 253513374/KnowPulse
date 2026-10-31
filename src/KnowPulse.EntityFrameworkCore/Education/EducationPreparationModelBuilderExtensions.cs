using System;
using KnowPulse.Education;
using KnowPulse.Exam;
using KnowPulse.KnowledgeBase;
using KnowPulse.KnowledgeIndexing;
using KnowPulse.QuestionBank;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace KnowPulse.EntityFrameworkCore;

public static class EducationPreparationModelBuilderExtensions
{
    public static void ConfigureKnowPulseEducationPreparation(this ModelBuilder builder)
    {
        var utc = new ValueConverter<DateTime, DateTime>(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        var nullableUtc = new ValueConverter<DateTime?, DateTime?>(value => value,
            value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null);
        builder.Entity<KnowledgeMaterialCurriculumBinding>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "KnowledgeMaterialCurriculumBindings", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.CurriculumSnapshotJson).IsRequired().HasMaxLength(200000);
            b.Property(x => x.MaterialTitle).IsRequired().HasMaxLength(256);
            b.Property(x => x.ConfirmedAtUtc).HasConversion(utc);
            b.Property(x => x.EndedAtUtc).HasConversion(nullableUtc);
            b.HasAlternateKey(x => new { x.Id, x.KnowledgeVersionId });
            b.HasIndex(x => new { x.TenantId, x.OwnerUserId, x.TextbookVersionId, x.EndedAtUtc });
            b.HasIndex(x => new { x.KnowledgeVersionId, x.KnowledgeChunkId, x.TextbookKnowledgePointMappingId });
            b.HasOne<KnowledgeLibrary>().WithMany().HasForeignKey(x => new { x.LibraryId, x.OwnerUserId })
                .HasPrincipalKey(x => new { x.Id, x.OwnerUserId }).OnDelete(DeleteBehavior.Restrict);
            // Organization knowledge keeps nullable ownership. Do not turn those columns into
            // an alternate key (which would make them required); applications verify the owner pair.
            b.HasOne<KnowledgeItem>().WithMany().HasForeignKey(x => x.KnowledgeItemId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<KnowledgeVersion>().WithMany().HasForeignKey(x => new { x.KnowledgeVersionId, x.KnowledgeItemId })
                .HasPrincipalKey(x => new { x.Id, x.KnowledgeItemId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<KnowledgeChunk>().WithMany().HasForeignKey(x => new { x.KnowledgeChunkId, x.KnowledgeVersionId })
                .HasPrincipalKey(x => new { x.Id, x.KnowledgeVersionId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<TextbookKnowledgePointMapping>().WithMany().HasForeignKey(x => new { x.TextbookKnowledgePointMappingId, x.TextbookVersionId })
                .HasPrincipalKey(x => new { x.Id, x.TextbookVersionId }).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<LessonPreparation>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "LessonPreparations", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Title).IsRequired().HasMaxLength(256);
            b.Property(x => x.CreateFingerprint).IsRequired().HasMaxLength(64);
            b.Property(x => x.UpdatedAtUtc).HasConversion(utc);
            b.HasAlternateKey(x => new { x.Id, x.OwnerUserId });
            b.HasIndex(x => new { x.TenantId, x.OwnerUserId, x.UpdatedAtUtc });
            b.HasOne<KnowledgeLibrary>().WithMany().HasForeignKey(x => new { x.LibraryId, x.OwnerUserId })
                .HasPrincipalKey(x => new { x.Id, x.OwnerUserId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<LessonPreparationRevision>().WithMany().HasForeignKey(x => new { x.CurrentRevisionId, PreparationId = x.Id })
                .HasPrincipalKey(x => new { x.Id, x.PreparationId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<LessonPreparationRevision>().WithMany().HasForeignKey(x => x.CopiedFromRevisionId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<LessonPreparationRevision>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "LessonPreparationRevisions", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Title).IsRequired().HasMaxLength(256);
            b.Property(x => x.CurriculumSnapshotJson).IsRequired().HasMaxLength(200000);
            b.Property(x => x.Objectives).IsRequired().HasMaxLength(8192);
            b.Property(x => x.Reflection).HasMaxLength(8192);
            b.Property(x => x.RequestFingerprint).IsRequired().HasMaxLength(64);
            b.Property(x => x.RecordedAtUtc).HasConversion(utc);
            b.HasAlternateKey(x => new { x.Id, x.PreparationId });
            b.HasAlternateKey(x => new { x.Id, x.TextbookVersionId });
            b.HasIndex(x => new { x.PreparationId, x.RevisionNumber }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.OwnerUserId, x.TextbookVersionId });
            b.HasOne<LessonPreparation>().WithMany().HasForeignKey(x => new { x.PreparationId, x.OwnerUserId })
                .HasPrincipalKey(x => new { x.Id, x.OwnerUserId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<TextbookVersion>().WithMany().HasForeignKey(x => new { x.TextbookVersionId, x.SchoolId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId }).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<LessonPreparationRevisionMapping>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "LessonPreparationRevisionMappings", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.HasIndex(x => new { x.RevisionId, x.TextbookKnowledgePointMappingId }).IsUnique();
            b.HasIndex(x => x.ChapterId);
            b.HasOne<LessonPreparationRevision>().WithMany().HasForeignKey(x => new { x.RevisionId, x.TextbookVersionId })
                .HasPrincipalKey(x => new { x.Id, x.TextbookVersionId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<TextbookKnowledgePointMapping>().WithMany().HasForeignKey(x => new { x.TextbookKnowledgePointMappingId, x.TextbookVersionId, x.ChapterId })
                .HasPrincipalKey(x => new { x.Id, x.TextbookVersionId, x.ChapterId }).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<LessonPreparationReference>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "LessonPreparationReferences", KnowPulseConsts.DbSchema, table =>
                table.HasCheckConstraint("CK_AppLessonPreparationReferences_Source", "(\"Kind\" = 0 AND \"MaterialBindingId\" IS NOT NULL AND \"KnowledgeVersionId\" IS NOT NULL AND \"QuestionId\" IS NULL AND \"ExamPaperId\" IS NULL) OR (\"Kind\" = 1 AND \"QuestionId\" IS NOT NULL AND \"MaterialBindingId\" IS NULL AND \"KnowledgeVersionId\" IS NULL AND \"KnowledgeChunkId\" IS NULL AND \"ExamPaperId\" IS NULL) OR (\"Kind\" = 2 AND \"ExamPaperId\" IS NOT NULL AND \"MaterialBindingId\" IS NULL AND \"KnowledgeVersionId\" IS NULL AND \"KnowledgeChunkId\" IS NULL AND \"QuestionId\" IS NULL)"));
            b.ConfigureByConvention();
            b.Property(x => x.TitleSnapshot).IsRequired().HasMaxLength(256);
            b.Property(x => x.SourceConcurrencyStamp).HasMaxLength(40);
            b.HasIndex(x => new { x.RevisionId, x.MaterialBindingId }).IsUnique().HasFilter("\"MaterialBindingId\" IS NOT NULL");
            b.HasIndex(x => new { x.RevisionId, x.QuestionId }).IsUnique().HasFilter("\"QuestionId\" IS NOT NULL");
            b.HasIndex(x => new { x.RevisionId, x.ExamPaperId }).IsUnique().HasFilter("\"ExamPaperId\" IS NOT NULL");
            b.HasOne<LessonPreparationRevision>().WithMany().HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<KnowledgeMaterialCurriculumBinding>().WithMany().HasForeignKey(x => new { x.MaterialBindingId, x.KnowledgeVersionId })
                .HasPrincipalKey(x => new { x.Id, x.KnowledgeVersionId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<KnowledgeVersion>().WithMany().HasForeignKey(x => x.KnowledgeVersionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<KnowledgeChunk>().WithMany().HasForeignKey(x => new { x.KnowledgeChunkId, x.KnowledgeVersionId })
                .HasPrincipalKey(x => new { x.Id, x.KnowledgeVersionId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Question>().WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ExamPaper>().WithMany().HasForeignKey(x => x.ExamPaperId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
