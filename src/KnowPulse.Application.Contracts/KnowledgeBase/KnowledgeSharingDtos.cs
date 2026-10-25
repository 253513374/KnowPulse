using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.KnowledgeBase;

public class KnowledgeShareTeacherDto : EntityDto<Guid>
{
    public string DisplayName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
}

public class ShareKnowledgeWithTeachersDto
{
    [MaxLength(100)]
    public List<Guid> TeacherIds { get; set; } = [];
}
