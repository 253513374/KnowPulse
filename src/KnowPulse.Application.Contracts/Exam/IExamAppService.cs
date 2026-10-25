using System;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace KnowPulse.Exam;

public interface IExamAppService : IApplicationService
{
    [Authorize(KnowPulsePermissions.Exam.Manage)]
    Task<PagedResultDto<ExamPlanDto>> GetPlanListAsync(GetExamPlanListInput input);

    [Authorize(KnowPulsePermissions.Exam.Manage)]
    Task<ExamPlanDto> GetPlanAsync(Guid id);

    [Authorize(KnowPulsePermissions.Exam.Manage)]
    Task<ExamPaperDto> PreviewPaperAsync(GenerateExamPaperDto input);

    [Authorize(KnowPulsePermissions.Exam.Manage)]
    Task<ExamPlanDto> CreatePlanAsync(CreateExamPlanDto input);

    [Authorize(KnowPulsePermissions.Exam.Participate)]
    Task<PagedResultDto<ExamPlanDto>> GetMyPlanListAsync(GetExamPlanListInput input);

    [Authorize(KnowPulsePermissions.Exam.Participate)]
    Task<ExamPlanDto> GetMyPlanAsync(Guid id);

    [Authorize(KnowPulsePermissions.Exam.Manage)]
    Task<ExamPlanResultsDto> GetPlanResultsAsync(Guid id);

    [Authorize(KnowPulsePermissions.Exam.Manage)]
    Task<ExamAttemptDto> GetStudentAttemptAsync(Guid attemptId);

    [Authorize(KnowPulsePermissions.Exam.Participate)]
    Task<ExamAttemptDto> StartAttemptAsync(Guid planId);

    [Authorize(KnowPulsePermissions.Exam.Participate)]
    Task<ExamAttemptDto> GetAttemptAsync(Guid attemptId);

    [Authorize(KnowPulsePermissions.Exam.Participate)]
    Task<ExamAttemptDto> SaveAnswersAsync(Guid attemptId, SaveExamAnswersDto input);

    [Authorize(KnowPulsePermissions.Exam.Participate)]
    Task<ExamAttemptDto> SubmitAttemptAsync(Guid attemptId, SubmitExamAttemptDto input);

    [Authorize(KnowPulsePermissions.Exam.Manage)]
    Task<ExamScoreDto> GradeAttemptAsync(Guid attemptId);

    [Authorize(KnowPulsePermissions.Exam.Results)]
    Task<PagedResultDto<ExamScoreDto>> GetScoreListAsync(GetExamScoreListInput input);
}
