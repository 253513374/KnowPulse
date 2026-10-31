using System;
using KnowPulse.Education;
using KnowPulse.Exam;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Volo.Abp.Identity;

namespace KnowPulse.EntityFrameworkCore;

public static class EducationAssessmentModelBuilderExtensions
{
    public static void ConfigureEducationAssessments(this ModelBuilder builder)
    {
        var utc = new ValueConverter<DateTime, DateTime>(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        var nullableUtc = new ValueConverter<DateTime?, DateTime?>(value => value,
            value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null);
        builder.Entity<TeachingAssignment>().HasAlternateKey(x => new
            { x.Id, x.SchoolId, x.AcademicTermId, x.SchoolClassId, x.TeacherUserId });
        builder.Entity<AcademicTerm>().HasAlternateKey(x => new { x.Id, x.SchoolId });
        builder.Entity<SchoolClass>().HasAlternateKey(x => new { x.Id, x.SchoolId, x.AcademicTermId });
        builder.Entity<SchoolMembership>().HasAlternateKey(x => new { x.Id, x.SchoolId, x.UserId });
        builder.Entity<StudentEnrollment>().HasAlternateKey(x => new { x.Id, x.SchoolId, x.SchoolClassId, x.StudentUserId });

        builder.Entity<EducationAssessmentTask>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "EducationAssessmentTasks", KnowPulseConsts.DbSchema, table =>
            {
                table.HasCheckConstraint("CK_EducationAssessmentTasks_CurrentRevision",
                    "(\"CurrentRevisionId\" IS NULL AND \"CurrentRevisionNumber\" = 0) OR (\"CurrentRevisionId\" IS NOT NULL AND \"CurrentRevisionNumber\" > 0)");
                table.HasCheckConstraint("CK_EducationAssessmentTasks_PublishedRevision",
                    "\"PublishedRevisionId\" IS NULL OR (\"CurrentRevisionId\" IS NOT NULL AND \"PublishedRevisionId\" = \"CurrentRevisionId\")");
            });
            b.ConfigureByConvention();
            b.Property(x => x.CreateFingerprint).IsRequired().HasMaxLength(64);
            b.Property(x => x.UpdatedAtUtc).HasConversion(utc);
            b.HasAlternateKey(x => new { x.Id, x.OwnerUserId });
            b.HasIndex(x => new { x.TenantId, x.OwnerUserId, x.UpdatedAtUtc });
            b.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.OwnerUserId)
                .HasConstraintName("FK_EduAssessmentTasks_Owner").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentTaskRevision>().WithMany()
                .HasForeignKey(x => new { x.CurrentRevisionId, TaskId = x.Id, RevisionNumber = x.CurrentRevisionNumber })
                .HasPrincipalKey(x => new { x.Id, x.TaskId, x.RevisionNumber })
                .HasConstraintName("FK_EduAssessmentTasks_CurrentRevision").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentTaskRevision>().WithMany()
                .HasForeignKey(x => new { x.PublishedRevisionId, TaskId = x.Id })
                .HasPrincipalKey(x => new { x.Id, x.TaskId })
                .HasConstraintName("FK_EduAssessmentTasks_PublishedRevision").OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<EducationAssessmentTaskRevision>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "EducationAssessmentTaskRevisions", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Title).IsRequired().HasMaxLength(256);
            b.Property(x => x.Instructions).IsRequired().HasMaxLength(8192);
            b.Property(x => x.SchoolClassName).IsRequired().HasMaxLength(128);
            b.Property(x => x.RequestFingerprint).IsRequired().HasMaxLength(64);
            b.Property(x => x.RosterFingerprint).HasMaxLength(64);
            b.Property(x => x.CurriculumSnapshotJson).IsRequired().HasMaxLength(200000);
            b.Property(x => x.SelectedStudentIdsJson).IsRequired().HasMaxLength(120000);
            b.Property(x => x.StartsAtUtc).HasConversion(utc);
            b.Property(x => x.EndsAtUtc).HasConversion(utc);
            b.Property(x => x.RecordedAtUtc).HasConversion(utc);
            b.Property(x => x.PublishedAtUtc).HasConversion(nullableUtc);
            b.HasAlternateKey(x => new { x.Id, x.TaskId });
            b.HasAlternateKey(x => new { x.Id, x.TaskId, x.RevisionNumber });
            b.HasAlternateKey(x => new { x.Id, x.TaskId, x.SchoolId, x.SchoolClassId });
            b.HasAlternateKey(x => new { x.Id, x.TaskId, x.ExamPaperId });
            b.HasIndex(x => new { x.TaskId, x.RevisionNumber }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.OwnerUserId, x.SchoolId, x.SchoolClassId });
            b.HasOne<EducationAssessmentTask>().WithMany().HasForeignKey(x => new { x.TaskId, x.OwnerUserId })
                .HasPrincipalKey(x => new { x.Id, x.OwnerUserId })
                .HasConstraintName("FK_EduAssessmentRevisions_TaskOwner").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentTaskRevision>().WithMany().HasForeignKey(x => new { x.BasedOnRevisionId, x.TaskId })
                .HasPrincipalKey(x => new { x.Id, x.TaskId })
                .HasConstraintName("FK_EduAssessmentRevisions_PreviousRevision").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<TeachingAssignment>().WithMany()
                .HasForeignKey(x => new { x.TeachingAssignmentId, x.SchoolId, x.AcademicTermId, x.SchoolClassId, TeacherUserId = x.OwnerUserId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId, x.AcademicTermId, x.SchoolClassId, x.TeacherUserId })
                .HasConstraintName("FK_EduAssessmentRevisions_TeachingAssignment").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<SchoolClass>().WithMany().HasForeignKey(x => new { x.SchoolClassId, x.SchoolId, x.AcademicTermId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId, x.AcademicTermId })
                .HasConstraintName("FK_EduAssessmentRevisions_Class").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<AcademicTerm>().WithMany().HasForeignKey(x => new { x.AcademicTermId, x.SchoolId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId })
                .HasConstraintName("FK_EduAssessmentRevisions_Term").OnDelete(DeleteBehavior.Restrict);
            // Paper ownership stays nullable for old enterprise papers; the use case checks it.
            b.HasOne<ExamPaper>().WithMany().HasForeignKey(x => x.ExamPaperId)
                .HasConstraintName("FK_EduAssessmentRevisions_Paper").OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<EducationAssessmentRecipient>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "EducationAssessmentRecipients", KnowPulseConsts.DbSchema, table =>
                table.HasCheckConstraint("CK_EducationAssessmentRecipients_CurrentAttempt",
                    "(\"CurrentAttemptId\" IS NULL AND \"StartedAttemptCount\" = 0) OR (\"CurrentAttemptId\" IS NOT NULL AND \"StartedAttemptCount\" > 0)"));
            b.ConfigureByConvention();
            b.Property(x => x.StudentName).IsRequired().HasMaxLength(256);
            b.Property(x => x.AccountName).IsRequired().HasMaxLength(256);
            b.HasAlternateKey(x => new { x.Id, x.TaskId, x.TaskRevisionId, x.StudentUserId });
            b.HasIndex(x => new { x.TaskRevisionId, x.StudentUserId }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.StudentUserId, x.SchoolId });
            b.HasOne<EducationAssessmentTaskRevision>().WithMany()
                .HasForeignKey(x => new { x.TaskRevisionId, x.TaskId, x.SchoolId, x.SchoolClassId })
                .HasPrincipalKey(x => new { x.Id, x.TaskId, x.SchoolId, x.SchoolClassId })
                .HasConstraintName("FK_EduAssessmentRecipients_RevisionClass").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<SchoolMembership>().WithMany().HasForeignKey(x => new { x.SchoolMembershipId, x.SchoolId, UserId = x.StudentUserId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId, x.UserId })
                .HasConstraintName("FK_EduAssessmentRecipients_Membership").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<StudentEnrollment>().WithMany()
                .HasForeignKey(x => new { x.StudentEnrollmentId, x.SchoolId, x.SchoolClassId, x.StudentUserId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId, x.SchoolClassId, x.StudentUserId })
                .HasConstraintName("FK_EduAssessmentRecipients_Enrollment").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentAttempt>().WithMany()
                .HasForeignKey(x => new { x.CurrentAttemptId, RecipientId = x.Id, AttemptNumber = x.StartedAttemptCount })
                .HasPrincipalKey(x => new { x.Id, x.RecipientId, x.AttemptNumber })
                .HasConstraintName("FK_EduAssessmentRecipients_CurrentAttempt").OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<EducationAssessmentAttempt>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "EducationAssessmentAttempts", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.StartedAtUtc).HasConversion(utc);
            b.Property(x => x.ExpiresAtUtc).HasConversion(utc);
            b.HasAlternateKey(x => new { x.Id, x.RecipientId, x.AttemptNumber });
            b.HasIndex(x => new { x.RecipientId, x.AttemptNumber }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.StudentUserId, x.ExpiresAtUtc });
            b.HasOne<EducationAssessmentRecipient>().WithMany()
                .HasForeignKey(x => new { x.RecipientId, x.TaskId, x.TaskRevisionId, x.StudentUserId })
                .HasPrincipalKey(x => new { x.Id, x.TaskId, x.TaskRevisionId, x.StudentUserId })
                .HasConstraintName("FK_EduAssessmentAttempts_Recipient").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationAssessmentTaskRevision>().WithMany()
                .HasForeignKey(x => new { x.TaskRevisionId, x.TaskId, x.ExamPaperId })
                .HasPrincipalKey(x => new { x.Id, x.TaskId, x.ExamPaperId })
                .HasConstraintName("FK_EduAssessmentAttempts_RevisionPaper").OnDelete(DeleteBehavior.Restrict);
        });
    }
}
