using System;
using KnowPulse.Education;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace KnowPulse.EntityFrameworkCore;

public static class EducationTeachingPlanGenerationModelBuilderExtensions
{
    public static void ConfigureEducationTeachingPlanGeneration(this ModelBuilder builder)
    {
        builder.Entity<EducationTeachingPlanGenerationJob>(b => {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "EducationTeachingPlanGenerationJobs", KnowPulseConsts.DbSchema); b.ConfigureByConvention();
            b.HasAlternateKey(x => new { x.Id, x.PlanId, x.StudentUserId, x.OwnerUserId }).HasName("AK_EduPlanGeneration_Recipient");
            b.HasIndex(x => x.OwnerUserId).IsUnique().HasFilter("\"IsDeleted\" = false AND \"Status\" IN (0,1)").HasDatabaseName("IX_EduPlanGeneration_OneActiveOwner");
            b.HasIndex(x => new { x.TenantId, x.OwnerUserId, x.RequestedAtUtc }).HasDatabaseName("IX_EduPlanGeneration_OwnerTime");
            b.HasIndex(x => new { x.Status, x.LeaseExpiresAtUtc, x.RequestedAtUtc }).HasDatabaseName("IX_EduPlanGeneration_Due");
            b.HasOne<EducationLearningReportSnapshot>().WithMany().HasForeignKey(x => new { x.ReportSnapshotId, x.ReportId, x.OwnerUserId, x.SchoolId, x.SchoolClassId })
                .HasPrincipalKey(x => new { x.Id, x.ReportId, x.OwnerUserId, x.SchoolId, x.SchoolClassId }).HasConstraintName("FK_EduPlanGeneration_Snapshot").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<TeachingAssignment>().WithMany().HasForeignKey(x => new { x.TeachingAssignmentId, x.SchoolId, x.AcademicTermId, x.SchoolClassId, TeacherUserId = x.OwnerUserId, x.EducationSubjectId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId, x.AcademicTermId, x.SchoolClassId, x.TeacherUserId, x.EducationSubjectId }).HasConstraintName("FK_EduPlanGeneration_Teaching").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<StudentEnrollment>().WithMany().HasForeignKey(x => new { x.StudentEnrollmentId, x.SchoolId, x.SchoolClassId, x.StudentUserId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId, x.SchoolClassId, x.StudentUserId }).HasConstraintName("FK_EduPlanGeneration_Enrollment").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<SchoolMembership>().WithMany().HasForeignKey(x => new { x.SchoolMembershipId, x.SchoolId, x.StudentUserId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId, x.UserId }).HasConstraintName("FK_EduPlanGeneration_Member").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<TextbookVersion>().WithMany().HasForeignKey(x => new { x.TextbookVersionId, x.SchoolId, x.EducationSubjectId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId, x.EducationSubjectId }).HasConstraintName("FK_EduPlanGeneration_Book").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationTeachingPlanGenerationJob>().WithMany().HasForeignKey(x => new { x.RetryOfJobId, x.PlanId, x.StudentUserId, x.OwnerUserId })
                .HasPrincipalKey(x => new { x.Id, x.PlanId, x.StudentUserId, x.OwnerUserId }).HasConstraintName("FK_EduPlanGeneration_Retry").OnDelete(DeleteBehavior.Restrict);
            foreach (var name in new[] { "BasisJson", "RequestJson", "ContentJson" }) b.Property<string>(name).HasColumnType("text");
            b.Property(x => x.BasisJson).IsRequired(); b.Property(x => x.RequestJson).IsRequired();
            foreach (var name in new[] { "RequestFingerprint", "PromptHash", "OutputHash", "PromptVersion", "ModelVersion", "ModelProvider" }) b.Property<string>(name).HasMaxLength(64);
            b.Property(x => x.RequestFingerprint).IsRequired(); b.Property(x => x.PromptVersion).IsRequired(); b.Property(x => x.ModelAlias).HasMaxLength(128); b.Property(x => x.EstimatedCostUsd).HasPrecision(28, 12);
            b.Property(x => x.StudentName).IsRequired().HasMaxLength(256); b.Property(x => x.SubjectName).IsRequired().HasMaxLength(128); b.Property(x => x.TextbookTitle).IsRequired().HasMaxLength(256);
            b.Property(x => x.RequestedAtUtc).HasConversion(new ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc)));
            foreach (var name in new[] { "CompletedAtUtc", "HeartbeatAtUtc", "LeaseExpiresAtUtc" }) b.Property<DateTime?>(name).HasConversion(new ValueConverter<DateTime?, DateTime?>(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null));
            b.ToTable(t => {
                t.HasCheckConstraint("CK_EduPlanGeneration_State", "\"Status\" IN (0,1,2,3) AND \"Failure\" IN (0,1,2,3,4,5,6) AND ((\"Status\" IN (0,1,2) AND \"Failure\" = 0) OR (\"Status\" = 3 AND \"Failure\" <> 0)) AND " +
                    "((\"Status\" = 0 AND \"RunId\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"CompletedAtUtc\" IS NULL) OR (\"Status\" = 1 AND \"RunId\" IS NOT NULL AND \"HeartbeatAtUtc\" IS NOT NULL AND \"LeaseExpiresAtUtc\" IS NOT NULL AND \"CompletedAtUtc\" IS NULL) OR (\"Status\" IN (2,3) AND \"RunId\" IS NOT NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"CompletedAtUtc\" IS NOT NULL)) AND " +
                    "((\"Status\" = 2 AND \"ContentJson\" IS NOT NULL AND length(\"ContentJson\") BETWEEN 1 AND 262144 AND \"ModelAlias\" IS NOT NULL AND \"OutputHash\" IS NOT NULL) OR (\"Status\" <> 2 AND \"ContentJson\" IS NULL))");
                t.HasCheckConstraint("CK_EduPlanGeneration_Source", "length(\"BasisJson\") BETWEEN 1 AND 8388608 AND length(\"RequestJson\") BETWEEN 1 AND 32000 AND length(\"RequestFingerprint\") = 64 AND (\"RetryOfJobId\" IS NULL OR \"RetryOfJobId\" <> \"Id\")");
                t.HasCheckConstraint("CK_EduPlanGeneration_Usage", "(\"PromptTokens\" IS NULL OR \"PromptTokens\" >= 0) AND (\"CompletionTokens\" IS NULL OR \"CompletionTokens\" >= 0) AND (\"DurationMilliseconds\" IS NULL OR \"DurationMilliseconds\" >= 0) AND (\"EstimatedCostUsd\" IS NULL OR \"EstimatedCostUsd\" >= 0) AND " +
                    "((\"ModelAlias\" IS NULL AND \"ModelVersion\" IS NULL AND \"ModelProvider\" IS NULL AND \"PromptHash\" IS NULL AND \"OutputTokenLimit\" IS NULL AND \"Temperature\" IS NULL) OR (\"ModelAlias\" IS NOT NULL AND \"ModelVersion\" IS NOT NULL AND \"ModelProvider\" IS NOT NULL AND \"PromptHash\" IS NOT NULL AND \"OutputTokenLimit\" IS NOT NULL AND \"Temperature\" IS NOT NULL AND length(\"PromptHash\") = 64 AND \"OutputTokenLimit\" BETWEEN 1 AND 8192 AND \"Temperature\" BETWEEN 0 AND 2))");
            });
        });
        builder.Entity<EducationTeachingPlanRevision>().HasOne<EducationTeachingPlanGenerationJob>().WithMany().HasForeignKey(x => new { x.GenerationJobId, x.PlanId, x.StudentUserId, x.OwnerUserId })
            .HasPrincipalKey(x => new { x.Id, x.PlanId, x.StudentUserId, x.OwnerUserId }).HasConstraintName("FK_EduPlanVersions_Generation").OnDelete(DeleteBehavior.Restrict);
    }
}
