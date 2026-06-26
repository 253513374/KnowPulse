using System;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.Organization;

public class UserOrganizationAssignmentTests
{
    [Fact]
    public void Should_Change_User()
    {
        var assignment = new UserOrganizationAssignment(
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            Guid.NewGuid());
        var newUserId = Guid.NewGuid();

        assignment.ChangeUser(newUserId);

        assignment.UserId.ShouldBe(newUserId);
    }

    [Fact]
    public void Should_Not_Accept_Negative_Security_Level()
    {
        var assignment = new UserOrganizationAssignment(
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            Guid.NewGuid());

        Should.Throw<BusinessException>(() => assignment.SetSecurityLevel(-1))
            .Code.ShouldBe(KnowPulseDomainErrorCodes.InvalidSecurityLevel);
    }
}
