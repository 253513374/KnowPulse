using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace KnowPulse.RagAI;

/// <summary>
/// RAG 质量评测应用服务接口。
/// </summary>
public interface IRagEvaluationAppService : IApplicationService
{
    /// <summary>
    /// 创建 RAG 评测用例。
    /// </summary>
    /// <param name="input">评测用例创建输入。</param>
    /// <returns>创建后的评测用例。</returns>
    [Authorize(KnowPulsePermissions.Rag.Evaluations)]
    Task<RagEvaluationCaseDto> CreateCaseAsync(CreateRagEvaluationCaseDto input);

    /// <summary>
    /// 更新 RAG 评测用例。
    /// </summary>
    /// <param name="id">评测用例 ID。</param>
    /// <param name="input">评测用例更新输入。</param>
    /// <returns>更新后的评测用例。</returns>
    [Authorize(KnowPulsePermissions.Rag.Evaluations)]
    Task<RagEvaluationCaseDto> UpdateCaseAsync(Guid id, UpdateRagEvaluationCaseDto input);

    /// <summary>
    /// 查询单条 RAG 评测用例。
    /// </summary>
    /// <param name="id">评测用例 ID。</param>
    /// <returns>评测用例详情。</returns>
    [Authorize(KnowPulsePermissions.Rag.Evaluations)]
    Task<RagEvaluationCaseDto> GetCaseAsync(Guid id);

    /// <summary>
    /// 分页查询 RAG 评测用例。
    /// </summary>
    /// <param name="input">用例筛选和分页条件。</param>
    /// <returns>评测用例分页结果。</returns>
    [Authorize(KnowPulsePermissions.Rag.Evaluations)]
    Task<PagedResultDto<RagEvaluationCaseDto>> GetCaseListAsync(GetRagEvaluationCaseListInput input);

    /// <summary>
    /// 执行一次 RAG 评测运行。
    /// </summary>
    /// <param name="input">评测运行输入。</param>
    /// <returns>评测运行结果及逐用例结果。</returns>
    [Authorize(KnowPulsePermissions.Rag.Evaluations)]
    Task<RagEvaluationRunDto> RunAsync(RunRagEvaluationDto input);

    /// <summary>
    /// 查询单次 RAG 评测运行详情。
    /// </summary>
    /// <param name="id">评测运行 ID。</param>
    /// <returns>评测运行详情和逐用例结果。</returns>
    [Authorize(KnowPulsePermissions.Rag.Evaluations)]
    Task<RagEvaluationRunDto> GetRunAsync(Guid id);

    /// <summary>
    /// 分页查询 RAG 评测运行记录。
    /// </summary>
    /// <param name="input">运行记录筛选和分页条件。</param>
    /// <returns>评测运行分页结果。</returns>
    [Authorize(KnowPulsePermissions.Rag.Evaluations)]
    Task<PagedResultDto<RagEvaluationRunDto>> GetRunListAsync(GetRagEvaluationRunListInput input);
}

/// <summary>
/// RAG 评测 citation 期望。空字段表示该层级不参与匹配。
/// </summary>
public class RagEvaluationCitationExpectationDto
{
    /// <summary>
    /// 期望或禁止命中的知识条目 ID；为空表示不按知识条目匹配。
    /// </summary>
    public Guid? KnowledgeItemId { get; set; }

    /// <summary>
    /// 期望或禁止命中的知识版本 ID；为空表示不按知识版本匹配。
    /// </summary>
    public Guid? KnowledgeVersionId { get; set; }

    /// <summary>
    /// 期望或禁止命中的 chunk ID；为空表示不按 chunk 匹配。
    /// </summary>
    public Guid? ChunkId { get; set; }
}

/// <summary>
/// RAG 评测权限上下文声明。MVP 阶段只允许声明当前运行主体的上下文，不能用来伪造其他用户。
/// </summary>
public class RagEvaluationPermissionContextDto
{
    /// <summary>
    /// 用例声明的租户 ID；必须与当前租户兼容。
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 用例声明的用户 ID；MVP 阶段只能声明当前运行用户。
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// 用例声明的部门范围。
    /// </summary>
    public List<Guid> DepartmentIds { get; set; } = [];

    /// <summary>
    /// 用例声明的岗位范围。
    /// </summary>
    public List<Guid> PositionIds { get; set; } = [];

    /// <summary>
    /// 用例声明的角色范围。
    /// </summary>
    public List<Guid> RoleIds { get; set; } = [];

    /// <summary>
    /// 用例声明的指定用户范围。
    /// </summary>
    public List<Guid> UserIds { get; set; } = [];

    /// <summary>
    /// 用例声明的安全等级。
    /// </summary>
    public int? SecurityLevel { get; set; }
}

/// <summary>
/// RAG 评测用例 DTO。
/// </summary>
public class RagEvaluationCaseDto : FullAuditedEntityDto<Guid>
{
    /// <summary>
    /// 租户 ID。
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 评测用例名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 待评测问题。
    /// </summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// 是否期望拒答。
    /// </summary>
    public bool ExpectedRefusal { get; set; }

    /// <summary>
    /// 用例是否启用。
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// 权限上下文。
    /// </summary>
    public RagEvaluationPermissionContextDto PermissionContext { get; set; } = new();

    /// <summary>
    /// 期望引用列表。
    /// </summary>
    public IReadOnlyList<RagEvaluationCitationExpectationDto> ExpectedCitations { get; set; } = [];

    /// <summary>
    /// 禁止引用列表，命中后计入越权召回。
    /// </summary>
    public IReadOnlyList<RagEvaluationCitationExpectationDto> ForbiddenCitations { get; set; } = [];

    /// <summary>
    /// 备注。
    /// </summary>
    public string? Notes { get; set; }
}

/// <summary>
/// 创建 RAG 评测用例输入。
/// </summary>
public class CreateRagEvaluationCaseDto
{
    /// <summary>
    /// 评测用例名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 待评测问题。
    /// </summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// 是否期望拒答。
    /// </summary>
    public bool ExpectedRefusal { get; set; }

    /// <summary>
    /// 权限上下文；为空时使用默认空上下文。
    /// </summary>
    public RagEvaluationPermissionContextDto? PermissionContext { get; set; }

    /// <summary>
    /// 期望命中的引用列表。
    /// </summary>
    public List<RagEvaluationCitationExpectationDto> ExpectedCitations { get; set; } = [];

    /// <summary>
    /// 禁止命中的引用列表。
    /// </summary>
    public List<RagEvaluationCitationExpectationDto> ForbiddenCitations { get; set; } = [];

    /// <summary>
    /// 备注。
    /// </summary>
    public string? Notes { get; set; }
}

/// <summary>
/// 更新 RAG 评测用例输入。
/// </summary>
public class UpdateRagEvaluationCaseDto : CreateRagEvaluationCaseDto
{
    /// <summary>
    /// 更新后是否启用该用例。
    /// </summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// RAG 评测用例分页查询输入。
/// </summary>
public class GetRagEvaluationCaseListInput : PagedAndSortedResultRequestDto
{
    /// <summary>
    /// 名称或问题关键字。
    /// </summary>
    public string? Filter { get; set; }

    /// <summary>
    /// 是否只查询启用或停用用例；为空表示不限制。
    /// </summary>
    public bool? IsActive { get; set; }
}

/// <summary>
/// RAG 评测运行输入。
/// </summary>
public class RunRagEvaluationDto
{
    /// <summary>
    /// 运行名称；为空时系统生成默认名称。
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 指定运行的用例 ID 集合；为空时按筛选条件运行所有可运行用例。
    /// </summary>
    public List<Guid> CaseIds { get; set; } = [];

    /// <summary>
    /// 是否仅运行启用用例。
    /// </summary>
    public bool ActiveCasesOnly { get; set; } = true;

    /// <summary>
    /// RAG 检索 TopK。应用服务会限制最大值，避免一次评测拉取过多上下文。
    /// </summary>
    public int TopK { get; set; } = 5;
}

/// <summary>
/// RAG 评测运行分页查询输入。
/// </summary>
public class GetRagEvaluationRunListInput : PagedAndSortedResultRequestDto
{
    /// <summary>
    /// 按运行状态筛选。
    /// </summary>
    public RagEvaluationRunStatus? Status { get; set; }

    /// <summary>
    /// 按是否硬失败筛选。
    /// </summary>
    public bool? HardFailed { get; set; }
}

/// <summary>
/// RAG 评测运行 DTO。
/// </summary>
public class RagEvaluationRunDto : EntityDto<Guid>
{
    /// <summary>
    /// 租户 ID。
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 运行名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 触发评测的用户 ID。
    /// </summary>
    public Guid? OperatorUserId { get; set; }

    /// <summary>
    /// 运行状态。
    /// </summary>
    public RagEvaluationRunStatus Status { get; set; }

    /// <summary>
    /// 开始时间。
    /// </summary>
    public DateTime StartedTime { get; set; }

    /// <summary>
    /// 完成时间。
    /// </summary>
    public DateTime? CompletedTime { get; set; }

    /// <summary>
    /// 用例总数。
    /// </summary>
    public int TotalCaseCount { get; set; }

    /// <summary>
    /// 通过用例数。
    /// </summary>
    public int PassedCaseCount { get; set; }

    /// <summary>
    /// 失败用例数。
    /// </summary>
    public int FailedCaseCount { get; set; }

    /// <summary>
    /// 命中率。
    /// </summary>
    public double HitRate { get; set; }

    /// <summary>
    /// 引用准确率。
    /// </summary>
    public double CitationAccuracyRate { get; set; }

    /// <summary>
    /// 无答案拒答率。
    /// </summary>
    public double NoAnswerRejectionRate { get; set; }

    /// <summary>
    /// 越权召回率。
    /// </summary>
    public double UnauthorizedRecallRate { get; set; }

    /// <summary>
    /// 越权召回数量。
    /// </summary>
    public int UnauthorizedRecallCount { get; set; }

    /// <summary>
    /// 是否硬失败。
    /// </summary>
    public bool HardFailed { get; set; }

    /// <summary>
    /// 失败原因摘要。
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// 逐用例结果。
    /// </summary>
    public IReadOnlyList<RagEvaluationRunCaseResultDto> Results { get; set; } = [];
}

/// <summary>
/// RAG 评测逐用例结果 DTO。
/// </summary>
public class RagEvaluationRunCaseResultDto : EntityDto<Guid>
{
    /// <summary>
    /// 所属评测运行 ID。
    /// </summary>
    public Guid EvaluationRunId { get; set; }

    /// <summary>
    /// 评测用例 ID。
    /// </summary>
    public Guid EvaluationCaseId { get; set; }

    /// <summary>
    /// 问题摘要。
    /// </summary>
    public string QuestionSummary { get; set; } = string.Empty;

    /// <summary>
    /// 是否期望拒答。
    /// </summary>
    public bool ExpectedRefusal { get; set; }

    /// <summary>
    /// 实际是否拒答。
    /// </summary>
    public bool ActualRefusal { get; set; }

    /// <summary>
    /// 期望引用数量。
    /// </summary>
    public int ExpectedCitationCount { get; set; }

    /// <summary>
    /// 实际引用数量。
    /// </summary>
    public int ActualCitationCount { get; set; }

    /// <summary>
    /// 命中期望引用数量。
    /// </summary>
    public int MatchedExpectedCitationCount { get; set; }

    /// <summary>
    /// 越权引用数量。
    /// </summary>
    public int UnauthorizedCitationCount { get; set; }

    /// <summary>
    /// 是否命中期望结果。
    /// </summary>
    public bool Hit { get; set; }

    /// <summary>
    /// 引用准确率。
    /// </summary>
    public double CitationAccuracyRate { get; set; }

    /// <summary>
    /// 当前用例是否通过。
    /// </summary>
    public bool Passed { get; set; }

    /// <summary>
    /// 失败原因。
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// 回答摘要。
    /// </summary>
    public string? AnswerSummary { get; set; }

    /// <summary>
    /// 实际引用列表。
    /// </summary>
    public IReadOnlyList<RagCitation> ActualCitations { get; set; } = [];
}
