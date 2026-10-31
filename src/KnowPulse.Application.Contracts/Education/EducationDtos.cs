using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.Education;

public class SchoolDto : EntityDto<Guid>
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long ConfigurationRevision { get; set; }
}

public class MySchoolDto : SchoolDto
{
    public List<SchoolMemberRole> Roles { get; set; } = [];
}

public class CreateSchoolDto
{
    [Required, StringLength(64)]
    public string Code { get; set; } = string.Empty;
    [Required, StringLength(128)]
    public string Name { get; set; } = string.Empty;
}

public class RenameSchoolDto
{
    [Required, StringLength(128)]
    public string Name { get; set; } = string.Empty;
}

public class SchoolMembershipDto : EntityDto<Guid>
{
    public Guid SchoolId { get; set; }
    public Guid UserId { get; set; }
    public string UserDisplayName { get; set; } = string.Empty;
    public SchoolMemberRole Role { get; set; }
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
}

public class AddSchoolMembershipDto
{
    public Guid UserId { get; set; }
    public SchoolMemberRole Role { get; set; }
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
}

public class EndEducationRelationDto
{
    public DateTime EffectiveToUtc { get; set; }
}

public class SchoolMemberImportInput
{
    [Required, MinLength(1), MaxLength(100)]
    public List<SchoolMemberImportRowInput> Rows { get; set; } = [];
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
    public long? ConfigurationRevision { get; set; }
}

public class SchoolMemberImportRowInput
{
    [Required, StringLength(256)]
    public string UserName { get; set; } = string.Empty;
    public SchoolMemberRole Role { get; set; }
    public Guid? ConfirmedUserId { get; set; }
}

public class SchoolMemberImportPreviewDto
{
    public long ConfigurationRevision { get; set; }
    public List<SchoolMemberImportRowDto> Rows { get; set; } = [];
}

public class SchoolMemberImportRowDto
{
    public int RowNumber { get; set; }
    public string UserName { get; set; } = string.Empty;
    public SchoolMemberRole Role { get; set; }
    public Guid? UserId { get; set; }
    public string UserDisplayName { get; set; } = string.Empty;
    public bool AlreadyExists { get; set; }
    public bool DuplicateInInput { get; set; }
    public string? ErrorCode { get; set; }
}

public class SchoolMemberImportResultDto
{
    public int CreatedCount { get; set; }
    public int ExistingCount { get; set; }
}

public class AcademicTermDto : EntityDto<Guid>
{
    public Guid SchoolId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
}

public class CreateAcademicTermDto
{
    [Required, StringLength(64)]
    public string Code { get; set; } = string.Empty;
    [Required, StringLength(128)]
    public string Name { get; set; } = string.Empty;
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
}

public class SchoolClassDto : EntityDto<Guid>
{
    public Guid SchoolId { get; set; }
    public Guid AcademicTermId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Grade { get; set; }
}

public class CreateSchoolClassDto
{
    public Guid AcademicTermId { get; set; }
    [Required, StringLength(64)]
    public string Code { get; set; } = string.Empty;
    [Required, StringLength(128)]
    public string Name { get; set; } = string.Empty;
    [Range(7, 9)]
    public int Grade { get; set; }
}

public class EducationSubjectDto : EntityDto<Guid>
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class CreateEducationSubjectDto
{
    [Required, StringLength(64)]
    public string Code { get; set; } = string.Empty;
    [Required, StringLength(128)]
    public string Name { get; set; } = string.Empty;
}

public class TeachingAssignmentDto : EntityDto<Guid>
{
    public Guid SchoolId { get; set; }
    public Guid AcademicTermId { get; set; }
    public Guid SchoolClassId { get; set; }
    public Guid EducationSubjectId { get; set; }
    public Guid TeacherUserId { get; set; }
    public string TeacherDisplayName { get; set; } = string.Empty;
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
}

public class AssignTeacherDto
{
    public Guid AcademicTermId { get; set; }
    public Guid SchoolClassId { get; set; }
    public Guid EducationSubjectId { get; set; }
    public Guid TeacherUserId { get; set; }
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
}

public class StudentEnrollmentDto : EntityDto<Guid>
{
    public Guid SchoolId { get; set; }
    public Guid SchoolClassId { get; set; }
    public Guid StudentUserId { get; set; }
    public string StudentDisplayName { get; set; } = string.Empty;
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
}

public class EnrollStudentDto
{
    public Guid SchoolClassId { get; set; }
    public Guid StudentUserId { get; set; }
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
}

public class GetSchoolMembershipListInput : PagedResultRequestDto
{
    public Guid? UserId { get; set; }
    public SchoolMemberRole? Role { get; set; }
    public DateTime? EffectiveAtUtc { get; set; }
}

public class GetSchoolRelationListInput : PagedResultRequestDto
{
    public Guid? SchoolClassId { get; set; }
    public Guid? UserId { get; set; }
    public DateTime? EffectiveAtUtc { get; set; }
}

public class SchoolConfigurationDto
{
    public SchoolDto School { get; set; } = new();
    public List<AcademicTermDto> AcademicTerms { get; set; } = [];
    public List<SchoolClassDto> Classes { get; set; } = [];
    public List<EducationSubjectDto> Subjects { get; set; } = [];
}

public class SchoolAccountDto : EntityDto<Guid>
{
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public class GetSchoolAccountListInput : PagedResultRequestDto
{
    [Required, StringLength(64, MinimumLength = 2)]
    public string Keyword { get; set; } = string.Empty;
    [EnumDataType(typeof(SchoolMemberRole))]
    public SchoolMemberRole? Role { get; set; }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext)) yield return result;
        if (MaxResultCount is < 1 or > 50)
            yield return new ValidationResult("Choose between 1 and 50 accounts per page.", [nameof(MaxResultCount)]);
        if (string.IsNullOrWhiteSpace(Keyword) || Keyword.Trim().Length < 2)
            yield return new ValidationResult("Enter at least two characters.", [nameof(Keyword)]);
    }
}
