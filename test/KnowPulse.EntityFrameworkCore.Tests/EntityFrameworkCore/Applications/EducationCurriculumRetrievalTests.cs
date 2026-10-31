using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.AiModelManagement;
using KnowPulse.Education;
using KnowPulse.KnowledgeBase;
using KnowPulse.KnowledgeIndexing;
using KnowPulse.RagAI;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Security.Claims;
using Volo.Abp.Identity;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public partial class EducationCurriculumRetrievalTests : KnowPulseEntityFrameworkCoreTestBase
{
    private readonly IKnowledgeRetriever _retriever;
    private readonly IKnowledgeSchoolSharingAppService _sharing;
    private readonly TestKnowledgeVectorStore _vectors;

    public EducationCurriculumRetrievalTests()
    {
        _retriever = GetRequiredService<IKnowledgeRetriever>();
        _sharing = GetRequiredService<IKnowledgeSchoolSharingAppService>();
        _vectors = GetRequiredService<TestKnowledgeVectorStore>();
        _vectors.Reset();
    }

    [Fact]
    public async Task Exact_Chapter_Mapping_Should_Filter_Personal_Chunks_And_Reject_Unbound_Tagged_Material()
    {
        var f = await FixtureAsync();
        var source = await SourceAsync(f.Reader, "数学积累");
        var otherChapter = await ChunkAsync(source.KnowledgeItemId, source.KnowledgeVersionId, "另一章节正文");
        var unbound = await SourceAsync(f.Reader, "仅有自由学科标签");
        var organization = await SourceAsync(null, "组织资料");
        await WithUnitOfWorkAsync(async () =>
        {
            var item = await Repo<KnowledgeItem>().GetAsync(unbound.KnowledgeItemId);
            item.OrganizePersonalMaterial(PersonalMaterialType.Unspecified, 2026, "数学", ["教材", "第一章"]);
            await Repo<KnowledgeItem>().UpdateManyAsync([item], autoSave: true);
        });
        using var reader = User(f.Reader);
        var binding = await BindAsync(source, f.MappingA);
        await BindAsync(otherChapter, f.MappingB);
        _vectors.SearchResults.AddRange([Hit(otherChapter, 1), Hit(unbound, 0.99), Hit(organization, 0.98), Hit(source, 0.8)]);
        var result = await _retriever.SearchAsync(Search(f, KnowledgeSearchScope.PersonalLibrary));
        result.Single().ChunkId.ShouldBe(source.Id);
        result.Single().CurriculumBindingIds.ShouldBe(new[] { binding });
        result.Single().TextbookKnowledgePointMappingIds.ShouldBe(new[] { f.MappingA });
        result.Single().SchoolKnowledgeShareId.ShouldBeNull();
        _vectors.SearchRequests.Single().PermissionFilter.AllowedCurriculumChunkIds.ShouldBe(new[] { source.Id });
        _vectors.SearchRequests.Single().PermissionFilter.AllowedSharedVersionIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task Precise_Chunk_Confirmation_Should_Override_Whole_Material_Without_Changing_Shared_Snapshot()
    {
        var f = await FixtureAsync();
        var source = await SourceAsync(f.Owner, "整份两章资料");
        var chapterB = await ChunkAsync(source.KnowledgeItemId, source.KnowledgeVersionId, "第二章正文");
        Guid whole;
        Guid precise;
        SchoolKnowledgeShareDto share;
        using (User(f.Owner))
        {
            whole = await BindAsync(source, f.MappingA, wholeMaterial: true);
            precise = await BindAsync(chapterB, f.MappingB);
            share = await _sharing.ShareVersionAsync(Share(f, source));
        }
        share.CurriculumBindingIds.OrderBy(x => x).ShouldBe(new[] { whole, precise }.OrderBy(x => x));
        _vectors.SearchResults.AddRange([Hit(chapterB, 1), Hit(source, 0.8)]);
        using (User(f.Reader))
        {
            (await _retriever.SearchAsync(Search(f, KnowledgeSearchScope.SchoolShared))).Single().ChunkId.ShouldBe(source.Id);
            var b = Search(f, KnowledgeSearchScope.SchoolShared);
            b.CurriculumScope!.TextbookKnowledgePointMappingIds = [f.MappingB];
            (await _retriever.SearchAsync(b)).Single().ChunkId.ShouldBe(chapterB.Id);
        }
        using (User(f.Owner)) await BindAsync(source, f.MappingB);
        using (User(f.Reader))
        {
            var shared = (await _retriever.SearchAsync(Search(f, KnowledgeSearchScope.SchoolShared))).Single();
            shared.ChunkId.ShouldBe(source.Id);
            shared.CurriculumBindingIds.ShouldBe(new[] { whole });
            shared.TextbookKnowledgePointMappingIds.ShouldBe(new[] { f.MappingA });
            (await _sharing.GetAsync(share.Id)).CurriculumBindingIds.OrderBy(x => x).ShouldBe(share.CurriculumBindingIds.OrderBy(x => x));
        }
        using (User(f.Owner))
        {
            var personal = Search(f, KnowledgeSearchScope.PersonalLibrary, owner: true);
            (await _retriever.SearchAsync(personal)).ShouldBeEmpty();
            await EndBindingAsync(precise);
        }
        using (User(f.Reader))
        {
            // Ending chapter B's precise binding cannot reopen that chunk through the old whole-material chapter A binding.
            (await _retriever.SearchAsync(Search(f, KnowledgeSearchScope.SchoolShared))).Single().ChunkId.ShouldBe(source.Id);
            var ended = Search(f, KnowledgeSearchScope.SchoolShared);
            ended.CurriculumScope!.TextbookKnowledgePointMappingIds = [f.MappingB];
            (await _retriever.SearchAsync(ended)).ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task First_Sharing_After_Ending_A_Passage_Should_Freeze_Its_Whole_Material_Exclusion()
    {
        var f = await FixtureAsync();
        var source = await SourceAsync(f.Owner, "整份适用但已结束第二章关联");
        var chapterB = await ChunkAsync(source.KnowledgeItemId, source.KnowledgeVersionId, "第二章已结束段落");
        Guid whole;
        Guid endedPassage;
        SchoolKnowledgeShareDto share;
        using (User(f.Owner))
        {
            whole = await BindAsync(source, f.MappingA, wholeMaterial: true);
            var endedWhole = await BindAsync(source, f.MappingB, wholeMaterial: true);
            await EndBindingAsync(endedWhole);
            endedPassage = await BindAsync(chapterB, f.MappingB);
            await EndBindingAsync(endedPassage);
            share = await _sharing.ShareVersionAsync(Share(f, source));
            share.CurriculumBindingIds.ShouldNotContain(endedWhole);
        }
        share.CurriculumBindingIds.OrderBy(x => x).ShouldBe(new[] { whole, endedPassage }.OrderBy(x => x));
        _vectors.SearchResults.AddRange([Hit(chapterB, 1), Hit(source, 0.8)]);
        using var reader = User(f.Reader);
        var request = Search(f, KnowledgeSearchScope.SchoolShared);
        var hit = (await _retriever.SearchAsync(request)).Single();
        hit.ChunkId.ShouldBe(source.Id);
        hit.CurriculumBindingIds.ShouldBe(new[] { whole });
        _vectors.SearchRequests.Last().PermissionFilter.AllowedCurriculumChunkIds.ShouldBe(new[] { source.Id });
        var snapshot = await GetRequiredService<EducationCurriculumAccessGuard>().ResolveAsync(request.CurriculumScope!);
        await Should.ThrowAsync<AbpAuthorizationException>(() => GetRequiredService<EducationMaterialScope>().ValidateSourceAsync(snapshot,
            chapterB.KnowledgeItemId, chapterB.KnowledgeVersionId, chapterB.Id, [whole], [f.MappingA], share.Id));
        request.CurriculumScope!.TextbookKnowledgePointMappingIds = [f.MappingB];
        (await _retriever.SearchAsync(request)).ShouldBeEmpty();
        _vectors.SearchRequests.Last().PermissionFilter.AllowedCurriculumChunkIds!.ShouldBeEmpty();
    }

    [Fact]
    public async Task Sharing_Again_After_Withdrawal_Should_Preserve_Ended_Passage_Exclusions()
    {
        var f = await FixtureAsync();
        var source = await SourceAsync(f.Owner, "撤回后重新共享整份资料");
        var chapterB = await ChunkAsync(source.KnowledgeItemId, source.KnowledgeVersionId, "重新共享前结束的第二章");
        Guid whole;
        Guid endedPassage;
        SchoolKnowledgeShareDto first;
        SchoolKnowledgeShareDto second;
        using (User(f.Owner))
        {
            whole = await BindAsync(source, f.MappingA, wholeMaterial: true);
            endedPassage = await BindAsync(chapterB, f.MappingB);
            first = await _sharing.ShareVersionAsync(Share(f, source));
            await EndBindingAsync(endedPassage);
            await _sharing.RevokeAsync(first.Id);
            second = await _sharing.ShareVersionAsync(Share(f, source));
            second.Id.ShouldNotBe(first.Id);
            second.CurriculumBindingIds.OrderBy(x => x).ShouldBe(new[] { whole, endedPassage }.OrderBy(x => x));
            (await _sharing.GetAsync(first.Id)).IsRevoked.ShouldBeTrue();
        }
        _vectors.SearchResults.AddRange([Hit(chapterB, 1), Hit(source, 0.8)]);
        using var reader = User(f.Reader);
        var hit = (await _retriever.SearchAsync(Search(f, KnowledgeSearchScope.SchoolShared))).Single();
        hit.ChunkId.ShouldBe(source.Id);
        hit.SchoolKnowledgeShareId.ShouldBe(second.Id);
        hit.CurriculumBindingIds.ShouldBe(new[] { whole });
        _vectors.SearchRequests.Last().PermissionFilter.AllowedCurriculumChunkIds.ShouldBe(new[] { source.Id });
        var request = Search(f, KnowledgeSearchScope.SchoolShared);
        request.CurriculumScope!.TextbookKnowledgePointMappingIds = [f.MappingB];
        (await _retriever.SearchAsync(request)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Later_Private_Passage_Organization_Should_Not_Change_An_Existing_Shared_Whole_Material_Scope()
    {
        var f = await FixtureAsync();
        var source = await SourceAsync(f.Owner, "共享时明确整份适用第一章");
        var chapterB = await ChunkAsync(source.KnowledgeItemId, source.KnowledgeVersionId, "共享后私人重新整理的段落");
        Guid whole;
        SchoolKnowledgeShareDto share;
        using (User(f.Owner))
        {
            whole = await BindAsync(source, f.MappingA, wholeMaterial: true);
            share = await _sharing.ShareVersionAsync(Share(f, source));
        }
        _vectors.SearchResults.AddRange([Hit(chapterB, 1), Hit(source, 0.8)]);
        using (User(f.Reader))
            (await _retriever.SearchAsync(Search(f, KnowledgeSearchScope.SchoolShared))).Select(x => x.ChunkId).OrderBy(x => x)
                .ShouldBe(new[] { source.Id, chapterB.Id }.OrderBy(x => x));
        using (User(f.Owner))
        {
            var laterPrivatePassage = await BindAsync(chapterB, f.MappingB);
            await EndBindingAsync(laterPrivatePassage);
            (await _retriever.SearchAsync(Search(f, KnowledgeSearchScope.PersonalLibrary, owner: true))).Single().ChunkId.ShouldBe(source.Id);
            (await _sharing.GetAsync(share.Id)).CurriculumBindingIds.ShouldBe(new[] { whole });
        }
        using (User(f.Reader))
        {
            var hits = await _retriever.SearchAsync(Search(f, KnowledgeSearchScope.SchoolShared));
            hits.Select(x => x.ChunkId).OrderBy(x => x).ShouldBe(new[] { source.Id, chapterB.Id }.OrderBy(x => x));
            hits.ShouldAllBe(x => x.SchoolKnowledgeShareId == share.Id && x.CurriculumBindingIds.SequenceEqual(new[] { whole }));
            var request = Search(f, KnowledgeSearchScope.SchoolShared);
            request.CurriculumScope!.TextbookKnowledgePointMappingIds = [f.MappingB];
            (await _retriever.SearchAsync(request)).ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Ended_Passage_Exclusions_Should_Count_Toward_Sharing_Capacity_And_Fail_Closed()
    {
        var f = await FixtureAsync();
        var source = await SourceAsync(f.Owner, "保留大量结束关联历史的资料");
        using var owner = User(f.Owner);
        await BindAsync(source, f.MappingA, wholeMaterial: true);
        var ended = await BindAsync(source, f.MappingB);
        await EndBindingAsync(ended);
        await WithUnitOfWorkAsync(async () =>
        {
            var template = await Repo<KnowledgeMaterialCurriculumBinding>().GetAsync(ended);
            var now = DateTime.UtcNow;
            var exclusions = Enumerable.Range(0, SchoolKnowledgeShare.MaxCurriculumBindings - 1).Select(_ =>
            {
                var binding = new KnowledgeMaterialCurriculumBinding(Guid.NewGuid(), template.TenantId, template.OwnerUserId,
                    template.LibraryId, template.KnowledgeItemId, template.KnowledgeVersionId, template.KnowledgeChunkId,
                    template.TextbookVersionId, template.TextbookKnowledgePointMappingId, template.CurriculumSnapshotJson,
                    template.MaterialTitle, false, now);
                binding.End(now);
                return binding;
            });
            await Repo<KnowledgeMaterialCurriculumBinding>().InsertManyAsync(exclusions, autoSave: true);
        });
        (await Should.ThrowAsync<Volo.Abp.BusinessException>(() => _sharing.ShareVersionAsync(Share(f, source))))
            .Code.ShouldBe(EducationCurriculumErrorCodes.CapacityExceeded);
        (await WithUnitOfWorkAsync(() => Repo<SchoolKnowledgeShare>().CountAsync(x => x.KnowledgeVersionId == source.KnowledgeVersionId)))
            .ShouldBe(0);
        _vectors.SearchRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Legacy_Unbound_Sharing_And_Repeated_Confirmation_Should_Not_Acquire_Later_Bindings()
    {
        var f = await FixtureAsync();
        var source = await SourceAsync(f.Owner, "原未关联共享");
        var request = Share(f, source);
        SchoolKnowledgeShareDto first;
        Guid binding;
        using (User(f.Owner))
        {
            first = await _sharing.ShareVersionAsync(request);
            first.CurriculumBindingIds.ShouldBeEmpty();
            binding = await BindAsync(source, f.MappingA);
            (await _sharing.ShareVersionAsync(request)).CurriculumBindingIds.ShouldBeEmpty();
            (await _sharing.ShareVersionAsync(Share(f, source))).Id.ShouldBe(first.Id);
        }
        _vectors.SearchResults.Add(Hit(source));
        using (User(f.Reader))
        {
            (await _retriever.SearchAsync(Search(f, KnowledgeSearchScope.SchoolShared))).ShouldBeEmpty();
            _vectors.SearchRequests.Last().PermissionFilter.AllowedCurriculumChunkIds.ShouldNotBeNull();
            _vectors.SearchRequests.Last().PermissionFilter.AllowedCurriculumChunkIds!.ShouldBeEmpty();
        }
        using (User(f.Owner))
        {
            await _sharing.RevokeAsync(first.Id);
            var second = await _sharing.ShareVersionAsync(Share(f, source));
            second.Id.ShouldNotBe(first.Id);
            second.CurriculumBindingIds.ShouldBe(new[] { binding });
        }
        using (User(f.Reader)) (await _retriever.SearchAsync(Search(f, KnowledgeSearchScope.SchoolShared))).Single().ChunkId.ShouldBe(source.Id);
    }

    [Fact]
    public async Task Shared_Old_Version_And_Current_Private_Version_Should_Have_Independent_Curriculum_Bindings()
    {
        var f = await FixtureAsync();
        var v1 = await SourceAsync(f.Owner, "共享旧版本标题");
        Guid oldBinding;
        SchoolKnowledgeShareDto share;
        using (User(f.Owner))
        {
            oldBinding = await BindAsync(v1, f.MappingA);
            share = await _sharing.ShareVersionAsync(Share(f, v1));
        }
        var v2 = await WithUnitOfWorkAsync(async () =>
        {
            var old = await Repo<KnowledgeVersion>().GetAsync(v1.KnowledgeVersionId);
            old.ClearCurrentPublished();
            await Repo<KnowledgeVersion>().UpdateManyAsync([old], autoSave: true);
            var version = new KnowledgeVersion(Guid.NewGuid(), null, v1.KnowledgeItemId, 2, "PRIVATE-V2-TITLE", "PRIVATE-V2-CONTENT");
            version.Publish(DateTime.UtcNow);
            await Repo<KnowledgeVersion>().InsertAsync(version, autoSave: true);
            var item = await Repo<KnowledgeItem>().GetAsync(v1.KnowledgeItemId);
            item.SetCurrentPublishedVersion(version.Id);
            item.Rename("PRIVATE-V2-TITLE");
            await Repo<KnowledgeItem>().UpdateManyAsync([item], autoSave: true);
            return await ChunkAsync(item.Id, version.Id, "PRIVATE-V2-CONTENT");
        });
        using (User(f.Owner)) await BindAsync(v2, f.MappingB);
        _vectors.SearchResults.AddRange([Hit(v2, 1), Hit(v1, 0.8)]);
        using (User(f.Reader))
        {
            var result = (await _retriever.SearchAsync(Search(f, KnowledgeSearchScope.SchoolShared))).Single();
            result.KnowledgeVersionId.ShouldBe(v1.KnowledgeVersionId);
            result.SourceTitle.ShouldBe("共享旧版本标题");
            result.SchoolKnowledgeShareId.ShouldBe(share.Id);
            result.CurriculumBindingIds.ShouldBe(new[] { oldBinding });
            result.Content.ShouldNotContain("PRIVATE-V2");
            var chapterB = Search(f, KnowledgeSearchScope.SchoolShared);
            chapterB.CurriculumScope!.TextbookKnowledgePointMappingIds = [f.MappingB];
            (await _retriever.SearchAsync(chapterB)).ShouldBeEmpty();
        }
        using (User(f.Owner))
        {
            var personal = Search(f, KnowledgeSearchScope.PersonalLibrary, owner: true);
            personal.CurriculumScope!.TextbookKnowledgePointMappingIds = [f.MappingB];
            (await _retriever.SearchAsync(personal)).Single().KnowledgeVersionId.ShouldBe(v2.KnowledgeVersionId);
        }
    }

    [Theory]
    [InlineData("binding")]
    [InlineData("selection")]
    [InlineData("share")]
    public async Task Changes_During_Vector_Call_Should_Block_Use_Of_Already_Retrieved_Curriculum_Source(string change)
    {
        var f = await FixtureAsync();
        var source = await SourceAsync(f.Owner, "关联可撤回");
        Guid binding;
        SchoolKnowledgeShareDto share;
        using (User(f.Owner))
        {
            binding = await BindAsync(source, f.MappingA);
            share = await _sharing.ShareVersionAsync(Share(f, source));
        }
        var vectors = Substitute.For<IKnowledgeVectorStore>();
        vectors.SearchAsync(Arg.Any<KnowledgeVectorSearchRequest>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            call.Arg<KnowledgeVectorSearchRequest>().PermissionFilter.AllowedCurriculumChunkIds.ShouldBe(new[] { source.Id });
            if (change == "binding") using (User(f.Owner)) await EndBindingAsync(binding);
            else if (change == "share") using (User(f.Owner)) await _sharing.RevokeAsync(share.Id);
            else await WithUnitOfWorkAsync(async () =>
            {
                var selection = await Repo<TeacherTextbookSelection>().GetAsync(f.ReaderSelection);
                selection.End(DateTime.UtcNow);
                await Repo<TeacherTextbookSelection>().UpdateManyAsync([selection], autoSave: true);
            });
            return (IReadOnlyList<VectorSearchHit>)new[] { Hit(source) };
        });
        var retriever = new KnowledgeRetriever(GetRequiredService<IKnowledgeEmbeddingClient>(), vectors,
            GetRequiredService<IKnowledgeAccessContextProvider>(), Repo<KnowledgeItem>(), Repo<KnowledgeVersion>(),
            Repo<KnowledgePermissionScope>(), Repo<KnowledgeChunk>(), GetRequiredService<IRagCallLogWriter>(),
            GetRequiredService<IAiModelResolver>(), GetRequiredService<IOptions<RagAiOptions>>())
        { LazyServiceProvider = GetRequiredService<KnowledgeRetriever>().LazyServiceProvider };
        using var reader = User(f.Reader);
        if (change == "selection") await Should.ThrowAsync<AbpAuthorizationException>(() => WithUnitOfWorkAsync(() => retriever.SearchAsync(Search(f, KnowledgeSearchScope.SchoolShared))));
        else (await WithUnitOfWorkAsync(() => retriever.SearchAsync(Search(f, KnowledgeSearchScope.SchoolShared)))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Final_Source_Validation_Should_Require_Original_Active_Binding_And_Exact_Mapping_Pair()
    {
        var f = await FixtureAsync();
        var source = await SourceAsync(f.Reader, "生成来源");
        using var reader = User(f.Reader);
        var binding = await BindAsync(source, f.MappingA);
        _vectors.SearchResults.Add(Hit(source));
        var hit = (await _retriever.SearchAsync(Search(f, KnowledgeSearchScope.PersonalLibrary))).Single();
        var snapshot = await GetRequiredService<EducationCurriculumAccessGuard>().ResolveAsync(Search(f, KnowledgeSearchScope.PersonalLibrary).CurriculumScope!);
        var guard = GetRequiredService<EducationMaterialScope>();
        await guard.ValidateSourceAsync(snapshot, hit.KnowledgeItemId, hit.KnowledgeVersionId, hit.ChunkId, hit.CurriculumBindingIds.ToArray(), hit.TextbookKnowledgePointMappingIds.ToArray());
        await Should.ThrowAsync<AbpAuthorizationException>(() => guard.ValidateSourceAsync(snapshot, hit.KnowledgeItemId, hit.KnowledgeVersionId, hit.ChunkId, [binding], [f.MappingB]));
        await EndBindingAsync(binding);
        var replacement = await BindAsync(source, f.MappingA);
        replacement.ShouldNotBe(binding);
        await Should.ThrowAsync<AbpAuthorizationException>(() => guard.ValidateSourceAsync(snapshot, hit.KnowledgeItemId, hit.KnowledgeVersionId, hit.ChunkId, [binding], [f.MappingA]));
    }

    [Fact]
    public async Task Retrieve_Only_Curriculum_Source_Should_Not_Pass_New_Generation_Source_Validation()
    {
        var f = await FixtureAsync();
        var source = await SourceAsync(f.Owner, "仅可检索");
        using (User(f.Owner))
        {
            await BindAsync(source, f.MappingA);
            var input = Share(f, source);
            input.Uses = SchoolKnowledgeShareUses.Read | SchoolKnowledgeShareUses.Retrieve;
            await _sharing.ShareVersionAsync(input);
        }
        _vectors.SearchResults.Add(Hit(source));
        using var reader = User(f.Reader);
        var request = Search(f, KnowledgeSearchScope.SchoolShared);
        var hit = (await _retriever.SearchAsync(request)).Single();
        var snapshot = await GetRequiredService<EducationCurriculumAccessGuard>().ResolveAsync(request.CurriculumScope!);
        await Should.ThrowAsync<AbpAuthorizationException>(() => GetRequiredService<EducationMaterialScope>().ValidateSourceAsync(snapshot,
            hit.KnowledgeItemId, hit.KnowledgeVersionId, hit.ChunkId, hit.CurriculumBindingIds.ToArray(), hit.TextbookKnowledgePointMappingIds.ToArray(), hit.SchoolKnowledgeShareId));
        request.ForQuestionGeneration = true;
        (await _retriever.SearchAsync(request)).ShouldBeEmpty();
        _vectors.SearchRequests.Last().PermissionFilter.AllowedCurriculumChunkIds!.ShouldBeEmpty();
    }

    [Fact]
    public async Task Foreign_Selection_Or_Mapping_Should_Not_Expand_Authorized_Curriculum()
    {
        var f = await FixtureAsync();
        using var reader = User(f.Reader);
        var foreignSelection = Search(f, KnowledgeSearchScope.PersonalLibrary, owner: true);
        await Should.ThrowAsync<AbpAuthorizationException>(() => _retriever.SearchAsync(foreignSelection));
        var forgedMapping = Search(f, KnowledgeSearchScope.PersonalLibrary);
        forgedMapping.CurriculumScope!.TextbookKnowledgePointMappingIds = [Guid.NewGuid()];
        await Should.ThrowAsync<Volo.Abp.BusinessException>(() => _retriever.SearchAsync(forgedMapping));
        _vectors.SearchRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Sharing_Should_Freeze_Only_Confirmed_Bindings_For_The_Selected_School()
    {
        var f = await FixtureAsync();
        var other = await FixtureAsync();
        await WithUnitOfWorkAsync(() => Repo<SchoolMembership>().InsertAsync(new SchoolMembership(Guid.NewGuid(), null,
            other.SchoolId, f.Owner, SchoolMemberRole.Teacher, DateTime.UtcNow.AddDays(-1)), autoSave: true));
        var source = await SourceAsync(f.Owner, "跨校本人资料");
        using var owner = User(f.Owner);
        var selectedSchoolBinding = await BindAsync(source, f.MappingA);
        var otherSchoolBinding = await BindAsync(source, other.MappingA);
        var share = await _sharing.ShareVersionAsync(Share(f, source));
        share.CurriculumBindingIds.ShouldBe(new[] { selectedSchoolBinding });
        share.CurriculumBindingIds.ShouldNotContain(otherSchoolBinding);
    }

    [Fact]
    public async Task Curriculum_Chunk_Capacity_Should_Fail_Closed_Instead_Of_Falling_Back_To_Unfiltered_Search()
    {
        var f = await FixtureAsync();
        var source = await SourceAsync(f.Reader, "明确整份适用的长资料");
        await WithUnitOfWorkAsync(() => Repo<KnowledgeChunk>().InsertManyAsync(Enumerable.Range(1, 3000).Select(i =>
            new KnowledgeChunk(Guid.NewGuid(), null, source.KnowledgeItemId, source.KnowledgeVersionId, source.KnowledgeIndexJobId,
                i, $"完整资料第{i}段", Guid.NewGuid().ToString("N"), "资料标题", isPublishedSnapshot: true)), autoSave: true));
        using var reader = User(f.Reader);
        await BindAsync(source, f.MappingA, wholeMaterial: true);
        (await Should.ThrowAsync<Volo.Abp.BusinessException>(() => _retriever.SearchAsync(Search(f, KnowledgeSearchScope.PersonalLibrary))))
            .Code.ShouldBe(EducationCurriculumErrorCodes.CapacityExceeded);
        _vectors.SearchRequests.ShouldBeEmpty();
    }

    private Task<Fixture> FixtureAsync() => WithUnitOfWorkAsync(async () =>
    {
        var owner = Guid.NewGuid();
        var reader = Guid.NewGuid();
        foreach (var user in new[] { owner, reader })
        {
            await Repo<IdentityUser>().InsertAsync(new IdentityUser(user, $"currag-{user:N}", $"{user:N}@example.invalid"), autoSave: true);
            await Repo<KnowledgeLibrary>().InsertAsync(new KnowledgeLibrary(user, null), autoSave: true);
        }
        var now = DateTime.UtcNow;
        var school = new School(Guid.NewGuid(), null, $"S{Guid.NewGuid():N}", "教材检索初中");
        var subject = new EducationSubject(Guid.NewGuid(), null, $"M{Guid.NewGuid():N}", "数学");
        var term = new AcademicTerm(Guid.NewGuid(), null, school.Id, "T1", "当前学期", now.AddDays(-2), now.AddDays(60));
        var schoolClass = new SchoolClass(Guid.NewGuid(), null, school.Id, term.Id, "C1", "七年一班", 7);
        await Repo<School>().InsertAsync(school, autoSave: true);
        await Repo<EducationSubject>().InsertAsync(subject, autoSave: true);
        await Repo<AcademicTerm>().InsertAsync(term, autoSave: true);
        await Repo<SchoolClass>().InsertAsync(schoolClass, autoSave: true);
        var book = new TextbookVersion(Guid.NewGuid(), null, school.Id, subject.Id, "MATH", 1, 7, "数学教材", "测试出版社", "测试版", "上册");
        await Repo<TextbookVersion>().InsertAsync(book, autoSave: true);
        var mappingIds = new List<Guid>();
        for (var i = 0; i < 2; i++)
        {
            var chapter = new TextbookChapter(Guid.NewGuid(), null, book.Id, null, $"CH{i}", $"第{i + 1}章", i);
            var point = new CourseKnowledgePoint(Guid.NewGuid(), null, school.Id, subject.Id, $"KP{i}");
            var revision = new CourseKnowledgePointRevision(Guid.NewGuid(), null, point.Id, point.AdvanceRevision(), "同名知识点", null);
            var mapping = new TextbookKnowledgePointMapping(Guid.NewGuid(), null, book.Id, chapter.Id, point.Id, revision.Id, owner, now);
            await Repo<TextbookChapter>().InsertAsync(chapter, autoSave: true);
            await Repo<CourseKnowledgePoint>().InsertAsync(point, autoSave: true);
            await Repo<CourseKnowledgePointRevision>().InsertAsync(revision, autoSave: true);
            await Repo<TextbookKnowledgePointMapping>().InsertAsync(mapping, autoSave: true);
            mappingIds.Add(mapping.Id);
        }
        book.Publish(now);
        await Repo<TextbookVersion>().UpdateManyAsync([book], autoSave: true);
        var selections = new List<Guid>();
        foreach (var user in new[] { owner, reader })
        {
            await Repo<SchoolMembership>().InsertAsync(new SchoolMembership(Guid.NewGuid(), null, school.Id, user, SchoolMemberRole.Teacher, term.StartsAtUtc), autoSave: true);
            var assignment = new TeachingAssignment(Guid.NewGuid(), null, school.Id, term.Id, schoolClass.Id, subject.Id, user, term.StartsAtUtc, term.EndsAtUtc);
            await Repo<TeachingAssignment>().InsertAsync(assignment, autoSave: true);
            var selection = new TeacherTextbookSelection(Guid.NewGuid(), null, school.Id, user, assignment.Id, book.Id, now);
            await Repo<TeacherTextbookSelection>().InsertAsync(selection, autoSave: true);
            selections.Add(selection.Id);
        }
        return new Fixture(owner, reader, school.Id, book.Id, selections[0], selections[1], mappingIds[0], mappingIds[1]);
    });

    private Task<KnowledgeChunk> SourceAsync(Guid? owner, string title) => WithUnitOfWorkAsync(async () =>
    {
        var item = new KnowledgeItem(Guid.NewGuid(), null, title, libraryId: owner, ownerUserId: owner);
        await Repo<KnowledgeItem>().InsertAsync(item, autoSave: true);
        var version = new KnowledgeVersion(Guid.NewGuid(), null, item.Id, 1, title, title);
        version.Publish(DateTime.UtcNow);
        await Repo<KnowledgeVersion>().InsertAsync(version, autoSave: true);
        item.SetCurrentPublishedVersion(version.Id);
        await Repo<KnowledgeItem>().UpdateManyAsync([item], autoSave: true);
        return await ChunkAsync(item.Id, version.Id, title);
    });

    private Task<KnowledgeChunk> ChunkAsync(Guid itemId, Guid versionId, string content) => WithUnitOfWorkAsync(async () =>
    {
        // These are relational authorization fixtures; ingestion lifecycle tests separately exercise real embedding and upsert.
        var job = new KnowledgeIndexJob(Guid.NewGuid(), null, itemId, versionId, KnowledgeIndexJobTriggerType.Publish);
        job.MarkIndexed(DateTime.UtcNow);
        await Repo<KnowledgeIndexJob>().InsertAsync(job, autoSave: true);
        var chunk = new KnowledgeChunk(Guid.NewGuid(), null, itemId, versionId, job.Id, 0, content, Guid.NewGuid().ToString("N"),
            "PRIVATE-CHUNK-TITLE", isPublishedSnapshot: true);
        await Repo<KnowledgeChunk>().InsertAsync(chunk, autoSave: true);
        return chunk;
    });

    private async Task<Guid> BindAsync(KnowledgeChunk chunk, Guid mappingId, bool wholeMaterial = false)
    {
        var binding = await GetRequiredService<IEducationPreparationAppService>().ConfirmBindingAsync(new ConfirmKnowledgeMaterialCurriculumBindingDto
        {
            IdempotencyKey = Guid.NewGuid(), KnowledgeVersionId = chunk.KnowledgeVersionId,
            KnowledgeChunkId = wholeMaterial ? null : chunk.Id, TextbookKnowledgePointMappingId = mappingId,
            ConfirmWholeMaterialForThisMapping = wholeMaterial
        });
        return binding.Id;
    }

    private async Task EndBindingAsync(Guid id)
    {
        var binding = await WithUnitOfWorkAsync(() => Repo<KnowledgeMaterialCurriculumBinding>().GetAsync(id));
        await GetRequiredService<IEducationPreparationAppService>().EndBindingAsync(id, new EndMaterialBindingDto { ConcurrencyStamp = binding.ConcurrencyStamp });
    }

    private IRepository<T, Guid> Repo<T>() where T : class, IEntity<Guid> => GetRequiredService<IRepository<T, Guid>>();
    private IDisposable User(Guid id) => GetRequiredService<ICurrentPrincipalAccessor>().Change(new ClaimsPrincipal(new ClaimsIdentity(
        [new Claim(AbpClaimTypes.UserId, id.ToString()), new Claim(AbpClaimTypes.UserName, $"currag-{id:N}"), new Claim(AbpClaimTypes.Role, "admin")], "Test")));
    private static RagSearchRequest Search(Fixture f, KnowledgeSearchScope scope, bool owner = false) => new()
    {
        Query = "第一章同名知识点", Scope = scope,
        CurriculumScope = new EducationCurriculumScopeDto { TeacherTextbookSelectionId = owner ? f.OwnerSelection : f.ReaderSelection, TextbookKnowledgePointMappingIds = [f.MappingA] }
    };
    private static CreateSchoolKnowledgeShareDto Share(Fixture f, KnowledgeChunk chunk) => new()
    {
        IdempotencyKey = Guid.NewGuid(), KnowledgeItemId = chunk.KnowledgeItemId, KnowledgeVersionId = chunk.KnowledgeVersionId, SchoolId = f.SchoolId,
        Uses = SchoolKnowledgeShareUses.Read | SchoolKnowledgeShareUses.Retrieve | SchoolKnowledgeShareUses.GenerateQuestions
    };
    private static VectorSearchHit Hit(KnowledgeChunk chunk, double score = 0.9) => new()
    { ChunkId = chunk.Id, KnowledgeItemId = chunk.KnowledgeItemId, KnowledgeVersionId = chunk.KnowledgeVersionId, Content = "untrusted vector content", Score = score };
    private sealed record Fixture(Guid Owner, Guid Reader, Guid SchoolId, Guid BookId, Guid OwnerSelection, Guid ReaderSelection, Guid MappingA, Guid MappingB);
}
