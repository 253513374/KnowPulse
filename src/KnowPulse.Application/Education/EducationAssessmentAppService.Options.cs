using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Exam;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

public partial class EducationAssessmentAppService
{
    [Authorize(EducationPermissions.AssessmentManage)]
    public virtual async Task<EducationAssessmentDraftOptionsDto> GetDraftOptionsAsync(GetEducationAssessmentDraftOptionsInput input)
    {
        await TeacherAsync();
        if (input == null || input.SkipCount < 0 || input.MaxResultCount is < 1 or > 100) throw Invalid();
        using var uow = Uow.Begin(requiresNew: true, isTransactional: false);
        var scope = await Curriculum.ResolveAsync(input.Scope);
        var assignment = await AssignmentAsync(scope);
        var term = await ReferenceAsync<AcademicTerm>(assignment.AcademicTermId) ?? throw Invalid();
        var result = new EducationAssessmentDraftOptionsDto
        {
            EarliestStartsAtUtc = term.StartsAtUtc > assignment.EffectiveFromUtc ? term.StartsAtUtc : assignment.EffectiveFromUtc,
            LatestEndsAtUtc = assignment.EffectiveToUtc.HasValue && assignment.EffectiveToUtc.Value < term.EndsAtUtc
                ? assignment.EffectiveToUtc.Value : term.EndsAtUtc
        };
        var rows = await CurrentClassRosterAsync(scope, assignment);
        result.Students = rows.Select(x => new EducationAssessmentRosterStudentDto
        {
            StudentUserId = x.UserId, StudentName = x.Name, AccountName = x.AccountName
        }).ToList();
        using var paperTracking = Repo<ExamPaper>().DisableTracking();
        var papers = await AsyncExecuter.ToListAsync((await Repo<ExamPaper>().GetQueryableAsync())
            .Where(x => x.TenantId == scope.TenantId && x.PersonalOwnerUserId == scope.TeacherUserId &&
                        x.SaveOperationRequestHash != null && x.CurriculumSnapshotJson != null)
            .OrderByDescending(x => x.CreationTime).ThenBy(x => x.Id).Skip(input.SkipCount).Take(input.MaxResultCount + 1));
        result.HasMoreSavedPapers = papers.Count > input.MaxResultCount;
        result.NextPaperSkipCount = checked(input.SkipCount + Math.Min(papers.Count, input.MaxResultCount));
        foreach (var paper in papers.Take(input.MaxResultCount))
        {
            try
            {
                // A frozen owned paper is history; selection for a new task separately checks
                // its exact curriculum, approved definition and current source uses.
                await Papers.EnsureAsync(paper.Id, scope);
                result.SavedPapers.Add(new EducationAssessmentSavedPaperOptionDto
                {
                    Id = paper.Id, Title = paper.Title, QuestionCount = paper.QuestionCount, TotalScore = paper.TotalScore
                });
            }
            catch (AbpAuthorizationException) { }
            catch (BusinessException) { }
        }
        // A relation withdrawn while checking a candidate cannot leave a readable student list.
        await Curriculum.ValidateAsync(scope);
        await uow.CompleteAsync();
        return result;
    }

    private Task<List<EducationClassStudent>> CurrentClassRosterAsync(EducationCurriculumSnapshot scope, TeachingAssignment assignment, List<Guid>? selectedIds = null) =>
        LazyServiceProvider.LazyGetRequiredService<EducationClassRoster>().GetAsync(scope, assignment, selectedIds);
}
