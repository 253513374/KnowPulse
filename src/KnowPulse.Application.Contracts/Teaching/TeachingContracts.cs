using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace KnowPulse.Teaching;

public interface ITeachingAppService : IApplicationService
{
    [Authorize(KnowPulsePermissions.Exam.Manage)]
    Task<TeacherInvitationDto> GetInvitationAsync();
    [Authorize(KnowPulsePermissions.Exam.Participate)]
    Task<TeacherInvitationDto> LookupInvitationAsync(string code);
    [Authorize(KnowPulsePermissions.Exam.Manage)]
    Task<PagedResultDto<TeachingStudentDto>> GetStudentsAsync(GetTeachingStudentListInput input);
    [Authorize(KnowPulsePermissions.Exam.Participate)]
    Task<TeachingStudentDto> ApplyAsync(ApplyTeacherInvitationDto input);
    [Authorize(KnowPulsePermissions.Exam.Manage)]
    Task<TeachingStudentDto> ConfirmAsync(Guid id);
    [Authorize(KnowPulsePermissions.Exam.Manage)]
    Task<TeachingStudentDto> RemoveAsync(Guid id);
    [Authorize(KnowPulsePermissions.Exam.Participate)]
    Task<ListResultDto<TeachingStudentDto>> GetMyTeachersAsync();
}

public class TeacherInvitationDto
{
    public Guid TeacherId { get; set; }
    public string TeacherName { get; set; } = string.Empty;
    public string InvitationCode { get; set; } = string.Empty;
}

public class TeachingStudentDto : EntityDto<Guid>
{
    public Guid TeacherId { get; set; }
    public string TeacherName { get; set; } = string.Empty;
    public Guid StudentId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public TeachingRelationshipStatus Status { get; set; }
    public DateTime CreationTime { get; set; }
}

public class GetTeachingStudentListInput : PagedAndSortedResultRequestDto
{
    public TeachingRelationshipStatus? Status { get; set; }
    public string? Keyword { get; set; }
}

public class ApplyTeacherInvitationDto
{
    [Required, StringLength(32)]
    public string InvitationCode { get; set; } = string.Empty;

    [Required, StringLength(64)]
    public string DisplayName { get; set; } = string.Empty;
}
