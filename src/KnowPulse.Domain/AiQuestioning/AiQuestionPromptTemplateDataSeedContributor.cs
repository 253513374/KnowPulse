using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Uow;

namespace KnowPulse.AiQuestioning;

/// <summary>
/// 初始化 AI 出题默认 prompt 模板版本。
/// 该种子只写模板元数据，不触发任何 RAG 检索、模型调用或候选题生成。
/// </summary>
public class AiQuestionPromptTemplateDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IConfiguration _configuration;
    private readonly IRepository<AiQuestionPromptTemplate, Guid> _promptTemplateRepository;
    private readonly IGuidGenerator _guidGenerator;

    /// <summary>
    /// 创建 AI 出题 prompt 模板种子组件。
    /// </summary>
    public AiQuestionPromptTemplateDataSeedContributor(
        IConfiguration configuration,
        IRepository<AiQuestionPromptTemplate, Guid> promptTemplateRepository,
        IGuidGenerator guidGenerator)
    {
        _configuration = configuration;
        _promptTemplateRepository = promptTemplateRepository;
        _guidGenerator = guidGenerator;
    }

    /// <summary>
    /// 为宿主或租户补齐默认 prompt 模板版本。
    /// </summary>
    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        var templateKey = Read("PromptTemplateKey", AiQuestionPromptDefaults.TemplateKey);
        var version = Read("PromptVersion", AiQuestionPromptDefaults.Version);

        var existing = await _promptTemplateRepository.FirstOrDefaultAsync(x =>
                x.TenantId == context.TenantId &&
                x.TemplateKey == templateKey &&
                x.Version == version);
        if (existing != null)
        {
            return;
        }

        await _promptTemplateRepository.InsertAsync(
            new AiQuestionPromptTemplate(
                _guidGenerator.Create(),
                context.TenantId,
                templateKey,
                version,
                AiQuestionPromptDefaults.SystemPrompt,
                AiQuestionPromptDefaults.UserPromptTemplate,
                AiQuestionPromptDefaults.Description,
                isActive: true),
            autoSave: true);
    }

    private string Read(string key, string fallback)
    {
        var value = _configuration[$"RagAI:AiQuestionGeneration:{key}"];
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
