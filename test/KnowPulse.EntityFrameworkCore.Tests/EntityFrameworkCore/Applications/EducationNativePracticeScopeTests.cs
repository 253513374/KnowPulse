using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.Exam;
using Shouldly;
using Volo.Abp.Authorization;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    [Fact]
    public async Task Native_Practice_School_Checks_Should_Precede_Start_Bodies_And_Feedback_Exposure_Without_Changing_Old_Start_Fingerprints()
    {
        var f = await GradingFixtureAsync(false); var release = await ExposureReleaseAsync(f, ExamResultVisibility.FullReview);
        using var student = NotebookActor(f.Source.Student.UserId);
        var school = f.Source.School.SchoolId; var wrong = Guid.NewGuid(); var start = PracticeStart(f, release);
        await Should.ThrowAsync<AbpAuthorizationException>(() => Practices.StartMyPracticeForSchoolAsync(wrong, f.RecipientId, start));
        (await WithUnitOfWorkAsync(() => Repo<EducationQuestionPractice>().GetCountAsync())).ShouldBe(0);
        (await Practices.GetMyPracticeStartOperationAsync(school, f.RecipientId, start.OperationId)).ShouldBeNull();
        var receipt = await Practices.StartMyPracticeAsync(f.RecipientId, start);
        (await Practices.StartMyPracticeForSchoolAsync(school, f.RecipientId, start)).RequestFingerprint.ShouldBe(receipt.RequestFingerprint);
        (await Practices.GetMyPracticeStartOperationAsync(school, f.RecipientId, start.OperationId))!.PracticeId.ShouldBe(receipt.PracticeId);
        await Should.ThrowAsync<AbpAuthorizationException>(() => Practices.GetMyPracticeStartOperationAsync(wrong, f.RecipientId, start.OperationId));
        (await Practices.GetMyPracticeListAsync(new() { SchoolId = school })).Items.Single().SchoolId.ShouldBe(school);
        (await Practices.GetMyPracticeListAsync(new() { SchoolId = wrong })).TotalCount.ShouldBe(0);
        (await Practices.GetMyPracticeListAsync(new())).TotalCount.ShouldBe(1);
        await Should.ThrowAsync<AbpAuthorizationException>(() => Practices.GetMyPracticeForSchoolAsync(receipt.PracticeId, wrong));
        var detail = await Practices.GetMyPracticeForSchoolAsync(receipt.PracticeId, school);
        detail.SchoolId.ShouldBe(school); detail.Feedback.ShouldBeNull();
        await Practices.SubmitMyPracticeAsync(detail.Id, await PracticeSubmitAsync(detail.Id, PracticeBlanks(detail)));
        await Should.ThrowAsync<AbpAuthorizationException>(() => Practices.GetMyPracticeForSchoolAsync(detail.Id, wrong));
        (await WithUnitOfWorkAsync(() => Repo<EducationQuestionExposure>().GetCountAsync())).ShouldBe(0);
        (await Practices.GetMyPracticeForSchoolAsync(detail.Id, school)).Feedback.ShouldNotBeNull();
        (await WithUnitOfWorkAsync(() => Repo<EducationQuestionExposure>().GetCountAsync())).ShouldBe(1);
    }
}
