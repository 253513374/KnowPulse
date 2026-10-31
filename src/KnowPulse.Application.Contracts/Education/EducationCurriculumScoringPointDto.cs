using System;

namespace KnowPulse.Education;

/// <summary>从题目/试卷冻结课程范围投影的评分点，不接受客户端名称覆盖。</summary>
public class EducationCurriculumScoringPointDto : QuestionCurriculumPointWeight
{
    public Guid KnowledgePointId { get; set; }
    public Guid KnowledgePointRevisionId { get; set; }
    public string KnowledgePointCode { get; set; } = string.Empty;
    public string KnowledgePointName { get; set; } = string.Empty;
    public string ChapterCode { get; set; } = string.Empty;
    public string ChapterTitle { get; set; } = string.Empty;
}
