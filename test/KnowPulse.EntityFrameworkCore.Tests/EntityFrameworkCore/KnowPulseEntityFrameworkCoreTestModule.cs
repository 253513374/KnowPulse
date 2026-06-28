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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.KnowledgeBase;
using KnowPulse.RagAI;
using Volo.Abp.BlobStoring;
using Volo.Abp.BlobStoring.FileSystem;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

    /// <summary>
    /// 配置 EF Core 集成测试环境、Blob 文件系统和 RAG 测试替身。
    /// </summary>
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
        ConfigureRagAiTestDoubles(context.Services);
    }

    /// <summary>
    /// 注册 RAG 相关测试替身，避免集成测试访问真实模型和向量库。
    /// </summary>
    private static void ConfigureRagAiTestDoubles(IServiceCollection services)
    {
        services.AddSingleton<TestKnowledgeEmbeddingClient>();
        services.Replace(ServiceDescriptor.Singleton<IKnowledgeEmbeddingClient>(
            serviceProvider => serviceProvider.GetRequiredService<TestKnowledgeEmbeddingClient>()));
        services.AddSingleton<TestKnowledgeVectorStore>();
        services.Replace(ServiceDescriptor.Singleton<IKnowledgeVectorStore>(
            serviceProvider => serviceProvider.GetRequiredService<TestKnowledgeVectorStore>()));
        services.AddSingleton<TestRagChatClient>();
        services.Replace(ServiceDescriptor.Singleton<IRagChatClient>(
            serviceProvider => serviceProvider.GetRequiredService<TestRagChatClient>()));
    }

    /// <summary>
    /// 配置测试附件 Blob 存储到临时文件系统目录。
    /// </summary>
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

    /// <summary>
    /// 配置共享的内存 SQLite 数据库连接。
    /// </summary>
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

    /// <summary>
    /// 测试应用关闭时释放 SQLite 连接并清理临时 Blob 目录。
    /// </summary>
    public override void OnApplicationShutdown(ApplicationShutdownContext context)
    {
        _sqliteConnection?.Dispose();

        if (Directory.Exists(_blobBasePath))
        {
            Directory.Delete(_blobBasePath, recursive: true);
        }
    }

    /// <summary>
    /// 创建内存 SQLite 数据库连接并初始化表结构。
    /// </summary>
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

public class TestKnowledgeEmbeddingClient : IKnowledgeEmbeddingClient
{
    public bool ShouldFail { get; set; }

    public string FailureReason { get; set; } = "Test embedding failure.";

    public int CallCount { get; private set; }

    /// <summary>
    /// 重置 embedding 测试替身状态。
    /// </summary>
    public void Reset()
    {
        ShouldFail = false;
        FailureReason = "Test embedding failure.";
        CallCount = 0;
    }

    /// <summary>
    /// 生成确定性测试向量，或按测试配置返回失败。
    /// </summary>
    public Task<KnowledgeEmbeddingClientResult> GenerateAsync(
        KnowledgeEmbeddingClientRequest request,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        if (ShouldFail)
        {
            return Task.FromResult(KnowledgeEmbeddingClientResult.Failed(FailureReason));
        }

        var embedding = Enumerable.Range(0, request.VectorDimension)
            .Select(i => (float)((request.Input.Length + i) % 31) / 31)
            .ToArray();

        return Task.FromResult(KnowledgeEmbeddingClientResult.Success(embedding));
    }
}

public class TestKnowledgeVectorStore : IKnowledgeVectorStore
{
    public List<KnowledgeVectorSearchRequest> SearchRequests { get; } = [];

    public List<VectorSearchHit> SearchResults { get; } = [];

    /// <summary>
    /// 清空向量库测试替身记录的请求和预置结果。
    /// </summary>
    public void Reset()
    {
        SearchRequests.Clear();
        SearchResults.Clear();
    }

    /// <summary>
    /// 测试替身忽略向量写入请求。
    /// </summary>
    public Task UpsertChunksAsync(
        IReadOnlyList<KnowledgeChunkVectorRecord> chunks,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 记录检索请求并返回预置的搜索结果。
    /// </summary>
    public Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
        KnowledgeVectorSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        SearchRequests.Add(request);
        return Task.FromResult<IReadOnlyList<VectorSearchHit>>(SearchResults.ToList());
    }

    /// <summary>
    /// 测试替身忽略按版本删除向量请求。
    /// </summary>
    public Task DeleteByKnowledgeVersionAsync(Guid knowledgeVersionId, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

public class TestRagChatClient : IRagChatClient
{
    private const string DefaultResponseContent =
        "根据当前可访问知识库，员工请假流程需要先在系统提交申请，再由直属主管审批。[1]";

    public bool ShouldFail { get; set; }

    public string FailureReason { get; set; } = "Test chat failure.";

    public string ResponseContent { get; set; } = DefaultResponseContent;

    public int CallCount { get; private set; }

    public List<RagChatClientRequest> Requests { get; } = [];

    /// <summary>
    /// 重置 chat 测试替身状态。
    /// </summary>
    public void Reset()
    {
        ShouldFail = false;
        FailureReason = "Test chat failure.";
        ResponseContent = DefaultResponseContent;
        CallCount = 0;
        Requests.Clear();
    }

    /// <summary>
    /// 记录 chat 请求并返回确定性测试回答，或按测试配置返回失败。
    /// </summary>
    public Task<RagChatClientResult> GenerateAsync(
        RagChatClientRequest request,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        Requests.Add(request);

        if (ShouldFail)
        {
            return Task.FromResult(RagChatClientResult.Failed(FailureReason));
        }

        return Task.FromResult(RagChatClientResult.Success(
            ResponseContent,
            promptTokenCount: 120,
            completionTokenCount: 30,
            totalTokenCount: 150));
    }
}
