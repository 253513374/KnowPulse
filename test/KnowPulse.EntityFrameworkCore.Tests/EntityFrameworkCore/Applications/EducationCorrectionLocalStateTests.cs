using System;
using System.Text.Json;
using KnowPulse.Blazor.Client.Pages.Education;
using KnowPulse.Education;
using Shouldly;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public class EducationCorrectionLocalStateTests
{
    [Fact]
    public void Local_Correction_Should_Preserve_Original_Base_After_Refresh_And_Bind_Exact_Preview()
    {
        var account = Guid.NewGuid(); var delivery = Guid.NewGuid(); var original = Guid.NewGuid();
        var state = new EducationCorrectionLocalState { AccountId = account, DeliveryId = delivery, BasedOnSubmissionId = original, Content = new() { CurrentSolution = "本机文字", CheckingStrategy = "验算" } };
        var restored = JsonSerializer.Deserialize<EducationCorrectionLocalState>(JsonSerializer.Serialize(state))!; restored.Validate(account, null, delivery);
        restored.ConflictsWith(original).ShouldBeFalse(); restored.ConflictsWith(Guid.NewGuid()).ShouldBeTrue(); restored.BasedOnSubmissionId.ShouldBe(original);
        var preview = new EducationCorrectionSubmissionPreviewDto { DeliveryId = delivery, ExpectedSubmissionId = original, Content = restored.Content };
        restored.MatchesPreview(preview).ShouldBeTrue(); restored.Content = restored.Content with { CheckingStrategy = "预览后修改" }; restored.MatchesPreview(preview).ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => restored.Validate(Guid.NewGuid(), null, delivery));
        Should.Throw<InvalidOperationException>(() => restored.Validate(account, Guid.NewGuid(), delivery));
        Should.Throw<InvalidOperationException>(() => restored.Validate(account, null, Guid.NewGuid()));
    }
    [Fact]
    public void Pending_Correction_Should_Roundtrip_And_Reject_Mismatched_Command_Or_Silent_Image_Drop()
    {
        var state = new EducationCorrectionLocalState { AccountId = Guid.NewGuid(), DeliveryId = Guid.NewGuid(), Content = new() { CurrentSolution = "已预览文字", CheckingStrategy = "检查" } };
        state.Pending = new() { OperationId = Guid.NewGuid(), Content = state.Content, PreviewFingerprint = new('a', 64), ConfirmSharingWithTeacher = true };
        state.PendingFingerprint = EducationCorrectionFingerprint.Submit(state.DeliveryId, state.Pending);
        var copy = JsonSerializer.Deserialize<EducationCorrectionLocalState>(JsonSerializer.Serialize(state))!; copy.Validate(state.AccountId, null, state.DeliveryId);
        copy.Content = copy.Content with { CurrentSolution = "另一份文字" }; Should.Throw<InvalidOperationException>(() => copy.Validate(state.AccountId, null, state.DeliveryId));
        state.ImageOperationId = Guid.NewGuid(); state.ImageHash = new('b', 64); state.ImageName = "pending.png"; Should.Throw<InvalidOperationException>(() => state.Validate(state.AccountId, null, state.DeliveryId));
        state.Pending = null; state.PendingFingerprint = null; state.Validate(state.AccountId, null, state.DeliveryId); state.ImageOperationId = null;
        Should.Throw<InvalidOperationException>(() => state.Validate(state.AccountId, null, state.DeliveryId));
    }
}
