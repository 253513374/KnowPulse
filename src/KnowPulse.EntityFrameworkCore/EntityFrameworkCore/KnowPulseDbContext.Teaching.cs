using KnowPulse.Exam;
using KnowPulse.Teaching;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace KnowPulse.EntityFrameworkCore;

public partial class KnowPulseDbContext
{
    public DbSet<TeacherInvitation> TeacherInvitations { get; set; }
    public DbSet<TeachingRelationship> TeachingRelationships { get; set; }
}

public static class TeachingModelBuilderExtensions
{
    public static void ConfigureTeaching(this ModelBuilder builder)
    {
        builder.Entity<TeacherInvitation>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "TeacherInvitations", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.InvitationCode).IsRequired().HasMaxLength(32);
            b.HasIndex(x => x.InvitationCode).IsUnique();
            b.HasIndex(x => x.TeacherId).IsUnique();
        });
        builder.Entity<TeachingRelationship>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "TeachingRelationships", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.DisplayName).IsRequired().HasMaxLength(64);
            b.HasIndex(x => new { x.TeacherId, x.StudentId }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.TeacherId, x.Status });
            b.HasIndex(x => new { x.TenantId, x.StudentId, x.Status });
        });
        builder.Entity<ExamPlan>().HasIndex(x => new { x.TenantId, x.OwnerId });
        builder.Entity<ExamAssignment>().HasIndex(x => new { x.PlanId, x.TargetId })
            .HasFilter("\"ScopeType\" = 3").IsUnique();
    }
}
