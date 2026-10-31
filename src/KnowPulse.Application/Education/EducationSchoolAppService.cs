using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Uow;
using Volo.Abp.Validation;

namespace KnowPulse.Education;

/// <summary>
/// 学校与教学关系基础。功能权限不能替代该校当前有效管理员身份。
/// 关系只允许结束后另建，不提供覆盖关联对象或删除历史的入口。
/// </summary>
[Authorize]
public partial class EducationSchoolAppService : KnowPulseAppService, IEducationSchoolAppService
{
    private readonly IRepository<School, Guid> _schools;
    private readonly IRepository<SchoolMembership, Guid> _memberships;
    private readonly IRepository<AcademicTerm, Guid> _terms;
    private readonly IRepository<SchoolClass, Guid> _classes;
    private readonly IRepository<EducationSubject, Guid> _subjects;
    private readonly IRepository<TeachingAssignment, Guid> _assignments;
    private readonly IRepository<StudentEnrollment, Guid> _enrollments;
    private readonly IRepository<IdentityUser, Guid> _users;

    public EducationSchoolAppService(IRepository<School, Guid> schools,
        IRepository<SchoolMembership, Guid> memberships, IRepository<AcademicTerm, Guid> terms,
        IRepository<SchoolClass, Guid> classes, IRepository<EducationSubject, Guid> subjects,
        IRepository<TeachingAssignment, Guid> assignments, IRepository<StudentEnrollment, Guid> enrollments,
        IRepository<IdentityUser, Guid> users)
    {
        _schools = schools;
        _memberships = memberships;
        _terms = terms;
        _classes = classes;
        _subjects = subjects;
        _assignments = assignments;
        _enrollments = enrollments;
        _users = users;
    }

    public virtual async Task<ListResultDto<MySchoolDto>> GetMySchoolsAsync()
    {
        var userId = await GetAuthenticatedUserIdAsync();
        var tenantId = CurrentTenant.Id;
        var now = DateTime.UtcNow;
        var memberships = await _memberships.GetQueryableAsync();
        var effectiveMemberships = memberships.Where(x => x.TenantId == tenantId && x.UserId == userId &&
            x.EffectiveFromUtc <= now && (!x.EffectiveToUtc.HasValue || now < x.EffectiveToUtc.Value));
        var schools = await _schools.GetQueryableAsync();
        var mySchools = await AsyncExecuter.ToListAsync(schools.Where(x => x.TenantId == tenantId &&
            effectiveMemberships.Any(membership => membership.SchoolId == x.Id)).OrderBy(x => x.Name).ThenBy(x => x.Id));
        var schoolIds = mySchools.Select(x => x.Id).ToList();
        var roles = await AsyncExecuter.ToListAsync(effectiveMemberships.Where(x => schoolIds.Contains(x.SchoolId)));
        return new ListResultDto<MySchoolDto>(mySchools.Select(x => new MySchoolDto
        {
            Id = x.Id, Code = x.Code, Name = x.Name, ConfigurationRevision = x.ConfigurationRevision,
            Roles = roles.Where(membership => membership.SchoolId == x.Id).Select(membership => membership.Role).Distinct().ToList()
        }).ToList());
    }

    [Authorize(EducationPermissions.SchoolCreate)]
    [UnitOfWork]
    public virtual async Task<SchoolDto> CreateSchoolAsync(CreateSchoolDto input)
    {
        await CheckMvpPermissionAsync(EducationPermissions.SchoolCreate);
        var userId = await GetAuthenticatedUserIdAsync();
        var school = new School(GuidGenerator.Create(), CurrentTenant.Id, input.Code, input.Name);
        var schools = await _schools.GetQueryableAsync();
        if (await AsyncExecuter.AnyAsync(schools.Where(x => x.TenantId == school.TenantId && x.Code == school.Code)))
        {
            throw new BusinessException(EducationErrorCodes.DuplicateCode);
        }

        var manager = new SchoolMembership(GuidGenerator.Create(), school.TenantId, school.Id, userId,
            SchoolMemberRole.Manager, DateTime.UtcNow);
        await _schools.InsertAsync(school, autoSave: false);
        await _memberships.InsertAsync(manager, autoSave: true);
        return Map(school);
    }

    [Authorize(EducationPermissions.SchoolManage)]
    public virtual async Task<SchoolDto> RenameSchoolAsync(Guid schoolId, RenameSchoolDto input)
    {
        var school = await GetManagedSchoolAsync(schoolId);
        var name = Check.NotNullOrWhiteSpace(input.Name, nameof(input.Name), School.MaxNameLength).Trim();
        if (school.Name == name)
        {
            return Map(school);
        }

        school.Rename(name);
        await AdvanceSchoolAsync(school);
        return Map(school);
    }

    [Authorize(EducationPermissions.SchoolManage)]
    public virtual async Task<SchoolConfigurationDto> GetSchoolConfigurationAsync(Guid schoolId)
    {
        var school = await GetManagedSchoolAsync(schoolId);
        var terms = await _terms.GetQueryableAsync();
        var classes = await _classes.GetQueryableAsync();
        var subjects = await _subjects.GetQueryableAsync();
        return new SchoolConfigurationDto
        {
            School = Map(school),
            AcademicTerms = (await AsyncExecuter.ToListAsync(terms.Where(x => x.SchoolId == school.Id && x.TenantId == school.TenantId)
                .OrderByDescending(x => x.StartsAtUtc))).Select(Map).ToList(),
            Classes = (await AsyncExecuter.ToListAsync(classes.Where(x => x.SchoolId == school.Id && x.TenantId == school.TenantId)
                .OrderBy(x => x.AcademicTermId).ThenBy(x => x.Grade).ThenBy(x => x.Code))).Select(Map).ToList(),
            Subjects = (await AsyncExecuter.ToListAsync(subjects.Where(x => x.TenantId == school.TenantId).OrderBy(x => x.Code))).Select(Map).ToList()
        };
    }

    [Authorize(EducationPermissions.SchoolManage)]
    public virtual async Task<PagedResultDto<SchoolMembershipDto>> GetMembershipsAsync(Guid schoolId, GetSchoolMembershipListInput input)
    {
        var school = await GetManagedSchoolAsync(schoolId);
        var query = (await _memberships.GetQueryableAsync()).Where(x => x.SchoolId == school.Id && x.TenantId == school.TenantId);
        if (input.UserId.HasValue) query = query.Where(x => x.UserId == input.UserId.Value);
        if (input.Role.HasValue) query = query.Where(x => x.Role == input.Role.Value);
        if (input.EffectiveAtUtc.HasValue)
        {
            var at = EducationRules.RequireUtc(input.EffectiveAtUtc.Value);
            query = query.Where(x => x.EffectiveFromUtc <= at && (!x.EffectiveToUtc.HasValue || at < x.EffectiveToUtc.Value));
        }

        var count = await AsyncExecuter.CountAsync(query);
        var rows = await AsyncExecuter.ToListAsync(query.OrderBy(x => x.UserId).ThenBy(x => x.Role).ThenBy(x => x.EffectiveFromUtc)
            .Skip(input.SkipCount).Take(input.MaxResultCount));
        var names = await GetAccountNamesAsync(rows.Select(x => x.UserId), school.TenantId);
        var result = rows.Select(Map).ToList();
        foreach (var row in result) row.UserDisplayName = names.GetValueOrDefault(row.UserId, string.Empty);
        return new PagedResultDto<SchoolMembershipDto>(count, result);
    }

    [Authorize(EducationPermissions.SchoolManage)]
    public virtual async Task<PagedResultDto<SchoolAccountDto>> GetAccountsAsync(Guid schoolId, GetSchoolAccountListInput input)
    {
        var school = await GetManagedSchoolAsync(schoolId);
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(input, new ValidationContext(input), errors, validateAllProperties: true))
            throw new AbpValidationException("Invalid account search.", errors);
        var keyword = input.Keyword.Trim().ToUpperInvariant();
        var query = (await _users.GetQueryableAsync()).Where(x => x.TenantId == school.TenantId && x.IsActive &&
            (x.NormalizedUserName.Contains(keyword) || (x.Name != null && x.Name.ToUpper().Contains(keyword)) ||
             (x.Surname != null && x.Surname.ToUpper().Contains(keyword))));
        if (input.Role.HasValue)
        {
            var memberships = await _memberships.GetQueryableAsync();
            query = query.Where(x => memberships.Any(membership => membership.TenantId == school.TenantId &&
                membership.SchoolId == school.Id && membership.UserId == x.Id && membership.Role == input.Role.Value));
        }
        var count = await AsyncExecuter.CountAsync(query);
        var users = await AsyncExecuter.ToListAsync(query.OrderBy(x => x.UserName).ThenBy(x => x.Id)
            .Skip(input.SkipCount).Take(input.MaxResultCount));
        return new PagedResultDto<SchoolAccountDto>(count, users.Select(x => new SchoolAccountDto
        {
            Id = x.Id, UserName = x.UserName, DisplayName = GetAccountDisplayName(x)
        }).ToList());
    }

    [Authorize(EducationPermissions.SchoolManage)]
    public virtual async Task<SchoolMembershipDto> AddMembershipAsync(Guid schoolId, AddSchoolMembershipDto input)
    {
        var school = await GetManagedSchoolAsync(schoolId);
        return await AddMembershipAsync(school, input);
    }

    private async Task<SchoolMembershipDto> AddMembershipAsync(School school, AddSchoolMembershipDto input)
    {
        var membership = new SchoolMembership(GuidGenerator.Create(), school.TenantId, school.Id, input.UserId,
            input.Role, input.EffectiveFromUtc, input.EffectiveToUtc);
        await ValidateUserAsync(input.UserId, school.TenantId);
        var query = (await _memberships.GetQueryableAsync()).Where(x => x.TenantId == school.TenantId &&
            x.SchoolId == school.Id && x.UserId == input.UserId && x.Role == input.Role);
        var existing = await AsyncExecuter.FirstOrDefaultAsync(query.Where(x =>
            x.EffectiveFromUtc == membership.EffectiveFromUtc && x.EffectiveToUtc == membership.EffectiveToUtc));
        if (existing != null) return Map(existing);
        var end = membership.EffectiveToUtc ?? DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
        if (await AsyncExecuter.AnyAsync(query.Where(x => x.EffectiveFromUtc < end &&
            (!x.EffectiveToUtc.HasValue || membership.EffectiveFromUtc < x.EffectiveToUtc.Value))))
        {
            throw new BusinessException(EducationErrorCodes.MembershipOverlap);
        }

        await AdvanceSchoolAsync(school);
        return Map(await _memberships.InsertAsync(membership, autoSave: true));
    }

    [Authorize(EducationPermissions.SchoolManage)]
    public virtual async Task<SchoolMemberImportPreviewDto> PreviewMemberImportAsync(Guid schoolId, SchoolMemberImportInput input)
    {
        var school = await GetManagedSchoolAsync(schoolId);
        return await PreviewMemberImportAsync(school, input);
    }

    [Authorize(EducationPermissions.SchoolManage)]
    [UnitOfWork]
    public virtual async Task<SchoolMemberImportResultDto> ImportMembersAsync(Guid schoolId, SchoolMemberImportInput input)
    {
        var school = await GetManagedSchoolAsync(schoolId);
        var preview = await PreviewMemberImportAsync(school, input);
        if (preview.Rows.Any(x => x.ErrorCode != null))
            throw new BusinessException(EducationErrorCodes.ImportContainsErrors);
        if (preview.Rows.Any(x => input.Rows[x.RowNumber - 1].ConfirmedUserId != x.UserId))
            throw new BusinessException(EducationErrorCodes.ImportConfirmationChanged);
        var rows = preview.Rows.Where(x => !x.DuplicateInInput).ToList();
        // A retry after an unknown response can return the same memberships without another write.
        if (rows.Any(x => !x.AlreadyExists) && input.ConfigurationRevision != school.ConfigurationRevision)
            throw new BusinessException(EducationErrorCodes.ImportConfirmationChanged);

        var result = new SchoolMemberImportResultDto { ExistingCount = rows.Count(x => x.AlreadyExists) };
        foreach (var row in rows.Where(x => !x.AlreadyExists))
        {
            await AddMembershipAsync(school, new AddSchoolMembershipDto
            {
                UserId = row.UserId!.Value, Role = row.Role,
                EffectiveFromUtc = input.EffectiveFromUtc, EffectiveToUtc = input.EffectiveToUtc
            });
            result.CreatedCount++;
        }
        return result;
    }

    private async Task<SchoolMemberImportPreviewDto> PreviewMemberImportAsync(School school, SchoolMemberImportInput input)
    {
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(input, new ValidationContext(input), errors, validateAllProperties: true))
            throw new AbpValidationException("Invalid membership import.", errors);
        foreach (var row in input.Rows)
        {
            if (row == null || !Validator.TryValidateObject(row, new ValidationContext(row), errors, validateAllProperties: true))
                throw new AbpValidationException("Invalid membership import row.", errors);
        }
        EducationRules.ValidatePeriod(input.EffectiveFromUtc, input.EffectiveToUtc);
        var names = input.Rows.Select(x => x.UserName.Trim().ToUpperInvariant()).Distinct().ToList();
        var accounts = await AsyncExecuter.ToListAsync((await _users.GetQueryableAsync())
            .Where(x => x.TenantId == school.TenantId && x.IsActive && names.Contains(x.NormalizedUserName)));
        var userIds = accounts.Select(x => x.Id).ToList();
        var memberships = await AsyncExecuter.ToListAsync((await _memberships.GetQueryableAsync())
            .Where(x => x.TenantId == school.TenantId && x.SchoolId == school.Id && userIds.Contains(x.UserId)));
        var result = new SchoolMemberImportPreviewDto { ConfigurationRevision = school.ConfigurationRevision };
        var seen = new HashSet<(Guid, SchoolMemberRole)>();
        for (var index = 0; index < input.Rows.Count; index++)
        {
            var inputRow = input.Rows[index];
            var row = new SchoolMemberImportRowDto
            {
                RowNumber = index + 1, UserName = inputRow.UserName.Trim(), Role = inputRow.Role
            };
            result.Rows.Add(row);
            if (row.Role is not (SchoolMemberRole.Teacher or SchoolMemberRole.Student))
            {
                row.ErrorCode = EducationErrorCodes.InvalidRole;
                continue;
            }
            var account = accounts.FirstOrDefault(x => x.NormalizedUserName == row.UserName.ToUpperInvariant());
            if (account == null)
            {
                row.ErrorCode = EducationErrorCodes.ImportAccountUnavailable;
                continue;
            }
            row.UserId = account.Id;
            row.UserName = account.UserName;
            row.UserDisplayName = GetAccountDisplayName(account);
            row.DuplicateInInput = !seen.Add((account.Id, row.Role));
            var matching = memberships.Where(x => x.UserId == account.Id && x.Role == row.Role).ToList();
            row.AlreadyExists = matching.Any(x => x.EffectiveFromUtc == input.EffectiveFromUtc && x.EffectiveToUtc == input.EffectiveToUtc);
            if (!row.AlreadyExists && matching.Any(x => EducationRules.Overlaps(x.EffectiveFromUtc, x.EffectiveToUtc,
                input.EffectiveFromUtc, input.EffectiveToUtc)))
                row.ErrorCode = EducationErrorCodes.MembershipOverlap;
        }
        return result;
    }

    [Authorize(EducationPermissions.SchoolManage)]
    public virtual async Task<SchoolMembershipDto> EndMembershipAsync(Guid schoolId, Guid membershipId, EndEducationRelationDto input)
    {
        var school = await GetManagedSchoolAsync(schoolId);
        var membership = await _memberships.GetAsync(membershipId);
        EnsureSchoolReference(school, membership.SchoolId, membership.TenantId);
        var end = EducationRules.EndPeriod(membership.EffectiveFromUtc, membership.EffectiveToUtc, input.EffectiveToUtc);
        if (membership.EffectiveToUtc == end) return Map(membership);
        var originalEnd = membership.EffectiveToUtc ?? DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
        if (membership.Role == SchoolMemberRole.Teacher)
        {
            var assignments = await _assignments.GetQueryableAsync();
            if (await AsyncExecuter.AnyAsync(assignments.Where(x => x.TenantId == school.TenantId && x.SchoolId == school.Id &&
                x.TeacherUserId == membership.UserId && x.EffectiveFromUtc < originalEnd &&
                (!x.EffectiveToUtc.HasValue || x.EffectiveToUtc.Value > end))))
                throw new BusinessException(EducationErrorCodes.MembershipHasDependentRelations);
        }
        if (membership.Role == SchoolMemberRole.Student)
        {
            var enrollments = await _enrollments.GetQueryableAsync();
            if (await AsyncExecuter.AnyAsync(enrollments.Where(x => x.TenantId == school.TenantId && x.SchoolId == school.Id &&
                x.StudentUserId == membership.UserId && x.EffectiveFromUtc < originalEnd &&
                (!x.EffectiveToUtc.HasValue || x.EffectiveToUtc.Value > end))))
                throw new BusinessException(EducationErrorCodes.MembershipHasDependentRelations);
        }
        if (membership.Role == SchoolMemberRole.Manager)
        {
            var memberships = await _memberships.GetQueryableAsync();
            if (!await AsyncExecuter.AnyAsync(memberships.Where(x => x.TenantId == school.TenantId && x.SchoolId == school.Id &&
                x.Id != membership.Id && x.Role == SchoolMemberRole.Manager && x.EffectiveFromUtc <= end &&
                (!x.EffectiveToUtc.HasValue || end < x.EffectiveToUtc.Value))))
                throw new BusinessException(EducationErrorCodes.LastManagerRequired);
        }

        await AdvanceSchoolAsync(school);
        membership.EndAt(end);
        return Map(await _memberships.UpdateAsync(membership, autoSave: true));
    }

    [Authorize(EducationPermissions.SchoolManage)]
    public virtual async Task<AcademicTermDto> CreateAcademicTermAsync(Guid schoolId, CreateAcademicTermDto input)
    {
        var school = await GetManagedSchoolAsync(schoolId);
        var term = new AcademicTerm(GuidGenerator.Create(), school.TenantId, school.Id, input.Code, input.Name,
            input.StartsAtUtc, input.EndsAtUtc);
        var terms = await _terms.GetQueryableAsync();
        var existing = await AsyncExecuter.FirstOrDefaultAsync(terms.Where(x => x.SchoolId == school.Id && x.TenantId == school.TenantId && x.Code == term.Code));
        if (existing != null)
        {
            if (existing.Name == term.Name && existing.StartsAtUtc == term.StartsAtUtc && existing.EndsAtUtc == term.EndsAtUtc) return Map(existing);
            throw new BusinessException(EducationErrorCodes.DuplicateCode);
        }

        await AdvanceSchoolAsync(school);
        return Map(await _terms.InsertAsync(term, autoSave: true));
    }

    [Authorize(EducationPermissions.SchoolManage)]
    public virtual async Task<SchoolClassDto> CreateClassAsync(Guid schoolId, CreateSchoolClassDto input)
    {
        var school = await GetManagedSchoolAsync(schoolId);
        var term = await _terms.GetAsync(input.AcademicTermId);
        EnsureSchoolReference(school, term.SchoolId, term.TenantId);
        var schoolClass = new SchoolClass(GuidGenerator.Create(), school.TenantId, school.Id, term.Id, input.Code, input.Name, input.Grade);
        var classes = await _classes.GetQueryableAsync();
        var existing = await AsyncExecuter.FirstOrDefaultAsync(classes.Where(x => x.SchoolId == school.Id && x.TenantId == school.TenantId &&
            x.AcademicTermId == term.Id && x.Code == schoolClass.Code));
        if (existing != null)
        {
            if (existing.Name == schoolClass.Name && existing.Grade == schoolClass.Grade) return Map(existing);
            throw new BusinessException(EducationErrorCodes.DuplicateCode);
        }

        await AdvanceSchoolAsync(school);
        return Map(await _classes.InsertAsync(schoolClass, autoSave: true));
    }

    [Authorize(EducationPermissions.SchoolManage)]
    public virtual async Task<EducationSubjectDto> CreateSubjectAsync(Guid schoolId, CreateEducationSubjectDto input)
    {
        var school = await GetManagedSchoolAsync(schoolId);
        var subject = new EducationSubject(GuidGenerator.Create(), school.TenantId, input.Code, input.Name);
        var subjects = await _subjects.GetQueryableAsync();
        var existing = await AsyncExecuter.FirstOrDefaultAsync(subjects.Where(x => x.TenantId == school.TenantId && x.Code == subject.Code));
        if (existing != null)
        {
            if (existing.Name == subject.Name) return Map(existing);
            throw new BusinessException(EducationErrorCodes.DuplicateCode);
        }

        await AdvanceSchoolAsync(school);
        return Map(await _subjects.InsertAsync(subject, autoSave: true));
    }

    [Authorize(EducationPermissions.SchoolManage)]
    public virtual async Task<PagedResultDto<TeachingAssignmentDto>> GetTeachingAssignmentsAsync(Guid schoolId, GetSchoolRelationListInput input)
    {
        var school = await GetManagedSchoolAsync(schoolId);
        var query = (await _assignments.GetQueryableAsync()).Where(x => x.SchoolId == school.Id && x.TenantId == school.TenantId);
        if (input.SchoolClassId.HasValue) query = query.Where(x => x.SchoolClassId == input.SchoolClassId.Value);
        if (input.UserId.HasValue) query = query.Where(x => x.TeacherUserId == input.UserId.Value);
        if (input.EffectiveAtUtc.HasValue)
        {
            var at = EducationRules.RequireUtc(input.EffectiveAtUtc.Value);
            query = query.Where(x => x.EffectiveFromUtc <= at && (!x.EffectiveToUtc.HasValue || at < x.EffectiveToUtc.Value));
        }
        var count = await AsyncExecuter.CountAsync(query);
        var rows = await AsyncExecuter.ToListAsync(query.OrderBy(x => x.SchoolClassId).ThenBy(x => x.EducationSubjectId)
            .ThenBy(x => x.TeacherUserId).ThenBy(x => x.EffectiveFromUtc).Skip(input.SkipCount).Take(input.MaxResultCount));
        var names = await GetAccountNamesAsync(rows.Select(x => x.TeacherUserId), school.TenantId);
        var result = rows.Select(Map).ToList();
        foreach (var row in result) row.TeacherDisplayName = names.GetValueOrDefault(row.TeacherUserId, string.Empty);
        return new PagedResultDto<TeachingAssignmentDto>(count, result);
    }

    [Authorize(EducationPermissions.SchoolManage)]
    public virtual async Task<TeachingAssignmentDto> AssignTeacherAsync(Guid schoolId, AssignTeacherDto input)
    {
        var school = await GetManagedSchoolAsync(schoolId);
        var (assignment, existing) = await PrepareTeachingAssignmentAsync(school, input);
        if (existing) return Map(assignment);
        await AdvanceSchoolAsync(school);
        return Map(await _assignments.InsertAsync(assignment, autoSave: true));
    }

    private async Task<(TeachingAssignment Assignment, bool Existing)> PrepareTeachingAssignmentAsync(School school, AssignTeacherDto input, Guid? replacing = null)
    {
        var term = await _terms.GetAsync(input.AcademicTermId);
        var schoolClass = await _classes.GetAsync(input.SchoolClassId);
        var subject = await _subjects.GetAsync(input.EducationSubjectId);
        EnsureSchoolReference(school, term.SchoolId, term.TenantId);
        EnsureSchoolReference(school, schoolClass.SchoolId, schoolClass.TenantId);
        if (schoolClass.AcademicTermId != term.Id || subject.TenantId != school.TenantId)
            throw new BusinessException(EducationErrorCodes.ReferenceScopeMismatch);
        await ValidateUserAsync(input.TeacherUserId, school.TenantId);
        var end = ResolveTermEnd(term, input.EffectiveFromUtc, input.EffectiveToUtc);
        var assignment = new TeachingAssignment(GuidGenerator.Create(), school.TenantId, school.Id, term.Id,
            schoolClass.Id, subject.Id, input.TeacherUserId, input.EffectiveFromUtc, end);
        await EnsureMembershipCoversAsync(school, input.TeacherUserId, SchoolMemberRole.Teacher, assignment.EffectiveFromUtc, end);
        var query = (await _assignments.GetQueryableAsync()).Where(x => x.SchoolId == school.Id && x.TenantId == school.TenantId &&
            x.SchoolClassId == schoolClass.Id && x.EducationSubjectId == subject.Id && x.TeacherUserId == input.TeacherUserId);
        query = query.Where(x => x.Id != replacing);
        var existing = await AsyncExecuter.FirstOrDefaultAsync(query.Where(x => x.AcademicTermId == term.Id &&
            x.EffectiveFromUtc == assignment.EffectiveFromUtc && x.EffectiveToUtc == assignment.EffectiveToUtc));
        if (existing != null) return (existing, true);
        if (await AsyncExecuter.AnyAsync(query.Where(x => x.EffectiveFromUtc < end &&
            (!x.EffectiveToUtc.HasValue || assignment.EffectiveFromUtc < x.EffectiveToUtc.Value))))
            throw new BusinessException(EducationErrorCodes.TeachingAssignmentOverlap);

        return (assignment, false);
    }

    [Authorize(EducationPermissions.SchoolManage)]
    public virtual async Task<TeachingAssignmentDto> EndTeachingAssignmentAsync(Guid schoolId, Guid assignmentId, EndEducationRelationDto input)
    {
        var school = await GetManagedSchoolAsync(schoolId);
        var assignment = await _assignments.GetAsync(assignmentId);
        EnsureSchoolReference(school, assignment.SchoolId, assignment.TenantId);
        var end = EducationRules.EndPeriod(assignment.EffectiveFromUtc, assignment.EffectiveToUtc, input.EffectiveToUtc);
        if (assignment.EffectiveToUtc == end) return Map(assignment);
        await AdvanceSchoolAsync(school);
        assignment.EndAt(end);
        return Map(await _assignments.UpdateAsync(assignment, autoSave: true));
    }

    [Authorize(EducationPermissions.SchoolManage)]
    public virtual async Task<PagedResultDto<StudentEnrollmentDto>> GetStudentEnrollmentsAsync(Guid schoolId, GetSchoolRelationListInput input)
    {
        var school = await GetManagedSchoolAsync(schoolId);
        var query = (await _enrollments.GetQueryableAsync()).Where(x => x.SchoolId == school.Id && x.TenantId == school.TenantId);
        if (input.SchoolClassId.HasValue) query = query.Where(x => x.SchoolClassId == input.SchoolClassId.Value);
        if (input.UserId.HasValue) query = query.Where(x => x.StudentUserId == input.UserId.Value);
        if (input.EffectiveAtUtc.HasValue)
        {
            var at = EducationRules.RequireUtc(input.EffectiveAtUtc.Value);
            query = query.Where(x => x.EffectiveFromUtc <= at && (!x.EffectiveToUtc.HasValue || at < x.EffectiveToUtc.Value));
        }
        var count = await AsyncExecuter.CountAsync(query);
        var rows = await AsyncExecuter.ToListAsync(query.OrderBy(x => x.SchoolClassId).ThenBy(x => x.StudentUserId)
            .ThenBy(x => x.EffectiveFromUtc).Skip(input.SkipCount).Take(input.MaxResultCount));
        var names = await GetAccountNamesAsync(rows.Select(x => x.StudentUserId), school.TenantId);
        var result = rows.Select(Map).ToList();
        foreach (var row in result) row.StudentDisplayName = names.GetValueOrDefault(row.StudentUserId, string.Empty);
        return new PagedResultDto<StudentEnrollmentDto>(count, result);
    }

    [Authorize(EducationPermissions.SchoolManage)]
    public virtual async Task<StudentEnrollmentDto> EnrollStudentAsync(Guid schoolId, EnrollStudentDto input)
    {
        var school = await GetManagedSchoolAsync(schoolId);
        var schoolClass = await _classes.GetAsync(input.SchoolClassId);
        EnsureSchoolReference(school, schoolClass.SchoolId, schoolClass.TenantId);
        var term = await _terms.GetAsync(schoolClass.AcademicTermId);
        EnsureSchoolReference(school, term.SchoolId, term.TenantId);
        await ValidateUserAsync(input.StudentUserId, school.TenantId);
        var end = ResolveTermEnd(term, input.EffectiveFromUtc, input.EffectiveToUtc);
        var enrollment = new StudentEnrollment(GuidGenerator.Create(), school.TenantId, school.Id, schoolClass.Id,
            input.StudentUserId, input.EffectiveFromUtc, end);
        await EnsureMembershipCoversAsync(school, input.StudentUserId, SchoolMemberRole.Student, enrollment.EffectiveFromUtc, end);
        var query = (await _enrollments.GetQueryableAsync()).Where(x => x.SchoolId == school.Id && x.TenantId == school.TenantId &&
            x.StudentUserId == input.StudentUserId);
        var existing = await AsyncExecuter.FirstOrDefaultAsync(query.Where(x => x.SchoolClassId == schoolClass.Id &&
            x.EffectiveFromUtc == enrollment.EffectiveFromUtc && x.EffectiveToUtc == enrollment.EffectiveToUtc));
        if (existing != null) return Map(existing);
        if (await AsyncExecuter.AnyAsync(query.Where(x => x.EffectiveFromUtc < end &&
            (!x.EffectiveToUtc.HasValue || enrollment.EffectiveFromUtc < x.EffectiveToUtc.Value))))
            throw new BusinessException(EducationErrorCodes.StudentEnrollmentOverlap);

        await AdvanceSchoolAsync(school);
        return Map(await _enrollments.InsertAsync(enrollment, autoSave: true));
    }

    [Authorize(EducationPermissions.SchoolManage)]
    public virtual async Task<StudentEnrollmentDto> EndStudentEnrollmentAsync(Guid schoolId, Guid enrollmentId, EndEducationRelationDto input)
    {
        var school = await GetManagedSchoolAsync(schoolId);
        var enrollment = await _enrollments.GetAsync(enrollmentId);
        EnsureSchoolReference(school, enrollment.SchoolId, enrollment.TenantId);
        var end = EducationRules.EndPeriod(enrollment.EffectiveFromUtc, enrollment.EffectiveToUtc, input.EffectiveToUtc);
        if (enrollment.EffectiveToUtc == end) return Map(enrollment);
        await AdvanceSchoolAsync(school);
        enrollment.EndAt(end);
        return Map(await _enrollments.UpdateAsync(enrollment, autoSave: true));
    }

    private async Task<School> GetManagedSchoolAsync(Guid schoolId)
    {
        await CheckMvpPermissionAsync(EducationPermissions.SchoolManage);
        var userId = await GetAuthenticatedUserIdAsync();
        var school = await _schools.GetAsync(schoolId);
        if (school.TenantId != CurrentTenant.Id)
            throw new AbpAuthorizationException("The school belongs to another tenant.");
        var now = DateTime.UtcNow;
        var memberships = await _memberships.GetQueryableAsync();
        if (!await AsyncExecuter.AnyAsync(memberships.Where(x => x.SchoolId == school.Id && x.TenantId == school.TenantId &&
            x.UserId == userId && x.Role == SchoolMemberRole.Manager && x.EffectiveFromUtc <= now &&
            (!x.EffectiveToUtc.HasValue || now < x.EffectiveToUtc.Value))))
            throw new AbpAuthorizationException("An effective manager membership in this school is required.");
        return school;
    }

    private async Task<Guid> GetAuthenticatedUserIdAsync()
    {
        if (!CurrentUser.IsAuthenticated || !CurrentUser.Id.HasValue)
            throw new AbpAuthorizationException("Authentication is required.");
        var user = await _users.FindAsync(CurrentUser.Id.Value);
        if (user == null || !user.IsActive || user.TenantId != CurrentTenant.Id)
            throw new AbpAuthorizationException("An active account in the current tenant is required.");
        return user.Id;
    }

    private async Task ValidateUserAsync(Guid userId, Guid? tenantId)
    {
        var user = await _users.GetAsync(EducationRules.RequireId(userId));
        if (user.TenantId != tenantId) throw new BusinessException(EducationErrorCodes.ReferenceScopeMismatch);
        if (!user.IsActive) throw new BusinessException(EducationErrorCodes.InactiveUser);
    }

    private async Task<Dictionary<Guid, string>> GetAccountNamesAsync(IEnumerable<Guid> userIds, Guid? tenantId)
    {
        var ids = userIds.Distinct().ToList();
        var query = await _users.GetQueryableAsync();
        var users = await AsyncExecuter.ToListAsync(query.Where(x => x.TenantId == tenantId && ids.Contains(x.Id)));
        return users.ToDictionary(x => x.Id, GetAccountDisplayName);
    }

    private static string GetAccountDisplayName(IdentityUser user)
    {
        var name = string.Join(" ", new[] { user.Name, user.Surname }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return name.Length == 0 ? user.UserName : $"{name} ({user.UserName})";
    }

    private static void EnsureSchoolReference(School school, Guid referenceSchoolId, Guid? referenceTenantId)
    {
        if (referenceSchoolId != school.Id || referenceTenantId != school.TenantId)
            throw new BusinessException(EducationErrorCodes.ReferenceScopeMismatch);
    }

    private static DateTime ResolveTermEnd(AcademicTerm term, DateTime fromUtc, DateTime? requestedEnd)
    {
        var end = requestedEnd ?? term.EndsAtUtc;
        EducationRules.ValidatePeriod(fromUtc, end);
        if (fromUtc < term.StartsAtUtc || end > term.EndsAtUtc)
            throw new BusinessException(EducationErrorCodes.PeriodOutsideTerm);
        return end;
    }

    private async Task EnsureMembershipCoversAsync(School school, Guid userId, SchoolMemberRole role, DateTime fromUtc, DateTime toUtc)
    {
        var memberships = await _memberships.GetQueryableAsync();
        if (!await AsyncExecuter.AnyAsync(memberships.Where(x => x.SchoolId == school.Id && x.TenantId == school.TenantId &&
            x.UserId == userId && x.Role == role && x.EffectiveFromUtc <= fromUtc &&
            (!x.EffectiveToUtc.HasValue || toUtc <= x.EffectiveToUtc.Value))))
            throw new BusinessException(EducationErrorCodes.MembershipDoesNotCoverPeriod);
    }

    private async Task AdvanceSchoolAsync(School school)
    {
        // ABP 的 ConcurrencyStamp 是并发令牌；所有校内配置写命令都修改同一校记录。
        // 并发校验冲突需重新提交，避免不同班级的并发在班记录都通过旧快照校验。
        school.AdvanceConfigurationRevision();
        await _schools.UpdateAsync(school, autoSave: true);
    }

    private static SchoolDto Map(School x) => new() { Id = x.Id, Code = x.Code, Name = x.Name, ConfigurationRevision = x.ConfigurationRevision };
    private static SchoolMembershipDto Map(SchoolMembership x) => new()
    {
        Id = x.Id, SchoolId = x.SchoolId, UserId = x.UserId, Role = x.Role,
        EffectiveFromUtc = x.EffectiveFromUtc, EffectiveToUtc = x.EffectiveToUtc
    };
    private static AcademicTermDto Map(AcademicTerm x) => new()
    {
        Id = x.Id, SchoolId = x.SchoolId, Code = x.Code, Name = x.Name, StartsAtUtc = x.StartsAtUtc, EndsAtUtc = x.EndsAtUtc
    };
    private static SchoolClassDto Map(SchoolClass x) => new()
    {
        Id = x.Id, SchoolId = x.SchoolId, AcademicTermId = x.AcademicTermId, Code = x.Code, Name = x.Name, Grade = x.Grade
    };
    private static EducationSubjectDto Map(EducationSubject x) => new() { Id = x.Id, Code = x.Code, Name = x.Name };
    private static TeachingAssignmentDto Map(TeachingAssignment x) => new()
    {
        Id = x.Id, SchoolId = x.SchoolId, AcademicTermId = x.AcademicTermId, SchoolClassId = x.SchoolClassId,
        EducationSubjectId = x.EducationSubjectId, TeacherUserId = x.TeacherUserId,
        EffectiveFromUtc = x.EffectiveFromUtc, EffectiveToUtc = x.EffectiveToUtc
    };
    private static StudentEnrollmentDto Map(StudentEnrollment x) => new()
    {
        Id = x.Id, SchoolId = x.SchoolId, SchoolClassId = x.SchoolClassId, StudentUserId = x.StudentUserId,
        EffectiveFromUtc = x.EffectiveFromUtc, EffectiveToUtc = x.EffectiveToUtc
    };
}
