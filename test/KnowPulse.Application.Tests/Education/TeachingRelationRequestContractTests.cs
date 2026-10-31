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

public class TeachingRelationRequestContractTests
{
    [Fact]
    public void Relation_Request_Routes_Should_Keep_Manager_Authority_Separate_And_Never_Accept_A_Target_Teacher_From_The_Client()
    {
        var failures = new List<string>();
        foreach (var (name, path, manager) in new[] { ("GetMyTeachingRelationsAsync", "my-teaching-relations/{schoolId}", false), ("GetMyRelationRequestsAsync", "my-relation-requests/{schoolId}", false),
            ("GetMyRelationRequestAsync", "my-relation-request/{requestId}", false), ("SubmitRelationRequestAsync", "submit-relation-request/{schoolId}", false),
            ("WithdrawMyRelationRequestAsync", "withdraw-my-relation-request/{requestId}", false), ("GetRelationRequestsAsync", "relation-requests/{schoolId}", true),
            ("GetRelationRequestAsync", "relation-request", true), ("ResolveRelationRequestAsync", "resolve-relation-request", true) }) {
            var type = typeof(EducationSchoolAppService); var method = type.GetMethod(name)!;
            foreach (var m in new[] { method, typeof(IEducationSchoolAppService).GetMethod(name)! }) {
                var attributes = m.GetCustomAttributes<AuthorizeAttribute>().ToList(); attributes.ShouldNotBeEmpty(); if (manager) attributes.Select(x => x.Policy).ShouldContain(EducationPermissions.SchoolManage);
            }
            var action = new ActionModel(method, method.GetCustomAttributes(true)) { ActionName = name, Controller = new ControllerModel(type.GetTypeInfo(), []) };
            foreach (var p in method.GetParameters()) action.Parameters.Add(new ParameterModel(p, p.GetCustomAttributes(true)) { Action = action, ParameterName = p.Name! });
            var route = new ConventionalRouteBuilder(Options.Create(new AbpConventionalControllerOptions())).Build("app", "EducationSchool", action, name.StartsWith("Get") ? "GET" : "POST", new ConventionalControllerSetting(type.Assembly, "app", "Default"));
            if (route != "api/app/education-school/" + path) failures.Add(name + ": " + route);
        }
        failures.ShouldBeEmpty(string.Join(Environment.NewLine, failures));
        typeof(ResolveTeachingRelationRequestDto).GetProperties().Select(x => x.Name).ShouldNotContain("TeacherUserId");
        typeof(SubmitTeachingRelationRequestDto).GetProperties().Select(x => x.Name).ShouldNotContain("TeacherUserId");
    }
}
