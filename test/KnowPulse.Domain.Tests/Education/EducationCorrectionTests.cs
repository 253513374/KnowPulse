using System;
using KnowPulse.Education;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.Domain.Tests.Education;

public class EducationCorrectionTests
{
    [Fact]
    public void Correction_Should_Require_Solution_And_Checking_Without_Forcing_An_Explanation_Of_The_Original_Thinking()
    {
        new EducationCorrectionContent { CurrentSolution = "新过程", CheckingStrategy = "核对" }.Validate(submitted: true);
        new EducationCorrectionContent { Images = [new(Guid.NewGuid(), 270)], CheckingStrategy = "核对图片步骤" }.Validate(submitted: true);
        Should.Throw<BusinessException>(() => new EducationCorrectionContent { CurrentSolution = "过程" }.Validate(submitted: true));
        Should.Throw<BusinessException>(() => new EducationCorrectionContent { CheckingStrategy = "检查" }.Validate(submitted: true));
    }
    [Fact]
    public void Correction_Should_Reject_Duplicate_Images_Invalid_Rotation_And_Unbounded_Content()
    {
        var id = Guid.NewGuid();
        Should.Throw<BusinessException>(() => new EducationCorrectionContent { Images = [new(id), new(id)] }.Validate());
        Should.Throw<BusinessException>(() => new EducationCorrectionContent { Images = [new(id, 45)] }.Validate());
        Should.Throw<BusinessException>(() => new EducationCorrectionContent { CurrentSolution = new('字', 8193) }.Validate());
        Should.Throw<BusinessException>(() => new EducationCorrectionInstructions { Title = "订正", Instructions = "说明", StartsAtUtc = DateTime.Now, DueAtUtc = DateTime.Now.AddDays(1), EstimatedMinutes = 15 }.Validate());
    }
}
