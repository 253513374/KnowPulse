using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using KnowPulse.Blazor.Client.Pages.Teaching;
using KnowPulse.KnowledgeBase;
using NSubstitute;
using Shouldly;
using Volo.Abp.Application.Dtos;
using Xunit;

namespace KnowPulse.Blazor;

public class MaterialsRefreshTests
{
    [Fact]
    public async Task Should_Refresh_The_Open_Detail_And_Attachments_When_Processing_Completes()
    {
        var id = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var management = Substitute.For<IKnowledgeManagementAppService>();
        var knowledge = Substitute.For<IKnowledgeItemAppService>();
        management.GetAsync(id).Returns(
            new KnowledgeManagementDetailDto { Id = id, Title = "资料", Status = KnowledgeManagementStatus.Publishing },
            new KnowledgeManagementDetailDto { Id = id, Title = "资料", CurrentVersionId = versionId, Status = KnowledgeManagementStatus.Ready });
        management.GetListAsync(Arg.Any<GetKnowledgeManagementListInput>()).Returns(new KnowledgeManagementListResultDto());
        knowledge.GetAttachmentsAsync(id, versionId).Returns(new ListResultDto<KnowledgeAttachmentDto>([new() { Id = Guid.NewGuid() }]));
        var page = Create(management, knowledge);

        await InvokeAsync(page, "OpenAsync", id);
        await InvokeAsync(page, "LoadAsync");

        Field<KnowledgeManagementDetailDto>(page, "_detail").Status.ShouldBe(KnowledgeManagementStatus.Ready);
        Field<System.Collections.Generic.IReadOnlyList<KnowledgeAttachmentDto>>(page, "_attachments").Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Should_Clear_Revoked_Detail_Attachments_And_Sharing_Controls(HttpStatusCode statusCode)
    {
        var id = Guid.NewGuid();
        var management = Substitute.For<IKnowledgeManagementAppService>();
        management.GetAsync(id).Returns(_ => Task.FromResult(new KnowledgeManagementDetailDto { Id = id }),
            _ => throw new HttpRequestException("资料不可读取", null, statusCode));
        management.GetListAsync(Arg.Any<GetKnowledgeManagementListInput>()).Returns(new KnowledgeManagementListResultDto());
        var page = Create(management, Substitute.For<IKnowledgeItemAppService>());
        await InvokeAsync(page, "OpenAsync", id);
        SetField(page, "_attachments", new KnowledgeAttachmentDto[] { new() });
        SetField(page, "_sharing", true);

        await InvokeAsync(page, "LoadAsync");

        Field<KnowledgeManagementDetailDto?>(page, "_detail").ShouldBeNull();
        Field<System.Collections.Generic.IReadOnlyList<KnowledgeAttachmentDto>>(page, "_attachments").ShouldBeEmpty();
        Field<bool>(page, "_sharing").ShouldBeFalse();
        Field<string>(page, "_error").ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Should_Preserve_Unsaved_Metadata_Content_And_Sharing_While_Refreshing_Detail()
    {
        var id = Guid.NewGuid();
        var management = Substitute.For<IKnowledgeManagementAppService>();
        management.GetAsync(id).Returns(
            new KnowledgeManagementDetailDto { Id = id, Title = "原始标题", Summary = "原始说明", Status = KnowledgeManagementStatus.Publishing },
            new KnowledgeManagementDetailDto { Id = id, Title = "原始标题", Summary = "原始说明", Status = KnowledgeManagementStatus.Ready });
        management.GetListAsync(Arg.Any<GetKnowledgeManagementListInput>()).Returns(new KnowledgeManagementListResultDto());
        var page = Create(management, Substitute.For<IKnowledgeItemAppService>());
        await InvokeAsync(page, "OpenAsync", id);
        SetField(page, "_metadataTitle", "未保存标题");
        SetField(page, "_metadataSummary", "未保存说明");
        SetField(page, "_editing", true);
        SetField(page, "_content", "未保存正文");
        SetField(page, "_sharing", true);
        var teacherId = Guid.NewGuid();
        Field<System.Collections.Generic.HashSet<Guid>>(page, "_shareIds").Add(teacherId);

        await InvokeAsync(page, "LoadAsync");

        Field<KnowledgeManagementDetailDto>(page, "_detail").Status.ShouldBe(KnowledgeManagementStatus.Ready);
        Field<string>(page, "_metadataTitle").ShouldBe("未保存标题");
        Field<string>(page, "_metadataSummary").ShouldBe("未保存说明");
        Field<string>(page, "_content").ShouldBe("未保存正文");
        Field<bool>(page, "_editing").ShouldBeTrue();
        Field<bool>(page, "_sharing").ShouldBeTrue();
        Field<System.Collections.Generic.HashSet<Guid>>(page, "_shareIds").ShouldContain(teacherId);
    }

    [Fact]
    public async Task Should_Refresh_An_Authorized_Detail_Outside_The_Current_List_And_Update_Unedited_Metadata()
    {
        var id = Guid.NewGuid();
        var management = Substitute.For<IKnowledgeManagementAppService>();
        management.GetAsync(id).Returns(
            new KnowledgeManagementDetailDto { Id = id, Title = "旧标题" },
            new KnowledgeManagementDetailDto { Id = id, Title = "新标题", Summary = "新说明" });
        management.GetListAsync(Arg.Any<GetKnowledgeManagementListInput>()).Returns(new KnowledgeManagementListResultDto());
        var page = Create(management, Substitute.For<IKnowledgeItemAppService>());
        await InvokeAsync(page, "OpenAsync", id);
        SetField(page, "_keyword", "另一份资料");

        await InvokeAsync(page, "LoadAsync");

        Field<KnowledgeManagementDetailDto>(page, "_detail").Title.ShouldBe("新标题");
        Field<string>(page, "_metadataTitle").ShouldBe("新标题");
        Field<string>(page, "_metadataSummary").ShouldBe("新说明");
    }

    [Fact]
    public async Task Should_Hide_Stale_Detail_On_Read_Failure_And_Keep_Metadata_For_A_Retry()
    {
        var id = Guid.NewGuid();
        var management = Substitute.For<IKnowledgeManagementAppService>();
        management.GetAsync(id).Returns(
            _ => Task.FromResult(new KnowledgeManagementDetailDto { Id = id, Title = "原始标题" }),
            _ => throw new HttpRequestException("暂时不可用"),
            _ => Task.FromResult(new KnowledgeManagementDetailDto { Id = id, Title = "原始标题" }));
        management.GetListAsync(Arg.Any<GetKnowledgeManagementListInput>()).Returns(new KnowledgeManagementListResultDto());
        var page = Create(management, Substitute.For<IKnowledgeItemAppService>());
        await InvokeAsync(page, "OpenAsync", id);
        SetField(page, "_metadataTitle", "未保存标题");
        SetField(page, "_sharing", true);
        var teacherId = Guid.NewGuid();
        Field<System.Collections.Generic.HashSet<Guid>>(page, "_shareIds").Add(teacherId);

        await InvokeAsync(page, "LoadAsync");
        Field<KnowledgeManagementDetailDto?>(page, "_detail").ShouldBeNull();
        await InvokeAsync(page, "OpenAsync", id);

        Field<string>(page, "_metadataTitle").ShouldBe("未保存标题");
        Field<bool>(page, "_sharing").ShouldBeTrue();
        Field<System.Collections.Generic.HashSet<Guid>>(page, "_shareIds").ShouldContain(teacherId);
    }

    [Fact]
    public async Task Should_Keep_Category_And_Inactive_State_When_Saving_Content_After_Refresh_Fails()
    {
        var id = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var management = Substitute.For<IKnowledgeManagementAppService>();
        var knowledge = Substitute.For<IKnowledgeItemAppService>();
        management.GetAsync(id).Returns(
            _ => Task.FromResult(new KnowledgeManagementDetailDto
            {
                Id = id, Title = "原始标题", CategoryId = categoryId, IsActive = false,
                Versions = [new() { Id = versionId, Status = KnowledgeVersionStatus.Draft, ContentText = "待修订正文" }]
            }),
            _ => throw new HttpRequestException("暂时不可用"));
        management.GetListAsync(Arg.Any<GetKnowledgeManagementListInput>()).Returns(new KnowledgeManagementListResultDto());
        knowledge.GetAttachmentsAsync(id, versionId).Returns(new ListResultDto<KnowledgeAttachmentDto>([]));
        var page = Create(management, knowledge);
        await InvokeAsync(page, "OpenAsync", id);
        await InvokeAsync(page, "EditContentAsync");
        await InvokeAsync(page, "LoadAsync");
        Field<KnowledgeManagementDetailDto?>(page, "_detail").ShouldBeNull();

        await InvokeAsync(page, "SaveAsync");

        await knowledge.Received(1).UpdateAsync(id, Arg.Is<UpdateKnowledgeItemDto>(x => x.CategoryId == categoryId && !x.IsActive));
    }

    [Fact]
    public async Task Should_Reset_Paging_And_Pass_The_Selected_Status_When_Filtering_Materials()
    {
        var management = Substitute.For<IKnowledgeManagementAppService>();
        management.GetListAsync(Arg.Any<GetKnowledgeManagementListInput>()).Returns(new KnowledgeManagementListResultDto());
        var page = Create(management, Substitute.For<IKnowledgeItemAppService>());
        SetField(page, "_skip", 40);
        SetField(page, "_keyword", "教材");

        await InvokeAsync(page, "SetStatusAsync", KnowledgeManagementStatus.Ready);

        Field<int>(page, "_skip").ShouldBe(0);
        Field<KnowledgeManagementStatus?>(page, "_status").ShouldBe(KnowledgeManagementStatus.Ready);
        await management.Received(1).GetListAsync(Arg.Is<GetKnowledgeManagementListInput>(input =>
            input.Status == KnowledgeManagementStatus.Ready && input.SkipCount == 0 && input.Keyword == "教材" &&
            input.OwnOnly && !input.SharedOnly));
    }

    private static Materials Create(IKnowledgeManagementAppService management, IKnowledgeItemAppService knowledge)
    {
        var page = new Materials();
        typeof(Materials).GetProperty("Management", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, management);
        typeof(Materials).GetProperty("Knowledge", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, knowledge);
        return page;
    }

    private static Task InvokeAsync(Materials page, string name, params object[] args) =>
        (Task)typeof(Materials).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, args)!;

    private static T Field<T>(Materials page, string name) =>
        (T)typeof(Materials).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;

    private static void SetField(Materials page, string name, object value) =>
        typeof(Materials).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);
}
