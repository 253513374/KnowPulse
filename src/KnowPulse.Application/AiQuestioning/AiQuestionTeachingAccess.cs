using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KnowPulse.KnowledgeBase;
using Volo.Abp;

namespace KnowPulse.AiQuestioning;

public partial class AiQuestionGenerationAppService
{
    private async Task<AiQuestionGenerationTask> GetOwnedTaskAsync(Guid id)
    {
        var task = await _taskRepository.GetAsync(id);
        EnsureTeachingOwner(task.OwnerTeacherId);
        return task;
    }

    private async Task EnsureSourcesAvailableAsync(IReadOnlyCollection<Guid> ids)
    {
        if (ids.Count > 50) throw new UserFriendlyException("单次最多选择50份资料。");
        var management = LazyServiceProvider.LazyGetRequiredService<IKnowledgeManagementAppService>();
        foreach (var id in ids)
        {
            var item = await management.GetAsync(id);
            if (item.Status != KnowledgeManagementStatus.Ready)
                throw new UserFriendlyException("所选资料尚未处理完成，请等待完成后再出题。");
        }
    }
}
