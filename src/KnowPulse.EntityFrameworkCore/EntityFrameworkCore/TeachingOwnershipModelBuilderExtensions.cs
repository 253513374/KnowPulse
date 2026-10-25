using KnowPulse.AiQuestioning;
using KnowPulse.KnowledgeBase;
using KnowPulse.QuestionBank;
using Microsoft.EntityFrameworkCore;

namespace KnowPulse.EntityFrameworkCore;

public static class TeachingOwnershipModelBuilderExtensions
{
    public static void ConfigureTeachingOwnership(this ModelBuilder builder)
    {
        builder.Entity<KnowledgeItem>().HasIndex(x => new { x.TenantId, x.OwnerTeacherId });
        builder.Entity<Question>().HasIndex(x => new { x.TenantId, x.OwnerTeacherId });
        builder.Entity<AiQuestionGenerationTask>(b =>
        {
            b.HasIndex(x => new { x.TenantId, x.OwnerTeacherId });
            b.Property(x => x.KnowledgeItemIdsJson).IsRequired().HasDefaultValue("[]");
        });
    }
}
