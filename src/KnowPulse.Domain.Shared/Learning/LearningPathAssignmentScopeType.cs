namespace KnowPulse.Learning;

/// <summary>
/// 学习路径分配范围类型。MVP-3 最小闭环只覆盖全员、部门、岗位和职级。
/// </summary>
public enum LearningPathAssignmentScopeType
{
    /// <summary>
    /// 全员可见，目标 ID 必须为空。
    /// </summary>
    All = 0,

    /// <summary>
    /// 按部门分配，目标 ID 指向部门。
    /// </summary>
    Department = 1,

    /// <summary>
    /// 按岗位分配，目标 ID 指向岗位。
    /// </summary>
    Position = 2,

    /// <summary>
    /// 按职级分配，目标 ID 指向职级。
    /// </summary>
    JobLevel = 3
}
