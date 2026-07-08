using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KnowPulse.AiModelManagement;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.Controllers.AiModelManagement;

/// <summary>
/// AI 模型目录和用途绑定 HTTP API。
/// </summary>
[Authorize(KnowPulsePermissions.AiModels.Default)]
[Route("api/ai/models")]
public class AiModelsController : KnowPulseController
{
    private readonly IAiModelCatalogAppService _catalogAppService;

    public AiModelsController(IAiModelCatalogAppService catalogAppService)
    {
        _catalogAppService = catalogAppService;
    }

    [Authorize(KnowPulsePermissions.AiModels.View)]
    [HttpGet("overview")]
    public Task<AiModelCatalogOverviewDto> GetOverviewAsync()
    {
        return _catalogAppService.GetOverviewAsync();
    }

    [Authorize(KnowPulsePermissions.AiModels.View)]
    [HttpGet]
    public Task<PagedResultDto<AiModelDeploymentDto>> GetDeploymentsAsync([FromQuery] GetAiModelDeploymentListInput input)
    {
        return _catalogAppService.GetDeploymentsAsync(input);
    }

    [Authorize(KnowPulsePermissions.AiModels.View)]
    [HttpGet("{id:guid}")]
    public Task<AiModelDeploymentDto> GetDeploymentAsync(Guid id)
    {
        return _catalogAppService.GetDeploymentAsync(id);
    }

    [Authorize(KnowPulsePermissions.AiModels.Manage)]
    [HttpPost]
    public Task<AiModelDeploymentDto> CreateDeploymentAsync([FromBody] CreateUpdateAiModelDeploymentDto input)
    {
        return _catalogAppService.CreateDeploymentAsync(input);
    }

    [Authorize(KnowPulsePermissions.AiModels.Manage)]
    [HttpPut("{id:guid}")]
    public Task<AiModelDeploymentDto> UpdateDeploymentAsync(Guid id, [FromBody] CreateUpdateAiModelDeploymentDto input)
    {
        return _catalogAppService.UpdateDeploymentAsync(id, input);
    }

    [Authorize(KnowPulsePermissions.AiModels.Manage)]
    [HttpPut("{id:guid}/active")]
    public Task<AiModelDeploymentDto> SetDeploymentActiveAsync(Guid id, [FromQuery] bool isActive)
    {
        return _catalogAppService.SetDeploymentActiveAsync(id, isActive);
    }

    [Authorize(KnowPulsePermissions.AiModels.SwitchDefaults)]
    [HttpPut("usage-binding")]
    public Task<AiModelUsageBindingDto> SetUsageBindingAsync([FromBody] SetAiModelUsageBindingDto input)
    {
        return _catalogAppService.SetUsageBindingAsync(input);
    }

    [Authorize(KnowPulsePermissions.AiModels.Test)]
    [HttpPost("{id:guid}/test")]
    public Task<AiModelHealthSnapshotDto> TestDeploymentAsync(Guid id)
    {
        return _catalogAppService.TestDeploymentAsync(id);
    }

    [Authorize(KnowPulsePermissions.AiModels.Test)]
    [HttpPost("test-all")]
    public Task<IReadOnlyList<AiModelHealthSnapshotDto>> TestAllAsync()
    {
        return _catalogAppService.TestAllAsync();
    }
}
