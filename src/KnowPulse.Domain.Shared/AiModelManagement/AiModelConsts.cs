namespace KnowPulse.AiModelManagement;

/// <summary>
/// 模型管理共享长度约束，供领域实体、DTO 和 EF 映射保持一致。
/// </summary>
public static class AiModelConsts
{
    public const int MaxAliasLength = 128;
    public const int MaxUpstreamModelNameLength = 256;
    public const int MaxModelVersionLength = 64;
    public const int MaxEnvironmentVariableLength = 128;
    public const int MaxBaseUrlLength = 256;
    public const int MaxDescriptionLength = 512;
    public const int MaxBusinessModelVersionLength = 64;
    public const int MaxFallbackAliasesJsonLength = 1024;
    public const int MaxFailureReasonLength = 512;
    public const int MaxGatewayModelInfoJsonLength = 4096;
}
