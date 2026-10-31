using System;
using KnowPulse.Education;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Volo.Abp.Identity;

namespace KnowPulse.EntityFrameworkCore;

public static class EducationLearningReportModelBuilderExtensions
{
    public static void ConfigureEducationLearningReports(this ModelBuilder builder)
    {
        builder.Entity<TeachingAssignment>().HasAlternateKey(x => new { x.Id, x.SchoolId, x.AcademicTermId, x.SchoolClassId, x.TeacherUserId, x.EducationSubjectId }).HasName("AK_EduTeaching_ReportScope");
        builder.Entity<TextbookVersion>().HasAlternateKey(x => new { x.Id, x.SchoolId, x.EducationSubjectId }).HasName("AK_EduBooks_ReportScope");
        builder.Entity<EducationMasteryRule>(b =>
        {
            Table(b, "EducationMasteryRules");
            b.HasAlternateKey(x => new { x.Id, x.SchoolId, x.EducationSubjectId }).HasName("AK_EduMasteryRules_Scope");
            b.HasIndex(x => new { x.SchoolId, x.EducationSubjectId }).IsUnique().HasDatabaseName("IX_EduMasteryRules_Subject");
            b.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).HasConstraintName("FK_EduMasteryRules_School").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationSubject>().WithMany().HasForeignKey(x => x.EducationSubjectId).HasConstraintName("FK_EduMasteryRules_Subject").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationMasteryRuleRevision>().WithMany().HasForeignKey(x => new { x.CurrentRevisionId, RuleId = x.Id })
                .HasPrincipalKey(x => new { x.Id, x.RuleId }).HasConstraintName("FK_EduMasteryRules_Current").OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_EduMasteryRules_Revision", "(\"RevisionNumber\" = 0 AND \"CurrentRevisionId\" IS NULL) OR (\"RevisionNumber\" > 0 AND \"CurrentRevisionId\" IS NOT NULL)"));
        });
        builder.Entity<EducationMasteryRuleRevision>(b =>
        {
            Table(b, "EducationMasteryRuleRevisions"); Text(b, "Reason", 2048); Text(b, "ExpectedConcurrencyStamp", 40); Text(b, "RequestFingerprint", 64); Utc(b, "RecordedAtUtc");
            b.Property(x => x.TargetRatio).HasPrecision(10, 4);
            b.HasAlternateKey(x => new { x.Id, x.RuleId }).HasName("AK_EduMasteryVersions_Root");
            b.HasIndex(x => new { x.RuleId, x.RevisionNumber }).IsUnique().HasDatabaseName("IX_EduMasteryVersions_Sequence");
            b.HasOne<EducationMasteryRule>().WithMany().HasForeignKey(x => new { x.RuleId, x.SchoolId, x.EducationSubjectId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId, x.EducationSubjectId }).HasConstraintName("FK_EduMasteryVersions_Root").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationMasteryRuleRevision>().WithMany().HasForeignKey(x => new { x.BasedOnRevisionId, x.RuleId })
                .HasPrincipalKey(x => new { x.Id, x.RuleId }).HasConstraintName("FK_EduMasteryVersions_Previous").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.ActorUserId).HasConstraintName("FK_EduMasteryVersions_Actor").OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_EduMasteryVersions_Values", "\"RevisionNumber\" > 0 AND \"WindowDays\" BETWEEN 1 AND 730 AND \"MinimumIndependentQuestionCount\" BETWEEN 1 AND 1000 AND " +
                "\"MinimumAssessmentCount\" BETWEEN 1 AND 1000 AND \"MaximumLatestEvidenceAgeDays\" BETWEEN 1 AND \"WindowDays\" AND CAST(\"TargetRatio\" AS numeric) > 0 AND CAST(\"TargetRatio\" AS numeric) <= 1 AND length(trim(\"Reason\")) > 0"));
        });
        builder.Entity<EducationLearningReport>(b =>
        {
            Table(b, "EducationLearningReports"); Text(b, "SelectedTaskIdsJson", 32000); Text(b, "CreateRequestFingerprint", 64); Utc(b, "WindowStartsAtUtc"); Utc(b, "WindowEndsAtUtc");
            b.HasAlternateKey(x => new { x.Id, x.OwnerUserId }).HasName("AK_EduReports_Owner");
            b.HasAlternateKey(x => new { x.Id, x.OwnerUserId, x.SchoolId, x.SchoolClassId }).HasName("AK_EduReports_Class");
            b.HasIndex(x => new { x.TenantId, x.OwnerUserId, x.TeachingAssignmentId, x.CreationTime }).HasDatabaseName("IX_EduReports_Teaching");
            b.HasOne<TeachingAssignment>().WithMany().HasForeignKey(x => new { x.TeachingAssignmentId, x.SchoolId, x.AcademicTermId, x.SchoolClassId, x.OwnerUserId, x.EducationSubjectId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId, x.AcademicTermId, x.SchoolClassId, x.TeacherUserId, x.EducationSubjectId }).HasConstraintName("FK_EduReports_Teaching").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<TextbookVersion>().WithMany().HasForeignKey(x => new { x.TextbookVersionId, x.SchoolId, x.EducationSubjectId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId, x.EducationSubjectId }).HasConstraintName("FK_EduReports_Book").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationLearningReportJob>().WithMany().HasForeignKey(x => new { x.CurrentJobId, ReportId = x.Id, x.OwnerUserId })
                .HasPrincipalKey(x => new { x.Id, x.ReportId, x.OwnerUserId }).HasConstraintName("FK_EduReports_CurrentJob").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationLearningReportSnapshot>().WithMany().HasForeignKey(x => new { x.CurrentSnapshotId, ReportId = x.Id, x.OwnerUserId, x.SchoolId, x.SchoolClassId })
                .HasPrincipalKey(x => new { x.Id, x.ReportId, x.OwnerUserId, x.SchoolId, x.SchoolClassId }).HasConstraintName("FK_EduReports_CurrentSnapshot").OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_EduReports_State", "\"WindowStartsAtUtc\" < \"WindowEndsAtUtc\" AND ((\"Generation\" = 0 AND \"CurrentJobId\" IS NULL) OR (\"Generation\" > 0 AND \"CurrentJobId\" IS NOT NULL))"));
        });
        builder.Entity<EducationLearningReportJob>(b =>
        {
            Table(b, "EducationLearningReportJobs"); Json(b, "SourceJson", EducationLearningReportJson.MaxSourceBytes, "CK_EduReportJobs_SourceSize");
            Json(b, "CurriculumJson", EducationLearningReportJson.MaxSourceBytes, "CK_EduReportJobs_CurriculumSize");
            Text(b, "SourceFingerprint", 64); Text(b, "RequestFingerprint", 64); Text(b, "ExpectedConcurrencyStamp", 40);
            Utc(b, "RequestedAtUtc"); Utc(b, "NextAttemptAtUtc"); Utc(b, "NextSourceCheckAtUtc"); NullableUtc(b, "HeartbeatAtUtc"); NullableUtc(b, "LeaseExpiresAtUtc"); NullableUtc(b, "CompletedAtUtc");
            b.HasAlternateKey(x => new { x.Id, x.ReportId }).HasName("AK_EduReportJobs_Report");
            b.HasAlternateKey(x => new { x.Id, x.ReportId, x.OwnerUserId }).HasName("AK_EduReportJobs_Owner");
            b.HasAlternateKey(x => new { x.Id, x.ReportId, x.OwnerUserId, x.Generation }).HasName("AK_EduReportJobs_Generation");
            b.HasIndex(x => new { x.ReportId, x.Generation }).IsUnique().HasDatabaseName("IX_EduReportJobs_Generation");
            b.HasIndex(x => new { x.Status, x.NextAttemptAtUtc, x.LeaseExpiresAtUtc }).HasDatabaseName("IX_EduReportJobs_Pending");
            b.HasOne<EducationLearningReport>().WithMany().HasForeignKey(x => new { x.ReportId, x.OwnerUserId })
                .HasPrincipalKey(x => new { x.Id, x.OwnerUserId }).HasConstraintName("FK_EduReportJobs_Report").OnDelete(DeleteBehavior.Restrict);
            foreach (var pointer in new[] { "PreviousJobId", "RetryOfJobId" })
                b.HasOne<EducationLearningReportJob>().WithMany().HasForeignKey(pointer, "ReportId").HasPrincipalKey(x => new { x.Id, x.ReportId }).HasConstraintName("FK_EduReportJobs_" + pointer).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationLearningReportSnapshot>().WithMany().HasForeignKey(x => new { x.SnapshotId, JobId = x.Id, x.ReportId })
                .HasPrincipalKey(x => new { x.Id, x.JobId, x.ReportId }).HasConstraintName("FK_EduReportJobs_Snapshot").OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_EduReportJobs_State", "\"Generation\" > 0 AND \"AttemptCount\" >= 0 AND \"StudentCount\" BETWEEN 0 AND 3000 AND \"CompletedStudentCount\" BETWEEN 0 AND \"StudentCount\" AND " +
                "((\"Status\" = 0 AND \"RunId\" IS NULL AND \"SnapshotId\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"AttemptCount\" = 0) OR " +
                "(\"Status\" = 1 AND \"RunId\" IS NOT NULL AND \"LeaseExpiresAtUtc\" IS NOT NULL AND \"HeartbeatAtUtc\" IS NOT NULL AND \"SnapshotId\" IS NULL AND \"AttemptCount\" > 0) OR " +
                "(\"Status\" = 2 AND \"SnapshotId\" IS NOT NULL AND \"CompletedAtUtc\" IS NOT NULL AND \"LeaseExpiresAtUtc\" IS NULL) OR " +
                "(\"Status\" = 3 AND \"FailureStage\" BETWEEN 1 AND 3 AND \"LeaseExpiresAtUtc\" IS NULL AND \"SnapshotId\" IS NULL AND \"AttemptCount\" > 0) OR " +
                "(\"Status\" IN (4, 5) AND \"RunId\" IS NOT NULL AND \"CompletedAtUtc\" IS NOT NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"SnapshotId\" IS NULL))"));
        });
        builder.Entity<EducationLearningReportSnapshot>(b =>
        {
            Table(b, "EducationLearningReportSnapshots"); Json(b, "SummaryJson", EducationLearningReportJson.MaxSourceBytes, "CK_EduReportSnapshots_SummarySize"); Text(b, "SourceFingerprint", 64); Utc(b, "CalculatedAtUtc");
            b.HasAlternateKey(x => new { x.Id, x.ReportId, x.OwnerUserId, x.SchoolId, x.SchoolClassId }).HasName("AK_EduReportSnapshots_Class");
            b.HasAlternateKey(x => new { x.Id, x.JobId, x.ReportId }).HasName("AK_EduReportSnapshots_Job");
            b.HasIndex(x => new { x.ReportId, x.Generation }).IsUnique().HasDatabaseName("IX_EduReportSnapshots_Generation");
            b.HasOne<EducationLearningReport>().WithMany().HasForeignKey(x => new { x.ReportId, x.OwnerUserId, x.SchoolId, x.SchoolClassId })
                .HasPrincipalKey(x => new { x.Id, x.OwnerUserId, x.SchoolId, x.SchoolClassId }).HasConstraintName("FK_EduReportSnapshots_Report").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationLearningReportJob>().WithMany().HasForeignKey(x => new { x.JobId, x.ReportId, x.OwnerUserId, x.Generation })
                .HasPrincipalKey(x => new { x.Id, x.ReportId, x.OwnerUserId, x.Generation }).HasConstraintName("FK_EduReportSnapshots_Job").OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_EduReportSnapshots_Identity", "\"Id\" = \"JobId\" AND \"Generation\" > 0"));
        });
        builder.Entity<EducationLearningReportStudentSnapshot>(b =>
        {
            Table(b, "EducationLearningReportStudentSnapshots"); Json(b, "ResultJson", EducationLearningReportJson.MaxStudentBytes, "CK_EduReportStudents_ResultSize");
            b.HasIndex(x => new { x.SnapshotId, x.StudentUserId }).IsUnique().HasDatabaseName("IX_EduReportStudents_OnePerStudent");
            b.HasOne<EducationLearningReportSnapshot>().WithMany().HasForeignKey(x => new { x.SnapshotId, x.ReportId, x.OwnerUserId, x.SchoolId, x.SchoolClassId })
                .HasPrincipalKey(x => new { x.Id, x.ReportId, x.OwnerUserId, x.SchoolId, x.SchoolClassId }).HasConstraintName("FK_EduReportStudents_Snapshot").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<StudentEnrollment>().WithMany().HasForeignKey(x => new { x.StudentEnrollmentId, x.SchoolId, x.SchoolClassId, x.StudentUserId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId, x.SchoolClassId, x.StudentUserId }).HasConstraintName("FK_EduReportStudents_Enrollment").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<SchoolMembership>().WithMany().HasForeignKey(x => new { x.SchoolMembershipId, x.SchoolId, x.StudentUserId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId, x.UserId }).HasConstraintName("FK_EduReportStudents_Membership").OnDelete(DeleteBehavior.Restrict);
        });
    }
    public static void ConfigureStudentGrowth(this ModelBuilder builder)
    {
        builder.Entity<EducationStudentGrowthSnapshot>(b =>
        {
            Table(b, "EducationStudentGrowthSnapshots"); Json(b, "ResultJson", EducationLearningReportJson.MaxStudentBytes, "CK_EduGrowth_ResultSize");
            Text(b, "SourceFingerprint", 64); Utc(b, "WindowStartsAtUtc"); Utc(b, "WindowEndsAtUtc"); Utc(b, "CalculatedAtUtc");
            b.HasIndex(x => new { x.StudentUserId, x.ContextId, x.WindowStartsAtUtc, x.WindowEndsAtUtc, x.SourceFingerprint }).IsUnique().HasDatabaseName("IX_EduGrowth_SourceVersion");
            b.HasOne<StudentEnrollment>().WithMany().HasForeignKey(x => new { x.StudentEnrollmentId, x.SchoolId, x.SchoolClassId, x.StudentUserId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId, x.SchoolClassId, x.StudentUserId }).HasConstraintName("FK_EduGrowth_Enrollment").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<SchoolMembership>().WithMany().HasForeignKey(x => new { x.SchoolMembershipId, x.SchoolId, x.StudentUserId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId, x.UserId }).HasConstraintName("FK_EduGrowth_Membership").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<TeachingAssignment>().WithMany().HasForeignKey(x => new { x.TeachingAssignmentId, x.SchoolId, x.AcademicTermId, x.SchoolClassId, x.TeacherUserId, x.EducationSubjectId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId, x.AcademicTermId, x.SchoolClassId, x.TeacherUserId, x.EducationSubjectId }).HasConstraintName("FK_EduGrowth_Teaching").OnDelete(DeleteBehavior.Restrict);
            b.HasOne<TextbookVersion>().WithMany().HasForeignKey(x => new { x.TextbookVersionId, x.SchoolId, x.EducationSubjectId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId, x.EducationSubjectId }).HasConstraintName("FK_EduGrowth_Book").OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_EduGrowth_Window", "\"WindowStartsAtUtc\" < \"WindowEndsAtUtc\" AND \"WindowEndsAtUtc\" <= \"CalculatedAtUtc\""));
        });
    }
    private static void Table<T>(EntityTypeBuilder<T> b, string name) where T : class
    { b.ToTable(KnowPulseConsts.DbTablePrefix + name, KnowPulseConsts.DbSchema); b.ConfigureByConvention(); }
    private static void Text<T>(EntityTypeBuilder<T> b, string name, int maximum) where T : class
        => b.Property<string>(name).IsRequired().HasMaxLength(maximum);
    private static void Json<T>(EntityTypeBuilder<T> b, string name, int maximum, string check) where T : class
    { b.Property<string>(name).IsRequired().HasColumnType("text"); b.ToTable(t => t.HasCheckConstraint(check, $"length(\"{name}\") BETWEEN 1 AND {maximum}")); }
    private static void Utc<T>(EntityTypeBuilder<T> b, string name) where T : class
        => b.Property<DateTime>(name).HasConversion(new ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc)));
    private static void NullableUtc<T>(EntityTypeBuilder<T> b, string name) where T : class
        => b.Property<DateTime?>(name).HasConversion(new ValueConverter<DateTime?, DateTime?>(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null));
}
