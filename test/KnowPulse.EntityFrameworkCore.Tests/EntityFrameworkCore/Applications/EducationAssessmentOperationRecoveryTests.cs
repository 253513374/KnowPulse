using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using KnowPulse.Education;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.Identity;
using Volo.Abp.Security.Claims;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Assessment_Create_Unknown_Result_Should_Survive_An_Identity_Rejection_And_Recover_The_Original_Operation(bool anonymous)
    {
        var f = await FixtureAsync();
        using var teacher = User(f.Reader);
        var input = await AssessmentInputAsync(f);
        // The command committed, but the client lost its acknowledgement and must
        // retain this exact input and operation ID through a temporary 401/403.
        var committed = await Assessments.CreateDraftAsync(input);
        committed.Id.ShouldBe(input.OperationId);
        committed.CurrentRevision.Id.ShouldBe(input.OperationId);

        if (anonymous)
        {
            using var signedOut = GetRequiredService<ICurrentPrincipalAccessor>().Change(new ClaimsPrincipal(new ClaimsIdentity()));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.CreateDraftAsync(input));
        }
        else
        {
            await WithUnitOfWorkAsync(async () =>
            {
                var account = await Repo<IdentityUser>().GetAsync(f.Reader);
                account.SetIsActive(false);
                await Repo<IdentityUser>().UpdateManyAsync([account], autoSave: true);
            });
            await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.CreateDraftAsync(input));
        }

        await WithUnitOfWorkAsync(async () =>
        {
            var roots = await Repo<EducationAssessmentTask>().GetListAsync();
            roots.Count.ShouldBe(1);
            roots.Single().Id.ShouldBe(committed.Id);
            roots.Single().CurrentRevisionId.ShouldBe(committed.CurrentRevision.Id);
            var revisions = await Repo<EducationAssessmentTaskRevision>().GetListAsync(x => x.TaskId == committed.Id);
            revisions.Count.ShouldBe(1);
            revisions.Single().Id.ShouldBe(input.OperationId);
            revisions.Single().RequestFingerprint.ShouldBe(committed.CurrentRevision.RequestFingerprint);
        });

        if (!anonymous) await WithUnitOfWorkAsync(async () =>
        {
            var account = await Repo<IdentityUser>().GetAsync(f.Reader);
            account.SetIsActive(true);
            await Repo<IdentityUser>().UpdateManyAsync([account], autoSave: true);
        });

        var recovered = await Assessments.CreateDraftAsync(input);
        recovered.Id.ShouldBe(committed.Id);
        recovered.ConcurrencyStamp.ShouldBe(committed.ConcurrencyStamp);
        recovered.CurrentRevision.Id.ShouldBe(committed.CurrentRevision.Id);
        recovered.CurrentRevision.RequestFingerprint.ShouldBe(committed.CurrentRevision.RequestFingerprint);
        recovered.CurrentRevision.Title.ShouldBe(input.Title);
        recovered.CurrentRevision.RecordedAtUtc.ShouldBe(committed.CurrentRevision.RecordedAtUtc);
        var receipt = await Assessments.GetOperationAsync(committed.Id, input.OperationId);
        receipt!.Id.ShouldBe(committed.CurrentRevision.Id);
        receipt.RequestFingerprint.ShouldBe(committed.CurrentRevision.RequestFingerprint);
        await WithUnitOfWorkAsync(async () =>
        {
            (await Repo<EducationAssessmentTask>().GetListAsync()).Count.ShouldBe(1);
            (await Repo<EducationAssessmentTaskRevision>().GetListAsync(x => x.TaskId == committed.Id)).Count.ShouldBe(1);
        });
    }
}
