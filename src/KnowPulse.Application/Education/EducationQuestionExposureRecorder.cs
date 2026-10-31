using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Exam;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Volo.Abp.Users;

namespace KnowPulse.Education;

/// <summary>Called only after student authorization and the shared School/account write barrier.</summary>
[RemoteService(false)]
public class EducationQuestionExposureRecorder : ITransientDependency
{
    private readonly IAbpLazyServiceProvider _services;
    public EducationQuestionExposureRecorder(IAbpLazyServiceProvider services) => _services = services;
    private IRepository<T, Guid> Repo<T>() where T : class, IEntity<Guid> => _services.LazyGetRequiredService<IRepository<T, Guid>>();
    private async Task<T?> RefAsync<T>(Guid id) where T : class, IEntity<Guid>
    { using var tracking = Repo<T>().DisableTracking(); return await Repo<T>().FindAsync(id); }

    public virtual async Task RecordAsync(EducationAssessmentRecipient recipient, EducationAssessmentResultRelease release,
        EducationStudentFeedbackRevision? feedback = null, Guid? paperQuestionId = null)
    {
        if (_services.LazyGetRequiredService<IUnitOfWorkManager>().Current == null) throw new InvalidOperationException("Exposure recording requires the caller's business unit of work.");
        var user = _services.LazyGetRequiredService<ICurrentUser>(); var tenant = _services.LazyGetRequiredService<ICurrentTenant>().Id;
        if (!user.IsAuthenticated || user.Id != recipient.StudentUserId || recipient.TenantId != tenant || release.TenantId != tenant) throw new AbpAuthorizationException();
        var task = await RefAsync<EducationAssessmentTask>(recipient.TaskId) ?? throw Invalid();
        var revision = await RefAsync<EducationAssessmentTaskRevision>(recipient.TaskRevisionId) ?? throw Invalid();
        var paper = await RefAsync<ExamPaper>(revision.ExamPaperId) ?? throw Invalid();
        if (task.TenantId != tenant || task.PublishedRevisionId != revision.Id || task.CurrentResultReleaseId != release.Id ||
            revision.TenantId != tenant || revision.TaskId != task.Id || paper.TenantId != tenant) throw Invalid();
        var scope = JsonSerializer.Deserialize<EducationCurriculumSnapshot>(revision.CurriculumSnapshotJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw Invalid();
        if (scope.TenantId != tenant || scope.SchoolId != revision.SchoolId || scope.TeacherUserId != revision.OwnerUserId || scope.EducationSubjectId == Guid.Empty) throw Invalid();
        if (feedback != null)
        {
            var current = await RefAsync<EducationStudentFeedback>(feedback.FeedbackId) ?? throw Invalid();
            using var linksTracking = Repo<EducationFeedbackAssessment>().DisableTracking();
            if (current.TenantId != tenant || current.StudentUserId != user.Id || current.PublishedRevisionId != feedback.Id ||
                !await _services.LazyGetRequiredService<IAsyncQueryableExecuter>().AnyAsync((await Repo<EducationFeedbackAssessment>().GetQueryableAsync())
                    .Where(x => x.FeedbackRevisionId == feedback.Id && x.RecipientId == recipient.Id && x.TaskId == task.Id && x.TenantId == tenant))) throw Invalid();
        }
        using var questionsTracking = Repo<ExamPaperQuestion>().DisableTracking();
        var questions = await Repo<ExamPaperQuestion>().GetListAsync(x => x.PaperId == paper.Id && x.TenantId == tenant);
        if (questions.Count == 0 || questions.Count != paper.QuestionCount) throw Invalid();
        if (paperQuestionId.HasValue) {
            if (feedback != null || !questions.Any(x => x.Id == paperQuestionId.Value)) throw Invalid();
            questions = questions.Where(x => x.Id == paperQuestionId.Value).ToList();
        }
        foreach (var question in questions.OrderBy(x => x.Id))
        {
            var id = EducationQuestionExposure.ExposureId(recipient.Id, release.Id, question.Id, feedback?.Id);
            if (await RefAsync<EducationQuestionExposure>(id) != null) continue;
            await Repo<EducationQuestionExposure>().InsertAsync(new(revision, recipient, release, question, scope.EducationSubjectId, DateTime.UtcNow, feedback), autoSave: true);
        }
    }
    private static BusinessException Invalid() => new(EducationLearningErrorCodes.InvalidEvidence);
}
