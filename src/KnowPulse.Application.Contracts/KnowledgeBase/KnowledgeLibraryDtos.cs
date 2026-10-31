using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.KnowledgeBase;

public class PersonalKnowledgeUploadPolicyDto
{
    public long MaxFileBytes { get; set; }

    public int MaxFilesPerVersion { get; set; }
}

public class KnowledgeLibraryDto : EntityDto<Guid>
{
    public Guid OwnerUserId { get; set; }

    public string Name { get; set; } = string.Empty;
}

public class GetPersonalKnowledgeItemListInput : PagedResultRequestDto
{
    [StringLength(256)]
    public string? Keyword { get; set; }

    [EnumDataType(typeof(PersonalMaterialType))]
    public PersonalMaterialType? MaterialType { get; set; }

    [Range(1, 9999)]
    public int? MaterialYear { get; set; }

    [StringLength(128)]
    public string? SubjectName { get; set; }

    [StringLength(64)]
    public string? Tag { get; set; }
}

public class PersonalKnowledgeOrganizationInput
{
    [EnumDataType(typeof(PersonalMaterialType))]
    public PersonalMaterialType MaterialType { get; set; }

    [Range(1, 9999)]
    public int? MaterialYear { get; set; }

    [StringLength(128)]
    public string? SubjectName { get; set; }

    [MaxLength(20)]
    public List<string> CustomTags { get; set; } = [];
}

public class UpdatePersonalKnowledgeOrganizationInput : PersonalKnowledgeOrganizationInput
{
    [Required, StringLength(40)]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public class PersonalKnowledgeOrganizationOptionsDto
{
    public List<string> Subjects { get; set; } = [];
    public List<string> Tags { get; set; } = [];
}
