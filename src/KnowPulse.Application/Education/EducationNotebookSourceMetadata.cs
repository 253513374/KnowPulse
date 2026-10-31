using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Exam;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

internal sealed record EducationNotebookSourceMetadata(Guid SourceVersionId, string Title, Guid SchoolId);

public partial class EducationAssessmentAppService
{
    [RemoteService(false)]
    internal async Task<EducationNotebookSourceMetadata> NotebookQuestionSourceAsync(Guid recipientId, Guid questionId)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: false);
        var result = await MyResultInCurrentUnitOfWorkAsync(recipientId, acquireReviewBarrier: false, recordExposure: false);
        if (result.CurrentResultVisibility != ExamResultVisibility.FullReview) throw new AbpAuthorizationException();
        var question = result.Questions.SingleOrDefault(x => x.Id == questionId) ?? throw new AbpAuthorizationException();
        var recipient = await StudentRecipientAsync(recipientId);
        var metadata = new EducationNotebookSourceMetadata(recipient.TaskRevisionId, question.Title, recipient.SchoolId);
        await uow.CompleteAsync(); return metadata;
    }
}

public partial class EducationStudyTaskAppService
{
    [RemoteService(false)]
    internal async Task<EducationNotebookSourceMetadata> NotebookMaterialSourceAsync(Guid recipientId, Guid materialId)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: false);
        var (_, _, publication) = await RecipientAsync(recipientId, true, acquireBarrier: false);
        var (task, material) = await MaterialContextAsync(recipientId, Guid.Empty, materialId, true, acquireBarrier: false);
        if (!await MaterialDelivery.AvailableAsync(Authority(task), material)) throw new AbpAuthorizationException();
        var metadata = new EducationNotebookSourceMetadata(publication.Id, material.Title, task.SchoolId);
        await uow.CompleteAsync(); return metadata;
    }
}
