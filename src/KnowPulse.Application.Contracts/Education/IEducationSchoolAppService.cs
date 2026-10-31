using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace KnowPulse.Education;

public interface IEducationSchoolAppService : IApplicationService
{
    [Authorize]
    Task<ListResultDto<MySchoolDto>> GetMySchoolsAsync();
    [Authorize]
    Task<EducationWorkspaceDto> GetMyWorkspaceAsync();
    [Authorize] Task<PagedResultDto<MySchoolInvitationDto>> GetMyInvitationsAsync(GetMySchoolInvitationsInput input);
    [Authorize] Task<MySchoolInvitationDto> GetMyInvitationAsync(Guid invitationId);
    [Authorize] Task<MySchoolInvitationDto> RespondToInvitationAsync(Guid invitationId, RespondSchoolInvitationDto input);
    [Authorize] Task<PagedResultDto<MyTeachingRelationDto>> GetMyTeachingRelationsAsync(Guid schoolId, PagedResultRequestDto input);
    [Authorize] Task<PagedResultDto<TeachingRelationRequestDto>> GetMyRelationRequestsAsync(Guid schoolId, PagedResultRequestDto input);
    [Authorize] Task<TeachingRelationRequestDto> GetMyRelationRequestAsync(Guid requestId);
    [Authorize] Task<TeachingRelationRequestDto> SubmitRelationRequestAsync(Guid schoolId, SubmitTeachingRelationRequestDto input);
    [Authorize] Task<TeachingRelationRequestDto> WithdrawMyRelationRequestAsync(Guid requestId, WithdrawTeachingRelationRequestDto input);
    [Authorize(EducationPermissions.SchoolManage)] Task<PagedResultDto<TeachingRelationRequestDto>> GetRelationRequestsAsync(Guid schoolId, PagedResultRequestDto input);
    [Authorize(EducationPermissions.SchoolManage)] Task<TeachingRelationRequestDto> GetRelationRequestAsync(Guid schoolId, Guid requestId);
    [Authorize(EducationPermissions.SchoolManage)] Task<TeachingRelationRequestDto> ResolveRelationRequestAsync(Guid schoolId, Guid requestId, ResolveTeachingRelationRequestDto input);
    [Authorize(EducationPermissions.SchoolManage)] Task<PagedResultDto<SchoolInvitationDto>> GetInvitationsAsync(Guid schoolId, PagedResultRequestDto input);
    [Authorize(EducationPermissions.SchoolManage)] Task<SchoolInvitationDto> GetInvitationAsync(Guid schoolId, Guid invitationId);
    [Authorize(EducationPermissions.SchoolManage)] Task<SchoolInvitationDto> CreateInvitationAsync(Guid schoolId, CreateSchoolInvitationDto input);
    [Authorize(EducationPermissions.SchoolManage)] Task<SchoolInvitationDto> RevokeInvitationAsync(Guid schoolId, Guid invitationId, RevokeSchoolInvitationDto input);
    [Authorize(EducationPermissions.SchoolCreate)]
    Task<SchoolDto> CreateSchoolAsync(CreateSchoolDto input);
    [Authorize(EducationPermissions.SchoolManage)]
    Task<SchoolDto> RenameSchoolAsync(Guid schoolId, RenameSchoolDto input);
    [Authorize(EducationPermissions.SchoolManage)]
    Task<SchoolConfigurationDto> GetSchoolConfigurationAsync(Guid schoolId);
    [Authorize(EducationPermissions.SchoolManage)]
    Task<PagedResultDto<SchoolMembershipDto>> GetMembershipsAsync(Guid schoolId, GetSchoolMembershipListInput input);
    [Authorize(EducationPermissions.SchoolManage)]
    Task<PagedResultDto<SchoolAccountDto>> GetAccountsAsync(Guid schoolId, GetSchoolAccountListInput input);
    [Authorize(EducationPermissions.SchoolManage)]
    Task<SchoolMembershipDto> AddMembershipAsync(Guid schoolId, AddSchoolMembershipDto input);
    [Authorize(EducationPermissions.SchoolManage)]
    Task<SchoolMemberImportPreviewDto> PreviewMemberImportAsync(Guid schoolId, SchoolMemberImportInput input);
    [Authorize(EducationPermissions.SchoolManage)]
    Task<SchoolMemberImportResultDto> ImportMembersAsync(Guid schoolId, SchoolMemberImportInput input);
    [Authorize(EducationPermissions.SchoolManage)]
    Task<SchoolMembershipDto> EndMembershipAsync(Guid schoolId, Guid membershipId, EndEducationRelationDto input);
    [Authorize(EducationPermissions.SchoolManage)]
    Task<AcademicTermDto> CreateAcademicTermAsync(Guid schoolId, CreateAcademicTermDto input);
    [Authorize(EducationPermissions.SchoolManage)]
    Task<SchoolClassDto> CreateClassAsync(Guid schoolId, CreateSchoolClassDto input);
    [Authorize(EducationPermissions.SchoolManage)]
    Task<EducationSubjectDto> CreateSubjectAsync(Guid schoolId, CreateEducationSubjectDto input);
    [Authorize(EducationPermissions.SchoolManage)]
    Task<PagedResultDto<TeachingAssignmentDto>> GetTeachingAssignmentsAsync(Guid schoolId, GetSchoolRelationListInput input);
    [Authorize(EducationPermissions.SchoolManage)]
    Task<TeachingAssignmentDto> AssignTeacherAsync(Guid schoolId, AssignTeacherDto input);
    [Authorize(EducationPermissions.SchoolManage)]
    Task<TeachingAssignmentDto> EndTeachingAssignmentAsync(Guid schoolId, Guid assignmentId, EndEducationRelationDto input);
    [Authorize(EducationPermissions.SchoolManage)]
    Task<PagedResultDto<StudentEnrollmentDto>> GetStudentEnrollmentsAsync(Guid schoolId, GetSchoolRelationListInput input);
    [Authorize(EducationPermissions.SchoolManage)]
    Task<StudentEnrollmentDto> EnrollStudentAsync(Guid schoolId, EnrollStudentDto input);
    [Authorize(EducationPermissions.SchoolManage)]
    Task<StudentEnrollmentDto> EndStudentEnrollmentAsync(Guid schoolId, Guid enrollmentId, EndEducationRelationDto input);
}
