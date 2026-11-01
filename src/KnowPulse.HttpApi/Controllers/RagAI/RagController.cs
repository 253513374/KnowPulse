using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using KnowPulse.RagAI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.Controllers.RagAI;

/// <summary>
/// RAG HTTP API。
/// 搜索、问答和审计日志分别使用独立权限，避免普通问答用户读取审计记录或绕过应用服务安全边界。
/// </summary>
[Route("api/ai/rag")]
public class RagController : KnowPulseController, IKnowledgeRetriever, IRagAnswerService, IRagCallLogAppService
{
    private readonly IKnowledgeRetriever _knowledgeRetriever;
    private readonly IRagAnswerService _answerService;
    private readonly IRagCallLogAppService _callLogAppService;

    /// <summary>
    /// 创建 RAG API 控制器。
    /// </summary>
    public RagController(
        IKnowledgeRetriever knowledgeRetriever,
        IRagAnswerService answerService,
        IRagCallLogAppService callLogAppService)
    {
        _knowledgeRetriever = knowledgeRetriever;
        _answerService = answerService;
        _callLogAppService = callLogAppService;
    }

    /// <summary>
    /// 检索当前用户可访问的知识分块。
    /// </summary>
    [Authorize(KnowPulsePermissions.Rag.Search)]
    [HttpPost("search")]
    public Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
        [FromBody] RagSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.CurriculumScope != null || request.SchoolId.HasValue || request.Scope == KnowledgeSearchScope.SchoolShared)
            throw new Volo.Abp.BusinessException("KnowPulse:Education:SchoolWorkflowRetired");
        // HTTP 层只负责稳定 API 路由和权限标记；真正的身份匹配、Qdrant 粗过滤和 PostgreSQL 二次校验在应用服务中完成。
        return _knowledgeRetriever.SearchAsync(request, cancellationToken);
    }

    /// <summary>
    /// 基于授权后的检索上下文生成回答。
    /// </summary>
    [Authorize(KnowPulsePermissions.Rag.Answer)]
    [HttpPost("answer")]
    public Task<RagAnswerResult> AnswerAsync(
        [FromBody] RagAnswerRequest request,
        CancellationToken cancellationToken = default)
    {
        // 问答入口返回回答和 citation，不暴露内部 prompt 或未授权候选 chunk。
        return _answerService.AnswerAsync(request, cancellationToken);
    }

    /// <summary>
    /// 查询 RAG 调用审计日志。
    /// </summary>
    [Authorize(KnowPulsePermissions.Rag.AuditLogs)]
    [HttpGet("call-logs")]
    public Task<PagedResultDto<RagCallLogDto>> GetListAsync([FromQuery] GetRagCallLogListInput input)
    {
        // 审计查询单独授权，避免普通问答用户读取其他用户的调用记录。
        return _callLogAppService.GetListAsync(input);
    }
}
