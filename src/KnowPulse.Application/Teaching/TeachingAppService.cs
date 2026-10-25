using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;

namespace KnowPulse.Teaching;

[Authorize]
public class TeachingAppService : KnowPulseAppService, ITeachingAppService
{
    private readonly IRepository<TeacherInvitation, Guid> _invitations;
    private readonly IRepository<TeachingRelationship, Guid> _relationships;
    private readonly IRepository<IdentityUser, Guid> _users;

    public TeachingAppService(IRepository<TeacherInvitation, Guid> invitations,
        IRepository<TeachingRelationship, Guid> relationships, IRepository<IdentityUser, Guid> users)
    {
        _invitations = invitations;
        _relationships = relationships;
        _users = users;
    }

    public virtual async Task<TeacherInvitationDto> GetInvitationAsync()
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Exam.Manage);
        var teacherId = CurrentUser.Id!.Value;
        var invitation = await _invitations.FindAsync(x => x.TeacherId == teacherId);
        invitation ??= await _invitations.InsertAsync(new TeacherInvitation(
            GuidGenerator.Create(), CurrentTenant.Id, teacherId, RandomNumberGenerator.GetHexString(12)), autoSave: true);
        return new TeacherInvitationDto
        {
            TeacherId = teacherId,
            TeacherName = await GetTeacherNameAsync(teacherId),
            InvitationCode = invitation.InvitationCode
        };
    }

    public virtual async Task<PagedResultDto<TeachingStudentDto>> GetStudentsAsync(GetTeachingStudentListInput input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Exam.Manage);
        var query = (await _relationships.GetQueryableAsync()).Where(x => x.TeacherId == CurrentUser.Id);
        if (input.Status.HasValue) query = query.Where(x => x.Status == input.Status.Value);
        if (!string.IsNullOrWhiteSpace(input.Keyword)) query = query.Where(x => x.DisplayName.Contains(input.Keyword.Trim()));
        var count = await AsyncExecuter.CountAsync(query);
        var relationships = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.CreationTime)
            .Skip(input.SkipCount).Take(input.MaxResultCount));
        var teacherName = await GetTeacherNameAsync(CurrentUser.Id!.Value);
        return new PagedResultDto<TeachingStudentDto>(count, relationships.Select(x => Map(x, teacherName)).ToList());
    }

    public virtual async Task<TeacherInvitationDto> LookupInvitationAsync(string code)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Exam.Participate);
        code = Check.NotNullOrWhiteSpace(code, nameof(code), 32).Trim().ToUpperInvariant();
        var invitation = await _invitations.FindAsync(x => x.InvitationCode == code)
            ?? throw new UserFriendlyException("邀请码无效，请向老师核对。");
        return new TeacherInvitationDto { TeacherId = invitation.TeacherId,
            TeacherName = await GetTeacherNameAsync(invitation.TeacherId), InvitationCode = invitation.InvitationCode };
    }

    public virtual async Task<TeachingStudentDto> ApplyAsync(ApplyTeacherInvitationDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Exam.Participate);
        var code = Check.NotNullOrWhiteSpace(input.InvitationCode, nameof(input.InvitationCode), 32).Trim().ToUpperInvariant();
        var name = Check.NotNullOrWhiteSpace(input.DisplayName, nameof(input.DisplayName), 64).Trim();
        var invitation = await _invitations.FindAsync(x => x.InvitationCode == code)
            ?? throw new UserFriendlyException("邀请码无效，请向老师核对。");
        if (invitation.TeacherId == CurrentUser.Id) throw new UserFriendlyException("不能申请加入自己的学生名单。");
        var relationship = await _relationships.FindAsync(x => x.TeacherId == invitation.TeacherId && x.StudentId == CurrentUser.Id);
        if (relationship == null)
            relationship = await _relationships.InsertAsync(new TeachingRelationship(GuidGenerator.Create(),
                CurrentTenant.Id, invitation.TeacherId, CurrentUser.Id!.Value, name), autoSave: true);
        else
        {
            relationship.Apply(name);
            await _relationships.UpdateAsync(relationship, autoSave: true);
        }
        return Map(relationship, await GetTeacherNameAsync(relationship.TeacherId));
    }

    public virtual async Task<TeachingStudentDto> ConfirmAsync(Guid id)
    {
        var relationship = await GetOwnedRelationshipAsync(id);
        relationship.Confirm();
        await _relationships.UpdateAsync(relationship, autoSave: true);
        return Map(relationship, await GetTeacherNameAsync(relationship.TeacherId));
    }

    public virtual async Task<TeachingStudentDto> RemoveAsync(Guid id)
    {
        var relationship = await GetOwnedRelationshipAsync(id);
        relationship.Remove();
        await _relationships.UpdateAsync(relationship, autoSave: true);
        return Map(relationship, await GetTeacherNameAsync(relationship.TeacherId));
    }

    public virtual async Task<ListResultDto<TeachingStudentDto>> GetMyTeachersAsync()
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Exam.Participate);
        var query = (await _relationships.GetQueryableAsync()).Where(x => x.StudentId == CurrentUser.Id);
        var relationships = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.CreationTime));
        var teacherIds = relationships.Select(x => x.TeacherId).Distinct().ToList();
        var users = await AsyncExecuter.ToListAsync((await _users.GetQueryableAsync()).Where(x => teacherIds.Contains(x.Id)));
        return new ListResultDto<TeachingStudentDto>(relationships.Select(x => Map(x,
            users.FirstOrDefault(u => u.Id == x.TeacherId)?.Name
            ?? users.FirstOrDefault(u => u.Id == x.TeacherId)?.UserName ?? "教师")).ToList());
    }

    private async Task<TeachingRelationship> GetOwnedRelationshipAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Exam.Manage);
        var relationship = await _relationships.GetAsync(id);
        if (relationship.TeacherId != CurrentUser.Id) throw new AbpAuthorizationException("只能维护自己的学生名单。");
        return relationship;
    }

    private async Task<string> GetTeacherNameAsync(Guid id)
    {
        var user = await _users.FindAsync(id);
        return user?.Name ?? user?.UserName ?? "教师";
    }

    private static TeachingStudentDto Map(TeachingRelationship relationship, string teacherName) => new()
    {
        Id = relationship.Id, TeacherId = relationship.TeacherId, TeacherName = teacherName,
        StudentId = relationship.StudentId, DisplayName = relationship.DisplayName,
        Status = relationship.Status, CreationTime = relationship.CreationTime
    };
}
