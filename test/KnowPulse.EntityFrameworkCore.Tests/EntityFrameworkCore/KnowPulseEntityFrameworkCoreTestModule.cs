using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Sqlite;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.SettingManagement;
using Volo.Abp.Uow;
using System;
using System.IO;
using KnowPulse.KnowledgeBase;
using Volo.Abp.BlobStoring;
using Volo.Abp.BlobStoring.FileSystem;

namespace KnowPulse.EntityFrameworkCore;

[DependsOn(
    typeof(KnowPulseApplicationTestModule),
    typeof(KnowPulseEntityFrameworkCoreModule),
    typeof(AbpBlobStoringFileSystemModule),
    typeof(AbpEntityFrameworkCoreSqliteModule)
    )]
public class KnowPulseEntityFrameworkCoreTestModule : AbpModule
{
    private SqliteConnection? _sqliteConnection;
    private readonly string _blobBasePath = Path.Combine(
        Path.GetTempPath(),
        "knowpulse-blob-tests",
        Guid.NewGuid().ToString("N"));

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
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
        context.Services.AddAlwaysDisableUnitOfWorkTransaction();

        ConfigureBlobStoring();
        ConfigureInMemorySqlite(context.Services);
    }

    private void ConfigureBlobStoring()
    {
        Configure<AbpBlobStoringOptions>(options =>
        {
            options.Containers.Configure<KnowledgeAttachmentBlobContainer>(container =>
            {
                container.UseFileSystem(fileSystem =>
                {
                    fileSystem.BasePath = _blobBasePath;
                    fileSystem.AppendContainerNameToBasePath = true;
                });
            });
        });
    }

    private void ConfigureInMemorySqlite(IServiceCollection services)
    {
        _sqliteConnection = CreateDatabaseAndGetConnection();

        services.Configure<AbpDbContextOptions>(options =>
        {
            options.Configure(context =>
            {
                context.DbContextOptions.UseSqlite(_sqliteConnection);
            });
        });
    }

    public override void OnApplicationShutdown(ApplicationShutdownContext context)
    {
        _sqliteConnection?.Dispose();

        if (Directory.Exists(_blobBasePath))
        {
            Directory.Delete(_blobBasePath, recursive: true);
        }
    }

    private static SqliteConnection CreateDatabaseAndGetConnection()
    {
        var connection = new AbpUnitTestSqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<KnowPulseDbContext>()
            .UseSqlite(connection)
            .Options;

        using (var context = new KnowPulseDbContext(options))
        {
            context.GetService<IRelationalDatabaseCreator>().CreateTables();
        }

        return connection;
    }
}
