using System;
using KnowPulse.Education;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace KnowPulse.EntityFrameworkCore;

public static class EducationTeachingPlanExecutionModelBuilderExtensions
{
    public static void ConfigureEducationTeachingPlanExecution(this ModelBuilder builder)
    {
        var utc = new ValueConverter<DateTime, DateTime>(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        builder.Entity<EducationTeachingPlanDecisionRevision>(b => {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "EducationTeachingPlanDecisions", KnowPulseConsts.DbSchema); b.ConfigureByConvention();
            b.HasAlternateKey(x => new { x.Id, x.PlanId, x.StudentUserId, x.OwnerUserId }).HasName("AK_EduPlanDecisions_Recipient");
            b.HasIndex(x => new { x.PlanId, x.RevisionNumber }).IsUnique().HasDatabaseName("IX_EduPlanDecisions_Sequence");
            b.Property(x => x.Reason).IsRequired().HasMaxLength(2000); b.Property(x => x.EvidenceJson).IsRequired().HasColumnType("text");
            foreach (var name in new[] { "EvidenceFingerprint", "RequestFingerprint" }) b.Property<string>(name).IsRequired().HasMaxLength(64);
            b.Property(x => x.ExpectedConcurrencyStamp).IsRequired().HasMaxLength(40); b.Property(x => x.RecordedAtUtc).HasConversion(utc);
            b.HasOne<EducationTeachingPlan>().WithMany().HasForeignKey(x => new { x.PlanId, x.StudentUserId, x.OwnerUserId })
                .HasPrincipalKey(x => new { x.Id, x.StudentUserId, x.OwnerUserId }).HasConstraintName("FK_EduPlanDecisions_Plan").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationTeachingPlanRevision>().WithMany().HasForeignKey(x => new { x.PublishedRevisionId, x.PlanId, x.StudentUserId, x.OwnerUserId })
                .HasPrincipalKey(x => new { x.Id, x.PlanId, x.StudentUserId, x.OwnerUserId }).HasConstraintName("FK_EduPlanDecisions_Publication").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationTeachingPlanDecisionRevision>().WithMany().HasForeignKey(x => new { x.BasedOnDecisionId, x.PlanId, x.StudentUserId, x.OwnerUserId })
                .HasPrincipalKey(x => new { x.Id, x.PlanId, x.StudentUserId, x.OwnerUserId }).HasConstraintName("FK_EduPlanDecisions_Predecessor").OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => {
                t.HasCheckConstraint("CK_EduPlanDecisions_Source", "length(\"EvidenceJson\") BETWEEN 1 AND 2097152 AND length(\"Reason\") BETWEEN 1 AND 2000 AND length(\"EvidenceFingerprint\") = 64 AND length(\"RequestFingerprint\") = 64 AND ((\"RevisionNumber\" = 1 AND \"BasedOnDecisionId\" IS NULL) OR (\"RevisionNumber\" > 1 AND \"BasedOnDecisionId\" IS NOT NULL AND \"BasedOnDecisionId\" <> \"Id\"))");
                t.HasCheckConstraint("CK_EduPlanDecisions_State", "\"Before\" IN (0,1) AND ((\"Decision\" = 0 AND \"Before\" = 0 AND \"After\" = 1) OR (\"Decision\" = 1 AND \"Before\" = 1 AND \"After\" = 0) OR (\"Decision\" = 2 AND \"Before\" = 0 AND \"After\" = 0) OR (\"Decision\" = 3 AND \"After\" = 1) OR (\"Decision\" = 4 AND \"After\" = 2) OR (\"Decision\" = 5 AND \"After\" = 3))");
            });
        });
        builder.Entity<EducationTeachingPlan>(b => {
            b.Property(x => x.ExecutionChangedAtUtc).HasConversion(utc);
            b.HasOne<EducationTeachingPlanDecisionRevision>().WithMany().HasForeignKey(x => new { x.CurrentDecisionId, x.Id, x.StudentUserId, x.OwnerUserId })
                .HasPrincipalKey(x => new { x.Id, x.PlanId, x.StudentUserId, x.OwnerUserId }).HasConstraintName("FK_EduPlans_CurrentDecision").OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_EduPlans_Execution", "\"ExecutionState\" BETWEEN 0 AND 3 AND ((\"DecisionRevisionNumber\" = 0 AND \"CurrentDecisionId\" IS NULL AND \"ExecutionState\" = 0 AND \"ExecutionChangedAtUtc\" IS NULL) OR (\"DecisionRevisionNumber\" > 0 AND \"CurrentDecisionId\" IS NOT NULL AND (\"ExecutionState\" = 0 OR \"ExecutionChangedAtUtc\" IS NOT NULL)))"));
        });
    }
}
