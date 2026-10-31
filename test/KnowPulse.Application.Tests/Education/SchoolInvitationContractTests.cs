using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc.Conventions;
using Xunit;

namespace KnowPulse.Education;

public class SchoolInvitationContractTests
{
    [Fact]
    public void Invitation_Contracts_Should_Separate_Recipient_Metadata_From_Manager_Authority_And_Reasons()
    {
        var routes = new List<string>();
        foreach (var (name, path, manager) in new[] { ("GetMyInvitationsAsync", "my-invitations", false), ("GetMyInvitationAsync", "my-invitation/{invitationId}", false),
            ("RespondToInvitationAsync", "respond-to-invitation/{invitationId}", false), ("GetInvitationsAsync", "invitations/{schoolId}", true), ("GetInvitationAsync", "invitation", true),
            ("CreateInvitationAsync", "invitation/{schoolId}", true), ("RevokeInvitationAsync", "revoke-invitation", true) }) {
            var type = typeof(EducationSchoolAppService); var method = type.GetMethod(name)!;
            foreach (var m in new[] { method, typeof(IEducationSchoolAppService).GetMethod(name)! }) {
                var attributes = m.GetCustomAttributes<AuthorizeAttribute>().ToList(); attributes.ShouldNotBeEmpty();
                if (manager) attributes.Select(x => x.Policy).ShouldContain(EducationPermissions.SchoolManage);
            }
            var action = new ActionModel(method, method.GetCustomAttributes(true)) { ActionName = name, Controller = new ControllerModel(type.GetTypeInfo(), []) };
            foreach (var p in method.GetParameters()) action.Parameters.Add(new ParameterModel(p, p.GetCustomAttributes(true)) { Action = action, ParameterName = p.Name! });
            var route = new ConventionalRouteBuilder(Options.Create(new AbpConventionalControllerOptions())).Build("app", "EducationSchool", action, name.StartsWith("Get") ? "GET" : "POST", new ConventionalControllerSetting(type.Assembly, "app", "Default"));
            if (route != "api/app/education-school/" + path) routes.Add(name + ": " + route);
        }
        routes.ShouldBeEmpty(string.Join(Environment.NewLine, routes));
        typeof(MySchoolInvitationDto).GetProperties().Select(x => x.Name).Order().ShouldBe(new[] { "Id", "SchoolId", "SchoolName", "SchoolCode", "TargetUserId", "Role", "EffectiveFromUtc", "EffectiveToUtc", "ExpiresAtUtc", "RecordedAtUtc", "Status", "CompletedAtUtc", "MembershipId", "ResponseOperationId", "DeclineReason", "CanRespond", "ConcurrencyStamp" }.Order());
    }
}
