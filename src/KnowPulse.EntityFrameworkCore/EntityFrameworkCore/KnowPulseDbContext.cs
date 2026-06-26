using Microsoft.EntityFrameworkCore;
using KnowPulse.KnowledgeBase;
using KnowPulse.Organization;
using Volo.Abp.AuditLogging.EntityFrameworkCore;
using Volo.Abp.BackgroundJobs.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Volo.Abp.FeatureManagement.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.Identity.EntityFrameworkCore;
using Volo.Abp.OpenIddict.EntityFrameworkCore;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;
using Volo.Abp.SettingManagement.EntityFrameworkCore;
using Volo.Abp.TenantManagement;
using Volo.Abp.TenantManagement.EntityFrameworkCore;

namespace KnowPulse.EntityFrameworkCore;

[ReplaceDbContext(typeof(IIdentityDbContext))]
[ReplaceDbContext(typeof(ITenantManagementDbContext))]
[ConnectionStringName("Default")]
public class KnowPulseDbContext :
    AbpDbContext<KnowPulseDbContext>,
    IIdentityDbContext,
    ITenantManagementDbContext
{
    public DbSet<Department> Departments { get; set; }
    public DbSet<Position> Positions { get; set; }
    public DbSet<JobLevel> JobLevels { get; set; }
    public DbSet<UserOrganizationAssignment> UserOrganizationAssignments { get; set; }
    public DbSet<KnowledgeCategory> KnowledgeCategories { get; set; }
    public DbSet<KnowledgeTag> KnowledgeTags { get; set; }
    public DbSet<KnowledgeItem> KnowledgeItems { get; set; }
    public DbSet<KnowledgeVersion> KnowledgeVersions { get; set; }
    public DbSet<KnowledgeAttachment> KnowledgeAttachments { get; set; }
    public DbSet<KnowledgePermissionScope> KnowledgePermissionScopes { get; set; }

    #region Entities from the modules

    /* Notice: We only implemented IIdentityDbContext and ITenantManagementDbContext
     * and replaced them for this DbContext. This allows you to perform JOIN
     * queries for the entities of these modules over the repositories easily. You
     * typically don't need that for other modules. But, if you need, you can
     * implement the DbContext interface of the needed module and use ReplaceDbContext
     * attribute just like IIdentityDbContext and ITenantManagementDbContext.
     *
     * More info: Replacing a DbContext of a module ensures that the related module
     * uses this DbContext on runtime. Otherwise, it will use its own DbContext class.
     */

    //Identity
    public DbSet<IdentityUser> Users { get; set; }
    public DbSet<IdentityRole> Roles { get; set; }
    public DbSet<IdentityClaimType> ClaimTypes { get; set; }
    public DbSet<OrganizationUnit> OrganizationUnits { get; set; }
    public DbSet<IdentitySecurityLog> SecurityLogs { get; set; }
    public DbSet<IdentityLinkUser> LinkUsers { get; set; }
    public DbSet<IdentityUserDelegation> UserDelegations { get; set; }
    public DbSet<IdentitySession> Sessions { get; set; }
    // Tenant Management
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<TenantConnectionString> TenantConnectionStrings { get; set; }

    #endregion

    public KnowPulseDbContext(DbContextOptions<KnowPulseDbContext> options)
        : base(options)
    {

    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        /* Include modules to your migration db context */

        builder.ConfigurePermissionManagement();
        builder.ConfigureSettingManagement();
        builder.ConfigureBackgroundJobs();
        builder.ConfigureAuditLogging();
        builder.ConfigureIdentity();
        builder.ConfigureOpenIddict();
        builder.ConfigureFeatureManagement();
        builder.ConfigureTenantManagement();

        builder.Entity<Department>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "Departments", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Code).IsRequired().HasMaxLength(Department.MaxCodeLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(Department.MaxNameLength);
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            b.HasIndex(x => x.ParentId);
        });

        builder.Entity<Position>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "Positions", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Code).IsRequired().HasMaxLength(Position.MaxCodeLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(Position.MaxNameLength);
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            b.HasIndex(x => x.DepartmentId);
        });

        builder.Entity<JobLevel>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "JobLevels", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Code).IsRequired().HasMaxLength(JobLevel.MaxCodeLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(JobLevel.MaxNameLength);
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        });

        builder.Entity<UserOrganizationAssignment>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "UserOrganizationAssignments", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.HasIndex(x => new { x.TenantId, x.UserId, x.DepartmentId, x.PositionId });
            b.HasIndex(x => x.DepartmentId);
            b.HasIndex(x => x.PositionId);
            b.HasIndex(x => x.JobLevelId);
        });

        builder.Entity<KnowledgeCategory>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "KnowledgeCategories", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Code).IsRequired().HasMaxLength(KnowledgeCategory.MaxCodeLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(KnowledgeCategory.MaxNameLength);
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            b.HasIndex(x => x.ParentId);
        });

        builder.Entity<KnowledgeTag>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "KnowledgeTags", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(KnowledgeTag.MaxNameLength);
            b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        });

        builder.Entity<KnowledgeItem>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "KnowledgeItems", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Title).IsRequired().HasMaxLength(KnowledgeItem.MaxTitleLength);
            b.Property(x => x.Summary).HasMaxLength(KnowledgeItem.MaxSummaryLength);
            b.HasIndex(x => x.CategoryId);
            b.HasIndex(x => x.CurrentPublishedVersionId);
        });

        builder.Entity<KnowledgeVersion>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "KnowledgeVersions", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Title).IsRequired().HasMaxLength(KnowledgeVersion.MaxTitleLength);
            b.Property(x => x.ContentText).HasMaxLength(KnowledgeVersion.MaxContentTextLength);
            b.Property(x => x.Status).IsRequired();
            b.HasIndex(x => new { x.KnowledgeItemId, x.VersionNumber }).IsUnique();
            b.HasIndex(x => new { x.KnowledgeItemId, x.IsCurrentPublished })
                .HasDatabaseName("IX_AppKnowledgeVersions_KnowledgeItemId_CurrentPublished")
                .HasFilter("\"IsCurrentPublished\" AND NOT \"IsDeleted\"")
                .IsUnique();
        });

        builder.Entity<KnowledgeAttachment>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "KnowledgeAttachments", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.FileName).IsRequired().HasMaxLength(KnowledgeAttachment.MaxFileNameLength);
            b.Property(x => x.BlobName).IsRequired().HasMaxLength(KnowledgeAttachment.MaxBlobNameLength);
            b.Property(x => x.ContentType).IsRequired().HasMaxLength(KnowledgeAttachment.MaxContentTypeLength);
            b.HasIndex(x => x.KnowledgeVersionId);
        });

        builder.Entity<KnowledgePermissionScope>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "KnowledgePermissionScopes", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.ScopeType).IsRequired();
            b.HasIndex(x => new { x.KnowledgeItemId, x.ScopeType, x.TargetId });
            b.HasIndex(x => x.MinimumSecurityLevel);
        });
    }
}
