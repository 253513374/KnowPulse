using System;
using KnowPulse.Education;
using KnowPulse.KnowledgeBase;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Volo.Abp.Identity;

namespace KnowPulse.EntityFrameworkCore;

public static class EducationModelBuilderExtensions
{
    public static void ConfigureKnowPulseEducation(this ModelBuilder builder)
    {
        var utc = new ValueConverter<DateTime, DateTime>(value => value,
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        var nullableUtc = new ValueConverter<DateTime?, DateTime?>(value => value,
            value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null);

        builder.Entity<PersonalKnowledgeExport>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "PersonalKnowledgeExports", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.BlobName).HasMaxLength(KnowledgeAttachment.MaxBlobNameLength);
            b.Property(x => x.FailureStage).HasMaxLength(32);
            b.Property(x => x.FailureCode).HasMaxLength(128);
            b.Property(x => x.SnapshotAtUtc).HasConversion(nullableUtc);
            b.Property(x => x.StartedAtUtc).HasConversion(nullableUtc);
            b.Property(x => x.LastHeartbeatAtUtc).HasConversion(nullableUtc);
            b.Property(x => x.CompletedAtUtc).HasConversion(nullableUtc);
            b.Property(x => x.ExpiresAtUtc).HasConversion(nullableUtc);
            b.Property(x => x.LastCleanupAtUtc).HasConversion(nullableUtc);
            b.HasIndex(x => new { x.TenantId, x.OwnerUserId, x.CreationTime });
            b.HasIndex(x => new { x.Status, x.LastHeartbeatAtUtc });
            b.HasOne<KnowledgeLibrary>().WithMany()
                .HasForeignKey(x => new { x.LibraryId, x.OwnerUserId })
                .HasPrincipalKey(x => new { x.Id, x.OwnerUserId }).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<SchoolKnowledgeShare>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "SchoolKnowledgeShares", KnowPulseConsts.DbSchema,
                table => table.HasCheckConstraint("CK_AppSchoolKnowledgeShares_Uses",
                    "(\"Uses\" & 1) = 1 AND (\"Uses\" & ~63) = 0 AND ((\"Uses\" & 4) = 0 OR (\"Uses\" & 2) = 2)"));
            b.ConfigureByConvention();
            b.Property(x => x.Title).IsRequired().HasMaxLength(KnowledgeItem.MaxTitleLength);
            b.Property(x => x.Summary).HasMaxLength(KnowledgeItem.MaxSummaryLength);
            b.Property(x => x.CurriculumBindingIdsJson).IsRequired()
                .HasMaxLength(SchoolKnowledgeShare.MaxCurriculumBindingIdsJsonLength).HasDefaultValue("[]");
            b.Property(x => x.SharedAtUtc).HasConversion(utc);
            b.Property(x => x.RevokedAtUtc).HasConversion(nullableUtc);
            b.HasIndex(x => new { x.KnowledgeItemId, x.KnowledgeVersionId, x.SchoolId }).IsUnique()
                .HasFilter("NOT \"IsDeleted\" AND NOT \"IsRevoked\"");
            b.HasIndex(x => new { x.TenantId, x.SchoolId, x.IsRevoked });
            b.HasIndex(x => new { x.TenantId, x.OwnerUserId, x.SharedAtUtc });
            b.HasOne<KnowledgeLibrary>().WithMany()
                .HasForeignKey(x => new { x.LibraryId, x.OwnerUserId })
                .HasPrincipalKey(x => new { x.Id, x.OwnerUserId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<KnowledgeItem>().WithMany().HasForeignKey(x => x.KnowledgeItemId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<KnowledgeVersion>().WithMany()
                .HasForeignKey(x => new { x.KnowledgeVersionId, x.KnowledgeItemId })
                .HasPrincipalKey(x => new { x.Id, x.KnowledgeItemId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<TextbookVersion>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "TextbookVersions", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Ignore(x => x.Stage);
            b.Property(x => x.CatalogCode).IsRequired().HasMaxLength(64);
            b.Property(x => x.Title).IsRequired().HasMaxLength(256);
            b.Property(x => x.Publisher).IsRequired().HasMaxLength(128);
            b.Property(x => x.Edition).IsRequired().HasMaxLength(128);
            b.Property(x => x.Volume).IsRequired().HasMaxLength(64);
            b.Property(x => x.PublishedAtUtc).HasConversion(nullableUtc);
            b.Property(x => x.RetiredAtUtc).HasConversion(nullableUtc);
            b.HasIndex(x => new { x.SchoolId, x.CatalogCode, x.CatalogRevision }).IsUnique();
            b.HasAlternateKey(x => new { x.Id, x.SchoolId });
            b.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationSubject>().WithMany().HasForeignKey(x => x.EducationSubjectId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<TextbookVersion>().WithMany().HasForeignKey(x => new { x.PreviousVersionId, x.SchoolId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId }).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<TextbookChapter>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "TextbookChapters", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Code).IsRequired().HasMaxLength(64);
            b.Property(x => x.Title).IsRequired().HasMaxLength(256);
            b.HasAlternateKey(x => new { x.Id, x.TextbookVersionId });
            // The aggregate validates distinct codes; a nonunique index permits swapping draft codes in one save.
            b.HasIndex(x => new { x.TextbookVersionId, x.Code });
            b.HasOne<TextbookVersion>().WithMany().HasForeignKey(x => x.TextbookVersionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<TextbookChapter>().WithMany().HasForeignKey(x => new { x.ParentChapterId, x.TextbookVersionId })
                .HasPrincipalKey(x => new { x.Id, x.TextbookVersionId }).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<CourseKnowledgePoint>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "CourseKnowledgePoints", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Code).IsRequired().HasMaxLength(64);
            b.HasIndex(x => new { x.SchoolId, x.EducationSubjectId, x.Code }).IsUnique();
            b.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationSubject>().WithMany().HasForeignKey(x => x.EducationSubjectId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<CourseKnowledgePointRevision>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "CourseKnowledgePointRevisions", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(128);
            b.HasAlternateKey(x => new { x.Id, x.KnowledgePointId });
            b.HasIndex(x => new { x.KnowledgePointId, x.RevisionNumber }).IsUnique();
            b.HasOne<CourseKnowledgePoint>().WithMany().HasForeignKey(x => x.KnowledgePointId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<CourseKnowledgePointRevision>().WithMany().HasForeignKey(x => x.ParentRevisionId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<TextbookKnowledgePointMapping>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "TextbookKnowledgePointMappings", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.ConfirmedAtUtc).HasConversion(utc);
            b.HasIndex(x => new { x.TextbookVersionId, x.ChapterId, x.KnowledgePointId }).IsUnique().HasFilter("NOT \"IsDeleted\"");
            b.HasOne<TextbookChapter>().WithMany().HasForeignKey(x => new { x.ChapterId, x.TextbookVersionId })
                .HasPrincipalKey(x => new { x.Id, x.TextbookVersionId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<CourseKnowledgePointRevision>().WithMany().HasForeignKey(x => new { x.KnowledgePointRevisionId, x.KnowledgePointId })
                .HasPrincipalKey(x => new { x.Id, x.KnowledgePointId }).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<TeacherTextbookSelection>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "TeacherTextbookSelections", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.SelectedAtUtc).HasConversion(utc);
            b.Property(x => x.EndedAtUtc).HasConversion(nullableUtc);
            b.HasIndex(x => x.TeachingAssignmentId).IsUnique().HasFilter("NOT \"IsDeleted\" AND \"EndedAtUtc\" IS NULL");
            b.HasIndex(x => new { x.TenantId, x.TeacherUserId, x.SelectedAtUtc });
            b.HasOne<TextbookVersion>().WithMany().HasForeignKey(x => new { x.TextbookVersionId, x.SchoolId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<TeachingAssignment>().WithMany().HasForeignKey(x => new { x.TeachingAssignmentId, x.SchoolId, x.TeacherUserId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId, x.TeacherUserId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.TeacherUserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<School>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "Schools", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Code).IsRequired().HasMaxLength(School.MaxCodeLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(School.MaxNameLength);
            b.Property(x => x.ConfigurationRevision).IsConcurrencyToken();
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            b.HasIndex(x => x.Code).IsUnique().HasFilter("\"TenantId\" IS NULL");
        });
        builder.Entity<SchoolMembership>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "SchoolMemberships", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.EffectiveFromUtc).HasConversion(utc);
            b.Property(x => x.EffectiveToUtc).HasConversion(nullableUtc);
            b.HasIndex(x => new { x.SchoolId, x.UserId, x.Role, x.EffectiveFromUtc }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.UserId, x.EffectiveFromUtc, x.EffectiveToUtc });
            b.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<AcademicTerm>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "AcademicTerms", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Code).IsRequired().HasMaxLength(AcademicTerm.MaxCodeLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(AcademicTerm.MaxNameLength);
            b.Property(x => x.StartsAtUtc).HasConversion(utc);
            b.Property(x => x.EndsAtUtc).HasConversion(utc);
            b.HasIndex(x => new { x.SchoolId, x.Code }).IsUnique();
            b.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<SchoolClass>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "SchoolClasses", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Code).IsRequired().HasMaxLength(SchoolClass.MaxCodeLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(SchoolClass.MaxNameLength);
            b.HasIndex(x => new { x.SchoolId, x.AcademicTermId, x.Code }).IsUnique();
            b.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<AcademicTerm>().WithMany().HasForeignKey(x => x.AcademicTermId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<EducationSubject>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "EducationSubjects", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Code).IsRequired().HasMaxLength(EducationSubject.MaxCodeLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(EducationSubject.MaxNameLength);
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            b.HasIndex(x => x.Code).IsUnique().HasFilter("\"TenantId\" IS NULL");
        });
        builder.Entity<TeachingAssignment>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "TeachingAssignments", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.EffectiveFromUtc).HasConversion(utc);
            b.Property(x => x.EffectiveToUtc).HasConversion(nullableUtc);
            b.HasIndex(x => new { x.SchoolId, x.SchoolClassId, x.EducationSubjectId, x.TeacherUserId, x.EffectiveFromUtc }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.TeacherUserId, x.EffectiveFromUtc, x.EffectiveToUtc });
            b.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<AcademicTerm>().WithMany().HasForeignKey(x => x.AcademicTermId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<SchoolClass>().WithMany().HasForeignKey(x => x.SchoolClassId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationSubject>().WithMany().HasForeignKey(x => x.EducationSubjectId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.TeacherUserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<StudentEnrollment>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "StudentEnrollments", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.EffectiveFromUtc).HasConversion(utc);
            b.Property(x => x.EffectiveToUtc).HasConversion(nullableUtc);
            b.HasIndex(x => new { x.SchoolId, x.StudentUserId, x.SchoolClassId, x.EffectiveFromUtc }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.SchoolId, x.StudentUserId, x.EffectiveFromUtc, x.EffectiveToUtc });
            b.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<SchoolClass>().WithMany().HasForeignKey(x => x.SchoolClassId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.StudentUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
