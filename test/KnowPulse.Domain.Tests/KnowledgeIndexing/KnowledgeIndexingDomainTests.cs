using System;
using Shouldly;
using Xunit;

namespace KnowPulse.KnowledgeIndexing;

/// <summary>
/// 知识索引领域对象的纯领域测试。
/// 这些测试不依赖 EF 或应用服务，主要守住任务状态流转、分块输入约束和 embedding 审计状态。
/// </summary>
public class KnowledgeIndexingDomainTests
{
    /// <summary>
    /// 验证索引任务会记录状态流转、失败原因和重试次数。
    /// </summary>
    [Fact]
    public void Index_Job_Should_Record_Status_Transitions_And_Failure()
    {
        // 验证失败次数和 NextRetryTime 的关系：未超过 MaxRetryCount 时保留重试计划，超过后清空。
        var now = DateTime.UtcNow;
        var job = new KnowledgeIndexJob(
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            Guid.NewGuid(),
            KnowledgeIndexJobTriggerType.Publish,
            maxRetryCount: 1);

        job.Status.ShouldBe(KnowledgeIndexJobStatus.Pending);

        job.Start(now);

        job.Status.ShouldBe(KnowledgeIndexJobStatus.Processing);
        job.StartedTime.ShouldBe(now);

        job.MarkFailed("reader", "Unsupported file type", now.AddMinutes(1), now.AddMinutes(5));

        job.Status.ShouldBe(KnowledgeIndexJobStatus.Failed);
        job.FailureStage.ShouldBe("reader");
        job.FailureReason.ShouldBe("Unsupported file type");
        job.RetryCount.ShouldBe(1);
        job.NextRetryTime.ShouldBe(now.AddMinutes(5));

        job.MarkFailed("writer", "Qdrant unavailable", now.AddMinutes(2), now.AddMinutes(6));

        job.RetryCount.ShouldBe(2);
        job.NextRetryTime.ShouldBeNull();
    }

    /// <summary>
    /// 验证索引任务完成后会清除旧失败信息。
    /// </summary>
    [Fact]
    public void Index_Job_Should_Mark_Indexed_And_Clear_Failure()
    {
        // 成功完成索引后必须清理旧失败信息，避免运维页把已恢复任务继续显示为异常。
        var now = DateTime.UtcNow;
        var job = new KnowledgeIndexJob(
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            Guid.NewGuid(),
            KnowledgeIndexJobTriggerType.ManualRebuild);

        job.Start(now);
        job.MarkFailed("chunker", "Bad content", now.AddMinutes(1), now.AddMinutes(2));
        job.MarkIndexed(now.AddMinutes(3));

        job.Status.ShouldBe(KnowledgeIndexJobStatus.Indexed);
        job.FailureStage.ShouldBeNull();
        job.FailureReason.ShouldBeNull();
        job.NextRetryTime.ShouldBeNull();
        job.CompletedTime.ShouldBe(now.AddMinutes(3));
    }

    /// <summary>
    /// 验证 chunk 会拒绝非法序号和非法页码。
    /// </summary>
    [Fact]
    public void Chunk_Should_Reject_Invalid_Sequence_And_Page()
    {
        // sequence 和页码是 citation 排序/定位字段，负数或 0 页会导致后续展示不可解释。
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new KnowledgeChunk(
                Guid.NewGuid(),
                null,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                -1,
                "content",
                "hash",
                "Policy"));

        Should.Throw<ArgumentOutOfRangeException>(() =>
            new KnowledgeChunk(
                Guid.NewGuid(),
                null,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                0,
                "content",
                "hash",
                "Policy",
                sourcePage: 0));
    }

    /// <summary>
    /// 验证 embedding 记录的逻辑删除状态。
    /// </summary>
    [Fact]
    public void Embedding_Record_Should_Track_Deletion()
    {
        // embedding 记录采用逻辑删除，便于重建后保留历史向量来源。
        var now = DateTime.UtcNow;
        var record = new KnowledgeEmbeddingRecord(
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "vector-1",
            "hash",
            1536,
            now);

        record.IsActive.ShouldBeTrue();
        record.IndexedTime.ShouldBe(now);

        record.MarkDeleted(now.AddMinutes(1));

        record.IsActive.ShouldBeFalse();
        record.DeletedTime.ShouldBe(now.AddMinutes(1));
    }

    /// <summary>
    /// 验证停用模型版本时会同时清除默认标记。
    /// </summary>
    [Fact]
    public void Embedding_Model_Version_Should_Deactivate_Default()
    {
        // 停用模型版本时同时清除默认标记，防止后续索引继续选到不可用模型。
        var model = new EmbeddingModelVersion(
            Guid.NewGuid(),
            null,
            "litellm",
            "text-embedding-3-small",
            "2026-06",
            1536,
            "knowledge_chunks_v1",
            isDefault: true);

        model.IsActive.ShouldBeTrue();
        model.IsDefault.ShouldBeTrue();

        model.Deactivate();

        model.IsActive.ShouldBeFalse();
        model.IsDefault.ShouldBeFalse();
    }
}
