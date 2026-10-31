using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace KnowPulse.Education;

public interface IEducationCurriculumAppService : IApplicationService
{
    [Authorize]
    Task<CurriculumContextDto> GetContextAsync(Guid schoolId);
    [Authorize]
    Task<PagedResultDto<TextbookVersionDto>> GetTextbooksAsync(Guid schoolId, GetTextbookListInput input);
    [Authorize]
    Task<TextbookVersionDto> GetTextbookAsync(Guid id);
    [Authorize(EducationPermissions.CurriculumManage)] Task<TextbookVersionDto> CreateDraftAsync(CreateTextbookDraftDto input);
    [Authorize(EducationPermissions.CurriculumManage)] Task<TextbookVersionDto> ReviseAsync(Guid id, ReviseTextbookDto input);
    [Authorize(EducationPermissions.CurriculumManage)] Task<TextbookVersionDto> SaveDraftAsync(Guid id, SaveTextbookDraftDto input);
    [Authorize(EducationPermissions.CurriculumManage)] Task<TextbookVersionDto> PublishAsync(Guid id, string concurrencyStamp);
    [Authorize(EducationPermissions.CurriculumManage)] Task<TextbookVersionDto> RetireAsync(Guid id, string concurrencyStamp);
    [Authorize]
    Task<PagedResultDto<CourseKnowledgePointDto>> GetKnowledgePointsAsync(Guid schoolId, Guid subjectId, PagedResultRequestDto input);
    [Authorize(EducationPermissions.CurriculumManage)] Task<CourseKnowledgePointDto> CreateKnowledgePointAsync(CreateCourseKnowledgePointDto input);
    [Authorize(EducationPermissions.CurriculumManage)] Task<CourseKnowledgePointDto> CreateKnowledgePointRevisionAsync(Guid id, CreateKnowledgePointRevisionDto input);
    [Authorize]
    Task<ListResultDto<TeacherTextbookSelectionDto>> GetMySelectionsAsync(Guid schoolId);
    [Authorize]
    Task<TeacherTextbookSelectionDto> SelectTextbookAsync(Guid assignmentId, SelectTeacherTextbookDto input);
}
