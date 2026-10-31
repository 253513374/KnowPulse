using System;
using KnowPulse.Education;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace KnowPulse.EntityFrameworkCore;

public static class EducationQuestionExposureModelBuilderExtensions
{
    public static void ConfigureEducationQuestionExposures(this ModelBuilder builder)
    {
        builder.Entity<EducationQuestionExposure>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "EducationQuestionExposures", KnowPulseConsts.DbSchema); b.ConfigureByConvention();
            b.Property(x => x.ExposedAtUtc).HasConversion(new ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc)));
            b.HasIndex(x => new { x.TenantId, x.StudentUserId, x.EducationSubjectId, x.OriginalQuestionId, x.ExposedAtUtc }).HasDatabaseName("IX_EduExposures_StudentOriginal");
            b.HasIndex(x => new { x.StudentUserId, x.RecipientId, x.ResultReleaseId, x.PaperQuestionId }).IsUnique().HasFilter("\"Source\" = 0").HasDatabaseName("IX_EduExposures_ReviewOnce");
            b.HasIndex(x => new { x.StudentUserId, x.RecipientId, x.ResultReleaseId, x.FeedbackRevisionId, x.PaperQuestionId }).IsUnique().HasFilter("\"Source\" = 1").HasDatabaseName("IX_EduExposures_FeedbackOnce");
            b.HasOne<EducationAssessmentRecipient>().WithMany().HasForeignKey(x => new { x.RecipientId, x.TaskId, x.TaskRevisionId, x.StudentUserId })
                .HasPrincipalKey(x => new { x.Id, x.TaskId, x.TaskRevisionId, x.StudentUserId }).HasConstraintName("FK_EduExposures_Recipient").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentResultRelease>().WithMany().HasForeignKey(x => new { x.ResultReleaseId, x.TaskId })
                .HasPrincipalKey(x => new { x.Id, x.TaskId }).HasConstraintName("FK_EduExposures_Release").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentTaskRevision>().WithMany().HasForeignKey(x => new { x.TaskRevisionId, x.TaskId, x.ExamPaperId })
                .HasPrincipalKey(x => new { x.Id, x.TaskId, x.ExamPaperId }).HasConstraintName("FK_EduExposures_PaperRevision").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ExamPaperQuestion>().WithMany().HasForeignKey(x => new { x.PaperQuestionId, PaperId = x.ExamPaperId })
                .HasPrincipalKey(x => new { x.Id, x.PaperId }).HasConstraintName("FK_EduExposures_PaperQuestion").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<QuestionDefinitionRevision>().WithMany().HasForeignKey(x => new { x.QuestionDefinitionRevisionId, QuestionId = x.OriginalQuestionId })
                .HasPrincipalKey(x => new { x.Id, x.QuestionId }).HasConstraintName("FK_EduExposures_Definition").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationStudentFeedbackRevision>().WithMany().HasForeignKey(x => new { x.FeedbackRevisionId, x.FeedbackId, x.StudentUserId })
                .HasPrincipalKey(x => new { x.Id, x.FeedbackId, x.StudentUserId }).HasConstraintName("FK_EduExposures_Feedback").OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_EduExposures_Source", "(\"Source\" = 0 AND \"FeedbackId\" IS NULL AND \"FeedbackRevisionId\" IS NULL) OR " +
                "(\"Source\" = 1 AND \"FeedbackId\" IS NOT NULL AND \"FeedbackRevisionId\" IS NOT NULL)"));
        });
    }
}
