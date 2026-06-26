using System;
using KnowPulse.Organization;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.Organization;

public class DepartmentTests
{
    [Fact]
    public void Should_Create_Active_Department()
    {
        var department = new Department(Guid.NewGuid(), null, "RD", "Research", securityLevel: 2);

        department.Code.ShouldBe("RD");
        department.Name.ShouldBe("Research");
        department.SecurityLevel.ShouldBe(2);
        department.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Should_Reject_Negative_Security_Level()
    {
        var exception = Should.Throw<BusinessException>(() =>
            new Department(Guid.NewGuid(), null, "RD", "Research", securityLevel: -1));

        exception.Code.ShouldBe(KnowPulseDomainErrorCodes.InvalidSecurityLevel);
    }
}
