using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Uow;

namespace KnowPulse.Teaching;

public class TeachingRoleDataSeedContributor(
    IdentityRoleManager roleManager,
    IGuidGenerator guidGenerator,
    IPermissionDataSeeder permissions) : IDataSeedContributor, ITransientDependency
{
    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        var teacher = await roleManager.FindByNameAsync("teacher");
        if (teacher == null)
        {
            teacher = new IdentityRole(guidGenerator.Create(), "teacher", context.TenantId) { IsDefault = true, IsPublic = false };
            (await roleManager.CreateAsync(teacher)).CheckErrors();
        }

        if (await roleManager.FindByNameAsync("student") == null)
            (await roleManager.CreateAsync(new IdentityRole(guidGenerator.Create(), "student", context.TenantId) { IsPublic = false })).CheckErrors();

        await permissions.SeedAsync("R", "teacher", [
            "KnowPulse.KnowledgeBase", "KnowPulse.KnowledgeBase.Items", "KnowPulse.KnowledgeBase.Versions",
            "KnowPulse.KnowledgeBase.Attachments", "KnowPulse.KnowledgeBase.PermissionScopes",
            "KnowPulse.QuestionBank", "KnowPulse.QuestionBank.Questions", "KnowPulse.QuestionBank.AiGeneration",
            "KnowPulse.Exam", "KnowPulse.Exam.Manage", "KnowPulse.Exam.Results"
        ], context.TenantId);
        await permissions.SeedAsync("R", "student", ["KnowPulse.Exam", "KnowPulse.Exam.Participate"], context.TenantId);
    }
}
