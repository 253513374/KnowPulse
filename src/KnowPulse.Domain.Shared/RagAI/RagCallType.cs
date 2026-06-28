namespace KnowPulse.RagAI;

/// <summary>
/// RAG 调用类型。
/// </summary>
public enum RagCallType
{
    /// <summary>
    /// 只执行检索并返回授权后的知识分块。
    /// </summary>
    Search = 0,

    /// <summary>
    /// 基于检索结果生成回答并写入 citation 审计链路。
    /// </summary>
    Answer = 1
}
