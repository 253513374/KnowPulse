using System;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace KnowPulse.EntityFrameworkCore;

public static class KnowPulseDbContextEducationQuestionDefinitionExtensions
{
    public static void ConfigureEducationQuestionDefinitions(this ModelBuilder builder)
    {
        var utc = new ValueConverter<DateTime, DateTime>(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        builder.Entity<QuestionReview>(b =>
        {
            b.Property(x => x.OperationRequestHash).HasMaxLength(64);
            b.Property(x => x.ExpectedConcurrencyStamp).HasMaxLength(40);
            b.Property(x => x.ReviewTime).HasConversion(utc);
            b.HasAlternateKey(x => new { x.Id, x.QuestionId });
        });
        builder.Entity<QuestionDefinitionRevision>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "QuestionDefinitionRevisions", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.DefinitionHash).IsRequired().HasMaxLength(64);
            b.Property(x => x.DefinitionJson).IsRequired().HasMaxLength(QuestionDefinitionRevision.MaxDefinitionJsonLength);
            b.Property(x => x.ReviewedAtUtc).HasConversion(utc);
            b.HasAlternateKey(x => new { x.Id, x.QuestionId });
            b.HasIndex(x => new { x.QuestionId, x.RevisionNumber }).IsUnique();
            b.HasIndex(x => x.ReviewOperationId).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.PersonalOwnerUserId, x.QuestionId });
            b.HasOne<Question>().WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<QuestionReview>().WithMany().HasForeignKey(x => new { x.ReviewOperationId, x.QuestionId })
                .HasPrincipalKey(x => new { x.Id, x.QuestionId }).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<Question>(b => b.HasOne<QuestionDefinitionRevision>().WithMany()
            .HasForeignKey(x => new { x.ApprovedDefinitionRevisionId, x.Id })
            .HasPrincipalKey(x => new { x.Id, x.QuestionId }).OnDelete(DeleteBehavior.Restrict));
        builder.Entity<ExamPaperQuestion>(b => b.HasOne<QuestionDefinitionRevision>().WithMany()
            .HasForeignKey(x => new { x.QuestionDefinitionRevisionId, x.OriginalQuestionId })
            .HasPrincipalKey(x => new { x.Id, x.QuestionId }).OnDelete(DeleteBehavior.Restrict));
    }
}
