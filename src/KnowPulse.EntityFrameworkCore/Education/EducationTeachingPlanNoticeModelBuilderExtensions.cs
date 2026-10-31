using System;
using KnowPulse.Education;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace KnowPulse.EntityFrameworkCore;

public static class EducationTeachingPlanNoticeModelBuilderExtensions
{
    public static void ConfigureEducationTeachingPlanNotices(this ModelBuilder builder)
    {
        builder.Entity<EducationTeachingPlan>().HasAlternateKey(x => new { x.Id, x.SchoolId, x.StudentUserId, x.OwnerUserId }).HasName("AK_EduPlans_NoticeSchool");
        builder.Entity<EducationTeachingActivity>().HasAlternateKey(x => new { x.Id, x.PlanId, x.PlanRevisionId, x.StudentUserId, x.OwnerUserId }).HasName("AK_EduActivities_NoticePlan");
        builder.Entity<EducationTeachingPlanNotice>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "EducationTeachingPlanNotices", KnowPulseConsts.DbSchema, t => {
                t.HasCheckConstraint("CK_EduPlanNotices_ReadTime", "\"ReadAtUtc\" IS NULL OR \"ReadAtUtc\" >= \"RecordedAtUtc\"");
                t.HasCheckConstraint("CK_EduPlanNotices_Source", "(\"Kind\" IN (0, 1) AND \"ContentRevisionId\" IS NOT NULL AND \"DecisionId\" IS NULL AND \"ActivityId\" IS NULL AND \"ActivityRevisionId\" IS NULL) OR " +
                    "(\"Kind\" = 2 AND \"ContentRevisionId\" IS NULL AND \"DecisionId\" IS NOT NULL AND \"ActivityId\" IS NULL AND \"ActivityRevisionId\" IS NULL) OR " +
                    "(\"Kind\" = 3 AND \"ContentRevisionId\" IS NULL AND \"DecisionId\" IS NULL AND \"ActivityId\" IS NOT NULL AND \"ActivityRevisionId\" IS NOT NULL)");
            });
            b.ConfigureByConvention();
            b.Property(x => x.RecordedAtUtc).HasConversion(new ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc)));
            b.Property(x => x.ReadAtUtc).HasConversion(new ValueConverter<DateTime?, DateTime?>(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null));
            b.HasIndex(x => new { x.TenantId, x.StudentUserId, x.SchoolId, x.RecordedAtUtc }).HasDatabaseName("IX_EduPlanNotices_Inbox");
            b.HasIndex(x => x.ContentRevisionId).IsUnique().HasFilter("\"ContentRevisionId\" IS NOT NULL");
            b.HasIndex(x => x.DecisionId).IsUnique().HasFilter("\"DecisionId\" IS NOT NULL");
            b.HasIndex(x => x.ActivityRevisionId).IsUnique().HasFilter("\"ActivityRevisionId\" IS NOT NULL");
            b.HasOne<EducationTeachingPlan>().WithMany().HasForeignKey(x => new { x.PlanId, x.SchoolId, x.StudentUserId, x.OwnerUserId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId, x.StudentUserId, x.OwnerUserId }).HasConstraintName("FK_EduPlanNotices_PlanSchool").OnDelete(DeleteBehavior.Restrict);
            foreach (var pointer in new[] { "PublishedRevisionId", "ContentRevisionId" })
                b.HasOne<EducationTeachingPlanRevision>().WithMany().HasForeignKey(pointer, "PlanId", "StudentUserId", "OwnerUserId")
                    .HasPrincipalKey(x => new { x.Id, x.PlanId, x.StudentUserId, x.OwnerUserId }).HasConstraintName("FK_EduPlanNotices_" + pointer).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationTeachingPlanDecisionRevision>().WithMany().HasForeignKey(x => new { x.DecisionId, x.PlanId, x.StudentUserId, x.OwnerUserId })
                .HasPrincipalKey(x => new { x.Id, x.PlanId, x.StudentUserId, x.OwnerUserId }).HasConstraintName("FK_EduPlanNotices_Decision").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationTeachingActivity>().WithMany().HasForeignKey(x => new { x.ActivityId, x.PlanId, PlanRevisionId = x.PublishedRevisionId, x.StudentUserId, x.OwnerUserId })
                .HasPrincipalKey(x => new { x.Id, x.PlanId, x.PlanRevisionId, x.StudentUserId, x.OwnerUserId }).HasConstraintName("FK_EduPlanNotices_Activity").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationTeachingActivityRevision>().WithMany().HasForeignKey(x => new { x.ActivityRevisionId, x.ActivityId })
                .HasPrincipalKey(x => new { x.Id, x.ActivityId }).HasConstraintName("FK_EduPlanNotices_FollowUp").OnDelete(DeleteBehavior.Restrict);
        });
    }
}
