using KnowPulse.KnowledgeIndexing;
using Shouldly;
using Xunit;

namespace KnowPulse.KnowledgeBase;

public class KnowledgeManagementStatusResolverTests
{
    [Theory]
    [InlineData(KnowledgeIndexJobStatus.Pending)]
    [InlineData(KnowledgeIndexJobStatus.Processing)]
    public void Should_Resolve_Publishing_For_Active_Published_Version_In_Progress(
        KnowledgeIndexJobStatus indexStatus)
    {
        Resolve(indexStatus).ShouldBe(KnowledgeManagementStatus.Publishing);
    }

    [Fact]
    public void Should_Resolve_Ready_For_Indexed_Published_Version()
    {
        Resolve(KnowledgeIndexJobStatus.Indexed).ShouldBe(KnowledgeManagementStatus.Ready);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(KnowledgeIndexJobStatus.Failed)]
    [InlineData(KnowledgeIndexJobStatus.Outdated)]
    [InlineData(KnowledgeIndexJobStatus.Deleted)]
    public void Should_Resolve_Needs_Attention_When_Published_Version_Is_Not_Queryable(
        KnowledgeIndexJobStatus? indexStatus)
    {
        Resolve(indexStatus).ShouldBe(KnowledgeManagementStatus.NeedsAttention);
    }

    [Fact]
    public void Should_Resolve_Draft_When_No_Current_Published_Version()
    {
        KnowledgeManagementStatusResolver.Resolve(
                isItemActive: true,
                currentVersionStatus: null,
                latestVersionStatus: KnowledgeVersionStatus.Draft,
                latestIndexStatus: null)
            .ShouldBe(KnowledgeManagementStatus.Draft);
    }

    [Theory]
    [InlineData(KnowledgeVersionStatus.Withdrawn, KnowledgeManagementStatus.Withdrawn)]
    [InlineData(KnowledgeVersionStatus.Archived, KnowledgeManagementStatus.Archived)]
    [InlineData(KnowledgeVersionStatus.Disabled, KnowledgeManagementStatus.Disabled)]
    public void Should_Keep_Terminal_Version_Lifecycle_Visible(
        KnowledgeVersionStatus versionStatus,
        KnowledgeManagementStatus expected)
    {
        KnowledgeManagementStatusResolver.Resolve(
                isItemActive: true,
                currentVersionStatus: null,
                latestVersionStatus: versionStatus,
                latestIndexStatus: null)
            .ShouldBe(expected);
    }

    [Fact]
    public void Should_Prioritize_Disabled_Item()
    {
        KnowledgeManagementStatusResolver.Resolve(
                isItemActive: false,
                currentVersionStatus: KnowledgeVersionStatus.Published,
                latestVersionStatus: KnowledgeVersionStatus.Published,
                latestIndexStatus: KnowledgeIndexJobStatus.Indexed)
            .ShouldBe(KnowledgeManagementStatus.Disabled);
    }

    private static KnowledgeManagementStatus Resolve(KnowledgeIndexJobStatus? indexStatus)
    {
        return KnowledgeManagementStatusResolver.Resolve(
            isItemActive: true,
            currentVersionStatus: KnowledgeVersionStatus.Published,
            latestVersionStatus: KnowledgeVersionStatus.Published,
            latestIndexStatus: indexStatus);
    }
}
