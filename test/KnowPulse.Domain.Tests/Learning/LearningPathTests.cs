using System;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.Learning;

public class LearningPathTests
{
    [Fact]
    public void Assignment_Should_Require_Target_For_Department_Position_And_JobLevel()
    {
        Should.Throw<BusinessException>(() => new LearningPathAssignment(
                Guid.NewGuid(),
                null,
                Guid.NewGuid(),
                LearningPathAssignmentScopeType.Department,
                null))
            .Code.ShouldBe(KnowPulseDomainErrorCodes.LearningPathAssignmentTargetRequired);
    }

    [Fact]
    public void Assignment_Should_Clear_Target_For_All()
    {
        var assignment = new LearningPathAssignment(
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            LearningPathAssignmentScopeType.All,
            Guid.NewGuid());

        assignment.TargetId.ShouldBeNull();
    }

    [Fact]
    public void Deactivate_Should_Disable_Path_And_Increment_Config_Version()
    {
        var path = new LearningPath(Guid.NewGuid(), null, "Onboarding");

        path.Deactivate();

        path.IsActive.ShouldBeFalse();
        path.ConfigVersion.ShouldBe(2);

        path.Deactivate();
        path.ConfigVersion.ShouldBe(2);
    }

    [Fact]
    public void Read_Record_Should_Track_Last_Read_And_Completed_Time()
    {
        var firstReadTime = DateTime.UtcNow;
        var record = new LearningKnowledgeReadRecord(
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            firstReadTime,
            markCompleted: false);

        record.ReadCount.ShouldBe(1);
        record.LastReadTime.ShouldBe(firstReadTime);
        record.CompletedTime.ShouldBeNull();

        var completedTime = firstReadTime.AddMinutes(10);
        record.RecordRead(completedTime, markCompleted: true);

        record.ReadCount.ShouldBe(2);
        record.LastReadTime.ShouldBe(completedTime);
        record.CompletedTime.ShouldBe(completedTime);
    }
}
