using System;

namespace KnowPulse.KnowledgeBase;

public class PersonalKnowledgeAttachmentOptions
{
    public const string SectionName = "PersonalKnowledge:Attachments";

    // 开发默认值沿用现有上传界面的 20 MiB；上线部署可按存储容量调整。
    public long MaxFileBytes { get; set; } = 20L * 1024 * 1024;

    // 每版本 20 个是开发默认值，并非教育领域的固定业务上限。
    public int MaxFilesPerVersion { get; set; } = 20;

    public void EnsureValid()
    {
        if (MaxFileBytes <= 0 || MaxFilesPerVersion <= 0)
        {
            throw new InvalidOperationException("Personal knowledge attachment limits must be positive.");
        }
    }
}
