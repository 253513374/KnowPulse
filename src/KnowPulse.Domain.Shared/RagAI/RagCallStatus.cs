namespace KnowPulse.RagAI;

/// <summary>
/// RAG 调用结果状态。
/// </summary>
public enum RagCallStatus
{
    /// <summary>
    /// 调用成功，并且至少完成了对应类型的业务动作。
    /// </summary>
    Succeeded = 0,

    /// <summary>
    /// 没有可用上下文，例如空输入、无向量命中或命中均被二次权限校验拒绝。
    /// </summary>
    NoContext = 1,

    /// <summary>
    /// 当前身份不能执行该检索或问答请求。
    /// </summary>
    Denied = 2,

    /// <summary>
    /// 模型、向量库、日志或其他运行时依赖失败。
    /// </summary>
    Failed = 3
}
