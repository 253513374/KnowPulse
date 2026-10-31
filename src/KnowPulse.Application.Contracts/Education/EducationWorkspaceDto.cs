using System;
using System.Collections.Generic;

namespace KnowPulse.Education;

public class EducationWorkspaceDto
{
    public Guid AccountUserId { get; set; }
    public Guid? TenantId { get; set; }
    public DateTime ObservedAtUtc { get; set; }
    public List<string> GrantedPermissions { get; set; } = [];
    public List<Guid> ManagedSchoolIds { get; set; } = [];
    public List<EducationRoleWorkspaceDto> Workspaces { get; set; } = [];
}

public class EducationRoleWorkspaceDto
{
    public Guid SchoolId { get; set; }
    public string SchoolName { get; set; } = string.Empty;
    public SchoolMemberRole Role { get; set; }
    public List<EducationWorkspaceClassDto> Classes { get; set; } = [];
}

public class EducationWorkspaceClassDto
{
    public Guid RelationId { get; set; }
    public Guid SchoolClassId { get; set; }
    public string ClassName { get; set; } = string.Empty;
    public int Grade { get; set; }
    public Guid AcademicTermId { get; set; }
    public string AcademicTermName { get; set; } = string.Empty;
    public Guid? EducationSubjectId { get; set; }
    public string? SubjectName { get; set; }
}
