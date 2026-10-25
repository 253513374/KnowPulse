using System;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.Exam;
using KnowPulse.KnowledgeBase;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Volo.Abp.Data;
using Volo.Abp.BlobStoring;
using Volo.Abp.BlobStoring.FileSystem;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.SettingManagement;
using Volo.Abp.Uow;
using Xunit;

namespace KnowPulse.EntityFrameworkCore;

public sealed class TeachingPostgresFactAttribute : FactAttribute
{
    public TeachingPostgresFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("KNOWPULSE_RUN_POSTGRES_TESTS") != "1" ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KNOWPULSE_POSTGRES_TEST_CONNECTION")))
            Skip = "需显式启用 KNOWPULSE_RUN_POSTGRES_TESTS=1，并配置隔离验证库连接。";
    }
}

[DependsOn(typeof(KnowPulseApplicationTestModule), typeof(KnowPulseEntityFrameworkCoreModule), typeof(AbpBlobStoringFileSystemModule))]
public class TeachingPostgresVerificationModule : AbpModule
{
    public const string DatabaseName = "KnowPulseTeachingVerification20261025";

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var connection = Environment.GetEnvironmentVariable("KNOWPULSE_POSTGRES_TEST_CONNECTION");
        if (Environment.GetEnvironmentVariable("KNOWPULSE_RUN_POSTGRES_TESTS") != "1" || string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("未显式启用 PostgreSQL 验证。");
        if (!string.Equals(new NpgsqlConnectionStringBuilder(connection).Database, DatabaseName, StringComparison.Ordinal))
            throw new InvalidOperationException("PostgreSQL 验证只允许使用约定的隔离验证库。");

        var fault = new TeachingPublicationFaultInterceptor();
        context.Services.AddSingleton(fault);
        Configure<AbpBlobStoringOptions>(options => options.Containers.Configure<KnowledgeAttachmentBlobContainer>(container =>
            container.UseFileSystem(fileSystem => fileSystem.BasePath = Path.Combine(Path.GetTempPath(),
                "knowpulse-postgres-verification", Guid.NewGuid().ToString("N")))));
        Configure<AbpDbConnectionOptions>(options => options.ConnectionStrings.Default = connection);
        Configure<AbpDbContextOptions>(options => options.Configure(configuration =>
            configuration.DbContextOptions.UseNpgsql(connection).AddInterceptors(fault)));
        Configure<AbpUnitOfWorkDefaultOptions>(options => options.TransactionBehavior = UnitOfWorkTransactionBehavior.Enabled);
        Configure<FeatureManagementOptions>(options =>
        {
            options.SaveStaticFeaturesToDatabase = false;
            options.IsDynamicFeatureStoreEnabled = false;
        });
        Configure<PermissionManagementOptions>(options =>
        {
            options.SaveStaticPermissionsToDatabase = false;
            options.IsDynamicPermissionStoreEnabled = false;
        });
        Configure<SettingManagementOptions>(options =>
        {
            options.SaveStaticSettingsToDatabase = false;
            options.IsDynamicSettingStoreEnabled = false;
        });
    }
}

public sealed class TeachingPublicationFaultInterceptor : SaveChangesInterceptor
{
    public Guid? FailStudentId { get; set; }
    public Guid? FailedPlanId { get; private set; }
    public Guid? FailedPaperId { get; private set; }
    public bool SawTransaction { get; private set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var assignment = eventData.Context?.ChangeTracker.Entries<ExamAssignment>()
            .FirstOrDefault(x => x.State == EntityState.Added && x.Entity.TargetId == FailStudentId && FailStudentId.HasValue);
        if (assignment != null)
        {
            FailedPlanId = assignment.Entity.PlanId;
            FailedPaperId = eventData.Context!.ChangeTracker.Entries<ExamPlan>()
                .Single(x => x.Entity.Id == FailedPlanId).Entity.PaperId;
            SawTransaction = eventData.Context.Database.CurrentTransaction != null;
            throw new InvalidOperationException("PostgreSQL 验证：注入第二位学生指派保存失败。");
        }
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
