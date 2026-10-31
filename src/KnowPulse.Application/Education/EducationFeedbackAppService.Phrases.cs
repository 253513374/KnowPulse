using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

public partial class EducationFeedbackAppService
{
    private async Task PhraseScopeAsync(EducationFeedbackPhraseScopeDto input, bool write = false)
    {
        await AuthorizeAsync(true); EducationRules.RequireId(input.SchoolId);
        var teaching = await Access.EnsureTeachingAssignmentAsync(input.TeachingAssignmentId, write);
        if (teaching.SchoolId != input.SchoolId) throw new AbpAuthorizationException();
    }

    private async Task<EducationFeedbackPhrase?> OwnedPhraseAsync(Guid id)
    {
        EducationRules.RequireId(id); var row = await RefAsync<EducationFeedbackPhrase>(id);
        if (row != null && (row.TenantId != CurrentTenant.Id || row.OwnerUserId != CurrentUser.Id)) throw new AbpAuthorizationException();
        return row;
    }
    private static EducationFeedbackPhraseRevisionDto PhraseRevisionDto(EducationFeedbackPhraseRevision row) => new()
    { Id = row.Id, PhraseId = row.PhraseId, RevisionNumber = row.RevisionNumber, Text = row.Text, IsArchived = row.IsArchived, RecordedAtUtc = row.RecordedAtUtc };
    private async Task<EducationFeedbackPhraseDto> PhraseDtoAsync(EducationFeedbackPhrase row)
    {
        var revision = await RefAsync<EducationFeedbackPhraseRevision>(row.CurrentRevisionId ?? throw Invalid()) ?? throw Invalid();
        if (revision.PhraseId != row.Id || revision.OwnerUserId != row.OwnerUserId || revision.TenantId != row.TenantId) throw Invalid();
        return new() { Id = row.Id, ConcurrencyStamp = row.ConcurrencyStamp, CurrentRevision = PhraseRevisionDto(revision) };
    }

    [Authorize(EducationPermissions.FeedbackManage)]
    public virtual async Task<PagedResultDto<EducationFeedbackPhraseDto>> GetPhrasesAsync(EducationFeedbackPhraseScopeDto input, PagedResultRequestDto page, bool includeArchived = false)
    {
        var take = Page(page); await PhraseScopeAsync(input);
        var query = (await Repo<EducationFeedbackPhrase>().GetQueryableAsync()).Where(x => x.TenantId == CurrentTenant.Id && x.OwnerUserId == CurrentUser.Id && (includeArchived || !x.IsArchived));
        var count = await AsyncExecuter.CountAsync(query);
        var rows = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.CreationTime).ThenBy(x => x.Id).Skip(page.SkipCount).Take(take));
        var result = new System.Collections.Generic.List<EducationFeedbackPhraseDto>();
        foreach (var row in rows) result.Add(await PhraseDtoAsync(row));
        return new(count, result);
    }

    [Authorize(EducationPermissions.FeedbackManage)]
    public virtual async Task<EducationFeedbackPhraseDto?> GetPhraseAsync(Guid phraseId, EducationFeedbackPhraseScopeDto input)
    {
        await PhraseScopeAsync(input); var row = await OwnedPhraseAsync(phraseId);
        return row == null ? null : await PhraseDtoAsync(row);
    }

    [Authorize(EducationPermissions.FeedbackManage)]
    public virtual async Task<EducationFeedbackPhraseRevisionDto?> GetPhraseOperationAsync(Guid phraseId, Guid operationId, EducationFeedbackPhraseScopeDto input)
    {
        await PhraseScopeAsync(input); EducationRules.RequireId(operationId);
        if (await OwnedPhraseAsync(phraseId) == null) return null;
        var row = await RefAsync<EducationFeedbackPhraseRevision>(operationId);
        if (row == null) return null;
        if (row.PhraseId != phraseId || row.TenantId != CurrentTenant.Id || row.OwnerUserId != CurrentUser.Id) throw new AbpAuthorizationException();
        return PhraseRevisionDto(row);
    }

    [Authorize(EducationPermissions.FeedbackManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationFeedbackPhraseRevisionDto> SavePhraseAsync(Guid phraseId, SaveEducationFeedbackPhraseDto input)
    {
        EducationRules.RequireId(phraseId); EducationRules.RequireId(input.OperationId);
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        await PhraseScopeAsync(input, write: true);
        var fingerprint = Hash(new { Command = "feedback-phrase", phraseId, input });
        var baseline = await OwnedPhraseAsync(phraseId);
        var previous = await RefAsync<EducationFeedbackPhraseRevision>(input.OperationId);
        if (previous != null)
        {
            if (previous.PhraseId != phraseId || previous.TenantId != CurrentTenant.Id || previous.OwnerUserId != CurrentUser.Id || previous.RequestFingerprint != fingerprint) throw Conflict();
            await uow.CompleteAsync(); return PhraseRevisionDto(previous);
        }
        using var tracking = Repo<EducationFeedbackPhrase>().EnableTracking();
        var item = baseline == null ? new EducationFeedbackPhrase(phraseId, CurrentTenant.Id, CurrentUser.Id!.Value) : await Repo<EducationFeedbackPhrase>().GetAsync(phraseId);
        if (item.CurrentRevisionId != input.ExpectedRevisionId || (baseline == null ? !string.IsNullOrEmpty(input.ConcurrencyStamp) : item.ConcurrencyStamp != input.ConcurrencyStamp)) throw new AbpDbConcurrencyException();
        var revision = new EducationFeedbackPhraseRevision(input.OperationId, item, input.Text, input.IsArchived, fingerprint, DateTime.UtcNow);
        if (baseline == null) await Repo<EducationFeedbackPhrase>().InsertAsync(item, autoSave: true);
        item.Advance(revision);
        await Repo<EducationFeedbackPhraseRevision>().InsertAsync(revision, autoSave: true);
        await Repo<EducationFeedbackPhrase>().UpdateManyAsync([item], autoSave: true);
        await uow.CompleteAsync(); return PhraseRevisionDto(revision);
    }
}
