using Microsoft.EntityFrameworkCore;
using KnowPulse.AiModelManagement;
using KnowPulse.AiQuestioning;
using KnowPulse.Exam;
using KnowPulse.KnowledgeIndexing;
using KnowPulse.KnowledgeBase;
using KnowPulse.Learning;
using KnowPulse.Organization;
using KnowPulse.QuestionBank;
using Volo.Abp.AuditLogging.EntityFrameworkCore;
using Volo.Abp.BackgroundJobs.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Volo.Abp.FeatureManagement.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.Identity.EntityFrameworkCore;
using Volo.Abp.OpenIddict.EntityFrameworkCore;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;
using Volo.Abp.SettingManagement.EntityFrameworkCore;
using Volo.Abp.TenantManagement;
using Volo.Abp.TenantManagement.EntityFrameworkCore;

namespace KnowPulse.EntityFrameworkCore;

[ReplaceDbContext(typeof(IIdentityDbContext))]
[ReplaceDbContext(typeof(ITenantManagementDbContext))]
[ConnectionStringName("Default")]
public partial class KnowPulseDbContext :
    AbpDbContext<KnowPulseDbContext>,
    IIdentityDbContext,
    ITenantManagementDbContext
{
    public DbSet<Department> Departments { get; set; }
    public DbSet<Position> Positions { get; set; }
    public DbSet<JobLevel> JobLevels { get; set; }
    public DbSet<UserOrganizationAssignment> UserOrganizationAssignments { get; set; }
    public DbSet<KnowledgeCategory> KnowledgeCategories { get; set; }
    public DbSet<KnowledgeTag> KnowledgeTags { get; set; }
    public DbSet<KnowledgeItem> KnowledgeItems { get; set; }
    public DbSet<KnowledgeVersion> KnowledgeVersions { get; set; }
    public DbSet<KnowledgeAttachment> KnowledgeAttachments { get; set; }
    public DbSet<KnowledgePermissionScope> KnowledgePermissionScopes { get; set; }
    public DbSet<KnowledgeFeedback> KnowledgeFeedbacks { get; set; }
    public DbSet<AiModelDeployment> AiModelDeployments { get; set; }
    public DbSet<AiModelUsageBinding> AiModelUsageBindings { get; set; }
    public DbSet<AiModelHealthSnapshot> AiModelHealthSnapshots { get; set; }
    public DbSet<EmbeddingModelVersion> EmbeddingModelVersions { get; set; }
    public DbSet<KnowledgeIndexJob> KnowledgeIndexJobs { get; set; }
    public DbSet<KnowledgeChunk> KnowledgeChunks { get; set; }
    public DbSet<KnowledgeEmbeddingRecord> KnowledgeEmbeddingRecords { get; set; }
    public DbSet<KnowPulse.RagAI.RagCallLog> RagCallLogs { get; set; }
    public DbSet<KnowPulse.RagAI.RagCitationRecord> RagCitationRecords { get; set; }
    public DbSet<KnowPulse.RagAI.RagEvaluationCase> RagEvaluationCases { get; set; }
    public DbSet<KnowPulse.RagAI.RagEvaluationRun> RagEvaluationRuns { get; set; }
    public DbSet<KnowPulse.RagAI.RagEvaluationRunCaseResult> RagEvaluationRunCaseResults { get; set; }
    public DbSet<AiQuestionPromptTemplate> AiQuestionPromptTemplates { get; set; }
    public DbSet<AiQuestionGenerationTask> AiQuestionGenerationTasks { get; set; }
    public DbSet<AiQuestionCandidate> AiQuestionCandidates { get; set; }
    public DbSet<AiQuestionCandidateCitation> AiQuestionCandidateCitations { get; set; }
    public DbSet<AiQuestionModelCallLog> AiQuestionModelCallLogs { get; set; }
    public DbSet<Question> Questions { get; set; }
    public DbSet<QuestionOption> QuestionOptions { get; set; }
    public DbSet<QuestionAnswer> QuestionAnswers { get; set; }
    public DbSet<QuestionAnalysis> QuestionAnalyses { get; set; }
    public DbSet<QuestionKnowledgePoint> QuestionKnowledgePoints { get; set; }
    public DbSet<QuestionSource> QuestionSources { get; set; }
    public DbSet<QuestionReview> QuestionReviews { get; set; }
    public DbSet<QuestionSimilarityCheck> QuestionSimilarityChecks { get; set; }
    public DbSet<ExamPlan> ExamPlans { get; set; }
    public DbSet<ExamPaper> ExamPapers { get; set; }
    public DbSet<ExamPaperQuestion> ExamPaperQuestions { get; set; }
    public DbSet<ExamAssignment> ExamAssignments { get; set; }
    public DbSet<ExamAttempt> ExamAttempts { get; set; }
    public DbSet<ExamAnswer> ExamAnswers { get; set; }
    public DbSet<ExamScore> ExamScores { get; set; }
    public DbSet<WrongQuestionRecord> WrongQuestionRecords { get; set; }
    public DbSet<LearningPath> LearningPaths { get; set; }
    public DbSet<LearningPathAssignment> LearningPathAssignments { get; set; }
    public DbSet<LearningPathRequiredKnowledge> LearningPathRequiredKnowledge { get; set; }
    public DbSet<LearningPathRecommendedKnowledge> LearningPathRecommendedKnowledge { get; set; }
    public DbSet<LearningPathRequiredExam> LearningPathRequiredExams { get; set; }
    public DbSet<LearningKnowledgeReadRecord> LearningKnowledgeReadRecords { get; set; }

    #region Entities from the modules

    /* Notice: We only implemented IIdentityDbContext and ITenantManagementDbContext
     * and replaced them for this DbContext. This allows you to perform JOIN
     * queries for the entities of these modules over the repositories easily. You
     * typically don't need that for other modules. But, if you need, you can
     * implement the DbContext interface of the needed module and use ReplaceDbContext
     * attribute just like IIdentityDbContext and ITenantManagementDbContext.
     *
     * More info: Replacing a DbContext of a module ensures that the related module
     * uses this DbContext on runtime. Otherwise, it will use its own DbContext class.
     */

    //Identity
    public DbSet<IdentityUser> Users { get; set; }
    public DbSet<IdentityRole> Roles { get; set; }
    public DbSet<IdentityClaimType> ClaimTypes { get; set; }
    public DbSet<OrganizationUnit> OrganizationUnits { get; set; }
    public DbSet<IdentitySecurityLog> SecurityLogs { get; set; }
    public DbSet<IdentityLinkUser> LinkUsers { get; set; }
    public DbSet<IdentityUserDelegation> UserDelegations { get; set; }
    public DbSet<IdentitySession> Sessions { get; set; }
    // Tenant Management
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<TenantConnectionString> TenantConnectionStrings { get; set; }

    #endregion

    /// <summary>
    /// 创建 KnowPulse EF Core DbContext。
    /// </summary>
    public KnowPulseDbContext(DbContextOptions<KnowPulseDbContext> options)
        : base(options)
    {

    }

    /// <summary>
    /// 配置 ABP 模块实体和 KnowPulse 业务实体的表结构、长度约束、索引和关系。
    /// </summary>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        /* Include modules to your migration db context */

        builder.ConfigurePermissionManagement();
        builder.ConfigureSettingManagement();
        builder.ConfigureBackgroundJobs();
        builder.ConfigureAuditLogging();
        builder.ConfigureIdentity();
        builder.ConfigureOpenIddict();
        builder.ConfigureFeatureManagement();
        builder.ConfigureTenantManagement();
        builder.ConfigureTeaching();
        builder.ConfigureTeachingOwnership();

        builder.Entity<Department>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "Departments", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Code).IsRequired().HasMaxLength(Department.MaxCodeLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(Department.MaxNameLength);
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            b.HasIndex(x => x.ParentId);
        });

        builder.Entity<Position>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "Positions", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Code).IsRequired().HasMaxLength(Position.MaxCodeLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(Position.MaxNameLength);
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            b.HasIndex(x => x.DepartmentId);
        });

        builder.Entity<JobLevel>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "JobLevels", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Code).IsRequired().HasMaxLength(JobLevel.MaxCodeLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(JobLevel.MaxNameLength);
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        });

        builder.Entity<UserOrganizationAssignment>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "UserOrganizationAssignments", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.HasIndex(x => new { x.TenantId, x.UserId, x.DepartmentId, x.PositionId });
            b.HasIndex(x => x.DepartmentId);
            b.HasIndex(x => x.PositionId);
            b.HasIndex(x => x.JobLevelId);
        });

        builder.Entity<KnowledgeCategory>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "KnowledgeCategories", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Code).IsRequired().HasMaxLength(KnowledgeCategory.MaxCodeLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(KnowledgeCategory.MaxNameLength);
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            b.HasIndex(x => x.ParentId);
        });

        builder.Entity<KnowledgeTag>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "KnowledgeTags", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(KnowledgeTag.MaxNameLength);
            b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        });

        builder.Entity<KnowledgeItem>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "KnowledgeItems", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Title).IsRequired().HasMaxLength(KnowledgeItem.MaxTitleLength);
            b.Property(x => x.Summary).HasMaxLength(KnowledgeItem.MaxSummaryLength);
            b.HasIndex(x => x.CategoryId);
            b.HasIndex(x => x.CurrentPublishedVersionId);
        });

        builder.Entity<KnowledgeVersion>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "KnowledgeVersions", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Title).IsRequired().HasMaxLength(KnowledgeVersion.MaxTitleLength);
            b.Property(x => x.ContentText).HasMaxLength(KnowledgeVersion.MaxContentTextLength);
            b.Property(x => x.Status).IsRequired();
            b.HasIndex(x => new { x.KnowledgeItemId, x.VersionNumber }).IsUnique();
            b.HasIndex(x => new { x.KnowledgeItemId, x.IsCurrentPublished })
                .HasDatabaseName("IX_AppKnowledgeVersions_KnowledgeItemId_CurrentPublished")
                .HasFilter("\"IsCurrentPublished\" AND NOT \"IsDeleted\"")
                .IsUnique();
        });

        builder.Entity<KnowledgeAttachment>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "KnowledgeAttachments", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.FileName).IsRequired().HasMaxLength(KnowledgeAttachment.MaxFileNameLength);
            b.Property(x => x.BlobName).IsRequired().HasMaxLength(KnowledgeAttachment.MaxBlobNameLength);
            b.Property(x => x.ContentType).IsRequired().HasMaxLength(KnowledgeAttachment.MaxContentTypeLength);
            b.HasIndex(x => x.KnowledgeVersionId);
        });

        builder.Entity<KnowledgePermissionScope>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "KnowledgePermissionScopes", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.ScopeType).IsRequired();
            b.HasIndex(x => new { x.KnowledgeItemId, x.ScopeType, x.TargetId });
            b.HasIndex(x => x.MinimumSecurityLevel);
        });

        builder.Entity<KnowledgeFeedback>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "KnowledgeFeedbacks", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Type).IsRequired();
            b.Property(x => x.Status).IsRequired();
            b.Property(x => x.Title).IsRequired().HasMaxLength(KnowledgeFeedback.MaxTitleLength);
            b.Property(x => x.Description).IsRequired().HasMaxLength(KnowledgeFeedback.MaxDescriptionLength);
            b.Property(x => x.Resolution).HasMaxLength(KnowledgeFeedback.MaxResolutionLength);
            b.Property(x => x.CloseReason).HasMaxLength(KnowledgeFeedback.MaxCloseReasonLength);
            b.HasIndex(x => new { x.TenantId, x.Status, x.CreationTime });
            b.HasIndex(x => x.KnowledgeItemId);
            b.HasIndex(x => x.KnowledgeVersionId);
            b.HasIndex(x => x.LinkedKnowledgeVersionId);
            b.HasIndex(x => x.SubmitterUserId);
            b.HasOne<KnowledgeItem>().WithMany().HasForeignKey(x => x.KnowledgeItemId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<KnowledgeVersion>().WithMany().HasForeignKey(x => x.KnowledgeVersionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<KnowledgeVersion>().WithMany().HasForeignKey(x => x.LinkedKnowledgeVersionId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<AiModelDeployment>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "AiModelDeployments", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Alias).IsRequired().HasMaxLength(AiModelDeployment.MaxAliasLength);
            b.Property(x => x.ProviderType).IsRequired();
            b.Property(x => x.Capability).IsRequired();
            b.Property(x => x.UpstreamModelName).IsRequired().HasMaxLength(AiModelDeployment.MaxUpstreamModelNameLength);
            b.Property(x => x.ModelVersion).IsRequired().HasMaxLength(AiModelDeployment.MaxModelVersionLength);
            b.Property(x => x.ApiKeyEnvironmentVariable).HasMaxLength(AiModelDeployment.MaxEnvironmentVariableLength);
            b.Property(x => x.BaseUrl).HasMaxLength(AiModelDeployment.MaxBaseUrlLength);
            b.Property(x => x.PromptTokenPriceUsd).HasPrecision(18, 8);
            b.Property(x => x.CompletionTokenPriceUsd).HasPrecision(18, 8);
            b.Property(x => x.Description).HasMaxLength(AiModelDeployment.MaxDescriptionLength);
            b.HasIndex(x => new { x.TenantId, x.Alias });
            b.HasIndex(x => new { x.TenantId, x.Capability, x.IsActive });
            b.HasIndex(x => new { x.ProviderType, x.IsActive });
        });

        builder.Entity<AiModelUsageBinding>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "AiModelUsageBindings", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Usage).IsRequired();
            b.Property(x => x.BusinessModelVersion).IsRequired().HasMaxLength(AiModelUsageBinding.MaxBusinessModelVersionLength);
            b.Property(x => x.FallbackAliasesJson).HasMaxLength(AiModelUsageBinding.MaxFallbackAliasesJsonLength);
            b.HasIndex(x => new { x.TenantId, x.Usage, x.IsActive });
            b.HasIndex(x => x.DeploymentId);
            b.HasOne<AiModelDeployment>()
                .WithMany()
                .HasForeignKey(x => x.DeploymentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<AiModelHealthSnapshot>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "AiModelHealthSnapshots", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Status).IsRequired();
            b.Property(x => x.FailureReason).HasMaxLength(AiModelHealthSnapshot.MaxFailureReasonLength);
            b.Property(x => x.GatewayModelInfoJson).HasMaxLength(AiModelHealthSnapshot.MaxGatewayModelInfoJsonLength);
            b.HasIndex(x => new { x.DeploymentId, x.CheckedTime });
            b.HasIndex(x => new { x.TenantId, x.Status, x.CheckedTime });
            b.HasOne<AiModelDeployment>()
                .WithMany()
                .HasForeignKey(x => x.DeploymentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<EmbeddingModelVersion>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "EmbeddingModelVersions", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Provider).IsRequired().HasMaxLength(EmbeddingModelVersion.MaxProviderLength);
            b.Property(x => x.ModelName).IsRequired().HasMaxLength(EmbeddingModelVersion.MaxModelNameLength);
            b.Property(x => x.ModelVersion).IsRequired().HasMaxLength(EmbeddingModelVersion.MaxModelVersionLength);
            b.Property(x => x.CollectionName).IsRequired().HasMaxLength(EmbeddingModelVersion.MaxCollectionNameLength);
            b.Property(x => x.Description).HasMaxLength(EmbeddingModelVersion.MaxDescriptionLength);
            b.HasIndex(x => new { x.TenantId, x.Provider, x.ModelName, x.ModelVersion });
            b.HasIndex(x => new { x.TenantId, x.IsDefault, x.IsActive });
            b.HasIndex(x => x.CollectionName);
        });

        builder.Entity<KnowledgeIndexJob>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "KnowledgeIndexJobs", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.TriggerType).IsRequired();
            b.Property(x => x.Status).IsRequired();
            b.Property(x => x.FailureStage).HasMaxLength(KnowledgeIndexJob.MaxFailureStageLength);
            b.Property(x => x.FailureReason).HasMaxLength(KnowledgeIndexJob.MaxFailureReasonLength);
            b.Property(x => x.ProcessingStage).HasMaxLength(KnowledgeIndexJob.MaxProcessingStageLength);
            b.HasIndex(x => new { x.TenantId, x.KnowledgeVersionId, x.Status });
            b.HasIndex(x => new { x.KnowledgeItemId, x.Status });
            b.HasIndex(x => x.EmbeddingModelVersionId);
            b.HasIndex(x => x.RetryOfJobId);
            b.HasIndex(x => x.NextRetryTime);
            b.HasIndex(x => new { x.Status, x.LastHeartbeatTime });
        });

        builder.Entity<KnowledgeChunk>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "KnowledgeChunks", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Content).IsRequired().HasMaxLength(KnowledgeChunk.MaxContentLength);
            b.Property(x => x.ContentHash).IsRequired().HasMaxLength(KnowledgeChunk.MaxContentHashLength);
            b.Property(x => x.SourceTitle).IsRequired().HasMaxLength(KnowledgeChunk.MaxSourceTitleLength);
            b.HasIndex(x => new { x.KnowledgeIndexJobId, x.Sequence }).IsUnique();
            b.HasIndex(x => new { x.KnowledgeVersionId, x.IsActive });
            b.HasIndex(x => new { x.TenantId, x.IsActive, x.IsPublishedSnapshot });
            b.HasIndex(x => x.ContentHash);
        });

        builder.Entity<KnowledgeEmbeddingRecord>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "KnowledgeEmbeddingRecords", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.VectorStoreRecordId).IsRequired().HasMaxLength(KnowledgeEmbeddingRecord.MaxVectorStoreRecordIdLength);
            b.Property(x => x.ContentHash).IsRequired().HasMaxLength(KnowledgeEmbeddingRecord.MaxContentHashLength);
            b.HasIndex(x => new { x.KnowledgeChunkId, x.EmbeddingModelVersionId })
                .HasDatabaseName("IX_AppKnowledgeEmbeddingRecords_Chunk_ModelVersion_Active")
                .HasFilter("\"IsActive\" AND NOT \"IsDeleted\"")
                .IsUnique();
            b.HasIndex(x => new { x.EmbeddingModelVersionId, x.VectorStoreRecordId }).IsUnique();
            b.HasIndex(x => new { x.KnowledgeVersionId, x.IsActive });
        });

        builder.Entity<KnowPulse.RagAI.RagCallLog>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "RagCallLogs", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.CallType).IsRequired();
            b.Property(x => x.Status).IsRequired();
            b.Property(x => x.QueryHash).IsRequired().HasMaxLength(KnowPulse.RagAI.RagCallLog.MaxQueryHashLength);
            b.Property(x => x.QuerySummary).IsRequired().HasMaxLength(KnowPulse.RagAI.RagCallLog.MaxQuerySummaryLength);
            b.Property(x => x.ModelProvider).IsRequired().HasMaxLength(KnowPulse.RagAI.RagCallLog.MaxModelProviderLength);
            b.Property(x => x.ModelName).IsRequired().HasMaxLength(KnowPulse.RagAI.RagCallLog.MaxModelNameLength);
            b.Property(x => x.ModelVersion).IsRequired().HasMaxLength(KnowPulse.RagAI.RagCallLog.MaxModelVersionLength);
            b.Property(x => x.CollectionName).IsRequired().HasMaxLength(KnowPulse.RagAI.RagCallLog.MaxCollectionNameLength);
            b.Property(x => x.EstimatedCostUsd).HasPrecision(18, 6);
            b.Property(x => x.FailureStage).HasMaxLength(KnowPulse.RagAI.RagCallLog.MaxFailureStageLength);
            b.Property(x => x.FailureReason).HasMaxLength(KnowPulse.RagAI.RagCallLog.MaxFailureReasonLength);
            b.HasIndex(x => new { x.TenantId, x.UserId, x.CreationTime });
            b.HasIndex(x => new { x.TenantId, x.ModelVersion, x.CreationTime });
            b.HasIndex(x => new { x.CallType, x.Status, x.CreationTime });
        });

        builder.Entity<KnowPulse.RagAI.RagCitationRecord>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "RagCitationRecords", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.SourceTitle).IsRequired().HasMaxLength(KnowPulse.RagAI.RagCitationRecord.MaxSourceTitleLength);
            b.Property(x => x.QuoteHash).IsRequired().HasMaxLength(KnowPulse.RagAI.RagCitationRecord.MaxQuoteHashLength);
            b.Property(x => x.QuotePreview).IsRequired().HasMaxLength(KnowPulse.RagAI.RagCitationRecord.MaxQuotePreviewLength);
            b.HasIndex(x => new { x.RagCallLogId, x.Rank }).IsUnique();
            b.HasIndex(x => new { x.KnowledgeVersionId, x.ChunkId });
            b.HasIndex(x => x.KnowledgeItemId);
            b.HasOne<KnowPulse.RagAI.RagCallLog>()
                .WithMany()
                .HasForeignKey(x => x.RagCallLogId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<KnowPulse.RagAI.RagEvaluationCase>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "RagEvaluationCases", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(KnowPulse.RagAI.RagEvaluationCase.MaxNameLength);
            b.Property(x => x.Question).IsRequired().HasMaxLength(KnowPulse.RagAI.RagEvaluationCase.MaxQuestionLength);
            b.Property(x => x.PermissionContextJson).IsRequired().HasMaxLength(KnowPulse.RagAI.RagEvaluationCase.MaxPermissionContextJsonLength);
            b.Property(x => x.ExpectedCitationsJson).IsRequired().HasMaxLength(KnowPulse.RagAI.RagEvaluationCase.MaxCitationExpectationJsonLength);
            b.Property(x => x.ForbiddenCitationsJson).IsRequired().HasMaxLength(KnowPulse.RagAI.RagEvaluationCase.MaxCitationExpectationJsonLength);
            b.Property(x => x.Notes).HasMaxLength(KnowPulse.RagAI.RagEvaluationCase.MaxNotesLength);
            b.HasIndex(x => new { x.TenantId, x.IsActive, x.CreationTime });
            b.HasIndex(x => x.Name);
        });

        builder.Entity<KnowPulse.RagAI.RagEvaluationRun>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "RagEvaluationRuns", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(KnowPulse.RagAI.RagEvaluationRun.MaxNameLength);
            b.Property(x => x.Status).IsRequired();
            b.Property(x => x.FailureReason).HasMaxLength(KnowPulse.RagAI.RagEvaluationRun.MaxFailureReasonLength);
            b.HasIndex(x => new { x.TenantId, x.Status, x.CreationTime });
            b.HasIndex(x => new { x.TenantId, x.HardFailed, x.CreationTime });
        });

        builder.Entity<KnowPulse.RagAI.RagEvaluationRunCaseResult>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "RagEvaluationRunCaseResults", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.QuestionSummary).IsRequired().HasMaxLength(KnowPulse.RagAI.RagEvaluationRunCaseResult.MaxQuestionSummaryLength);
            b.Property(x => x.FailureReason).HasMaxLength(KnowPulse.RagAI.RagEvaluationRunCaseResult.MaxFailureReasonLength);
            b.Property(x => x.AnswerSummary).HasMaxLength(KnowPulse.RagAI.RagEvaluationRunCaseResult.MaxAnswerSummaryLength);
            b.Property(x => x.ActualCitationsJson).IsRequired().HasMaxLength(KnowPulse.RagAI.RagEvaluationRunCaseResult.MaxActualCitationsJsonLength);
            b.HasIndex(x => new { x.EvaluationRunId, x.EvaluationCaseId }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.Passed, x.CreationTime });
            b.HasIndex(x => x.UnauthorizedCitationCount);
            b.HasOne<KnowPulse.RagAI.RagEvaluationRun>()
                .WithMany()
                .HasForeignKey(x => x.EvaluationRunId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne<KnowPulse.RagAI.RagEvaluationCase>()
                .WithMany()
                .HasForeignKey(x => x.EvaluationCaseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<AiQuestionPromptTemplate>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "AiQuestionPromptTemplates", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.TemplateKey).IsRequired().HasMaxLength(AiQuestionPromptTemplate.MaxTemplateKeyLength);
            b.Property(x => x.Version).IsRequired().HasMaxLength(AiQuestionPromptTemplate.MaxVersionLength);
            b.Property(x => x.Description).HasMaxLength(AiQuestionPromptTemplate.MaxDescriptionLength);
            b.Property(x => x.SystemPrompt).IsRequired().HasMaxLength(AiQuestionPromptTemplate.MaxSystemPromptLength);
            b.Property(x => x.UserPromptTemplate).IsRequired().HasMaxLength(AiQuestionPromptTemplate.MaxUserPromptTemplateLength);
            b.HasIndex(x => new { x.TenantId, x.TemplateKey, x.Version }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.TemplateKey, x.IsActive });
        });

        builder.Entity<AiQuestionGenerationTask>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "AiQuestionGenerationTasks", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Status).IsRequired();
            b.Property(x => x.Type).IsRequired();
            b.Property(x => x.Difficulty).IsRequired();
            b.Property(x => x.PromptVersion).IsRequired().HasMaxLength(AiQuestionGenerationTask.MaxPromptVersionLength);
            b.Property(x => x.ModelProvider).IsRequired().HasMaxLength(AiQuestionGenerationTask.MaxModelProviderLength);
            b.Property(x => x.ModelName).IsRequired().HasMaxLength(AiQuestionGenerationTask.MaxModelNameLength);
            b.Property(x => x.ModelVersion).IsRequired().HasMaxLength(AiQuestionGenerationTask.MaxModelVersionLength);
            b.Property(x => x.KnowledgeScope).IsRequired().HasMaxLength(AiQuestionGenerationTask.MaxKnowledgeScopeLength);
            b.Property(x => x.RetrievalQuery).IsRequired().HasMaxLength(AiQuestionGenerationTask.MaxRetrievalQueryLength);
            b.Property(x => x.Tags).HasMaxLength(AiQuestionGenerationTask.MaxTagsLength);
            b.Property(x => x.FailureStage).HasMaxLength(AiQuestionGenerationTask.MaxFailureStageLength);
            b.Property(x => x.FailureReason).HasMaxLength(AiQuestionGenerationTask.MaxFailureReasonLength);
            b.HasIndex(x => new { x.TenantId, x.Status, x.CreationTime });
            b.HasIndex(x => new { x.Type, x.Difficulty });
            b.HasIndex(x => x.PromptTemplateId);
            b.HasOne<AiQuestionPromptTemplate>()
                .WithMany()
                .HasForeignKey(x => x.PromptTemplateId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<AiQuestionCandidate>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "AiQuestionCandidates", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Status).IsRequired();
            b.Property(x => x.Type).IsRequired();
            b.Property(x => x.Difficulty).IsRequired();
            b.Property(x => x.Title).IsRequired().HasMaxLength(AiQuestionCandidate.MaxTitleLength);
            b.Property(x => x.Stem).IsRequired().HasMaxLength(AiQuestionCandidate.MaxStemLength);
            b.Property(x => x.Tags).HasMaxLength(AiQuestionCandidate.MaxTagsLength);
            b.Property(x => x.Analysis).HasMaxLength(AiQuestionCandidate.MaxAnalysisLength);
            b.Property(x => x.OptionsJson).IsRequired().HasMaxLength(AiQuestionCandidate.MaxJsonLength);
            b.Property(x => x.AnswersJson).IsRequired().HasMaxLength(AiQuestionCandidate.MaxJsonLength);
            b.Property(x => x.KnowledgePointsJson).IsRequired().HasMaxLength(AiQuestionCandidate.MaxJsonLength);
            b.Property(x => x.ValidationErrors).HasMaxLength(AiQuestionCandidate.MaxValidationErrorsLength);
            b.Property(x => x.SimilaritySummary).HasMaxLength(AiQuestionCandidate.MaxSimilaritySummaryLength);
            b.HasIndex(x => new { x.TaskId, x.CreationTime });
            b.HasIndex(x => new { x.Status, x.Type, x.Difficulty });
            b.HasIndex(x => x.LinkedQuestionId);
            b.HasIndex(x => x.HighestSimilarQuestionId);
            b.HasOne<AiQuestionGenerationTask>()
                .WithMany()
                .HasForeignKey(x => x.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne<Question>()
                .WithMany()
                .HasForeignKey(x => x.LinkedQuestionId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Question>()
                .WithMany()
                .HasForeignKey(x => x.HighestSimilarQuestionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<AiQuestionCandidateCitation>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "AiQuestionCandidateCitations", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.SourceTitle).IsRequired().HasMaxLength(AiQuestionCandidateCitation.MaxSourceTitleLength);
            b.Property(x => x.SourceExcerpt).IsRequired().HasMaxLength(AiQuestionCandidateCitation.MaxSourceExcerptLength);
            b.Property(x => x.SourceHash).IsRequired().HasMaxLength(AiQuestionCandidateCitation.MaxSourceHashLength);
            b.HasIndex(x => new { x.CandidateId, x.Rank }).IsUnique();
            b.HasIndex(x => new { x.KnowledgeVersionId, x.ChunkId });
            b.HasIndex(x => x.KnowledgeItemId);
            b.HasOne<AiQuestionCandidate>()
                .WithMany()
                .HasForeignKey(x => x.CandidateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AiQuestionModelCallLog>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "AiQuestionModelCallLogs", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.PromptVersion).IsRequired().HasMaxLength(AiQuestionModelCallLog.MaxPromptVersionLength);
            b.Property(x => x.Status).IsRequired();
            b.Property(x => x.QueryHash).IsRequired().HasMaxLength(AiQuestionModelCallLog.MaxQueryHashLength);
            b.Property(x => x.QuerySummary).IsRequired().HasMaxLength(AiQuestionModelCallLog.MaxQuerySummaryLength);
            b.Property(x => x.ModelProvider).IsRequired().HasMaxLength(AiQuestionModelCallLog.MaxModelProviderLength);
            b.Property(x => x.ModelName).IsRequired().HasMaxLength(AiQuestionModelCallLog.MaxModelNameLength);
            b.Property(x => x.ModelVersion).IsRequired().HasMaxLength(AiQuestionModelCallLog.MaxModelVersionLength);
            b.Property(x => x.FailureStage).HasMaxLength(AiQuestionModelCallLog.MaxFailureStageLength);
            b.Property(x => x.FailureReason).HasMaxLength(AiQuestionModelCallLog.MaxFailureReasonLength);
            b.HasIndex(x => new { x.TaskId, x.CreationTime });
            b.HasIndex(x => new { x.Status, x.CreationTime });
            b.HasIndex(x => x.PromptTemplateId);
            b.HasOne<AiQuestionGenerationTask>()
                .WithMany()
                .HasForeignKey(x => x.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne<AiQuestionPromptTemplate>()
                .WithMany()
                .HasForeignKey(x => x.PromptTemplateId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Question>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "Questions", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Type).IsRequired();
            b.Property(x => x.Status).IsRequired();
            b.Property(x => x.Difficulty).IsRequired();
            b.Property(x => x.CreationSource).IsRequired();
            b.Property(x => x.Title).IsRequired().HasMaxLength(Question.MaxTitleLength);
            b.Property(x => x.Stem).IsRequired().HasMaxLength(Question.MaxStemLength);
            b.Property(x => x.Tags).HasMaxLength(Question.MaxTagsLength);
            b.HasIndex(x => new { x.TenantId, x.Status, x.Type });
            b.HasIndex(x => x.ApplicablePositionId);
            b.HasIndex(x => x.CreationSource);
        });

        builder.Entity<QuestionOption>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "QuestionOptions", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.OptionKey).IsRequired().HasMaxLength(QuestionOption.MaxOptionKeyLength);
            b.Property(x => x.Content).IsRequired().HasMaxLength(QuestionOption.MaxContentLength);
            b.HasIndex(x => new { x.QuestionId, x.OptionKey });
            b.HasIndex(x => new { x.QuestionId, x.DisplayOrder });
            b.HasOne<Question>()
                .WithMany()
                .HasForeignKey(x => x.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<QuestionAnswer>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "QuestionAnswers", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Value).IsRequired().HasMaxLength(QuestionAnswer.MaxValueLength);
            b.HasIndex(x => new { x.QuestionId, x.DisplayOrder });
            b.HasOne<Question>()
                .WithMany()
                .HasForeignKey(x => x.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<QuestionAnalysis>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "QuestionAnalyses", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Content).IsRequired().HasMaxLength(QuestionAnalysis.MaxContentLength);
            b.HasIndex(x => x.QuestionId);
            b.HasOne<Question>()
                .WithMany()
                .HasForeignKey(x => x.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<QuestionKnowledgePoint>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "QuestionKnowledgePoints", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(QuestionKnowledgePoint.MaxNameLength);
            b.HasIndex(x => new { x.QuestionId, x.Name });
            b.HasIndex(x => new { x.TenantId, x.Name });
            b.HasOne<Question>()
                .WithMany()
                .HasForeignKey(x => x.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<QuestionSource>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "QuestionSources", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.SourceTitle).HasMaxLength(QuestionSource.MaxSourceTitleLength);
            b.Property(x => x.SourceExcerpt).HasMaxLength(QuestionSource.MaxSourceExcerptLength);
            b.Property(x => x.SourceHash).HasMaxLength(QuestionSource.MaxSourceHashLength);
            b.Property(x => x.ManualSourceDescription).HasMaxLength(QuestionSource.MaxManualSourceDescriptionLength);
            b.HasIndex(x => x.QuestionId);
            b.HasIndex(x => x.KnowledgeItemId);
            b.HasIndex(x => x.KnowledgeVersionId);
            b.HasIndex(x => x.ChunkId);
            b.HasOne<Question>()
                .WithMany()
                .HasForeignKey(x => x.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<QuestionReview>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "QuestionReviews", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Action).IsRequired();
            b.Property(x => x.FromStatus).IsRequired();
            b.Property(x => x.ToStatus).IsRequired();
            b.Property(x => x.Comment).HasMaxLength(QuestionReview.MaxCommentLength);
            b.HasIndex(x => new { x.QuestionId, x.CreationTime });
            b.HasIndex(x => new { x.ReviewerUserId, x.ReviewTime });
            b.HasOne<Question>()
                .WithMany()
                .HasForeignKey(x => x.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<QuestionSimilarityCheck>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "QuestionSimilarityChecks", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.SimilarityScore).IsRequired();
            b.Property(x => x.Summary).HasMaxLength(QuestionSimilarityCheck.MaxSummaryLength);
            b.HasIndex(x => new { x.QuestionId, x.CreationTime });
            b.HasIndex(x => x.SimilarQuestionId);
            b.HasOne<Question>()
                .WithMany()
                .HasForeignKey(x => x.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne<Question>()
                .WithMany()
                .HasForeignKey(x => x.SimilarQuestionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ExamPlan>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "ExamPlans", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(ExamPlan.MaxNameLength);
            b.Property(x => x.Description).HasMaxLength(ExamPlan.MaxDescriptionLength);
            b.Property(x => x.Status).IsRequired();
            b.Property(x => x.ResultVisibility).IsRequired();
            b.Property(x => x.PassingScore).HasPrecision(18, 2);
            b.HasIndex(x => new { x.TenantId, x.Status, x.StartTime });
            b.HasIndex(x => x.PaperId);
            b.HasOne<ExamPaper>()
                .WithMany()
                .HasForeignKey(x => x.PaperId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ExamPaper>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "ExamPapers", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Title).IsRequired().HasMaxLength(ExamPaper.MaxTitleLength);
            b.Property(x => x.BuildMode).IsRequired();
            b.Property(x => x.TotalScore).HasPrecision(18, 2);
            b.HasIndex(x => new { x.TenantId, x.BuildMode, x.CreationTime });
            b.HasIndex(x => x.ApplicablePositionId);
        });

        builder.Entity<ExamPaperQuestion>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "ExamPaperQuestions", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Type).IsRequired();
            b.Property(x => x.Difficulty).IsRequired();
            b.Property(x => x.OriginalQuestionCreationSource).IsRequired();
            b.Property(x => x.Title).IsRequired().HasMaxLength(ExamPaperQuestion.MaxTitleLength);
            b.Property(x => x.Stem).IsRequired().HasMaxLength(ExamPaperQuestion.MaxStemLength);
            b.Property(x => x.OptionsJson).IsRequired().HasMaxLength(ExamPaperQuestion.MaxJsonLength);
            b.Property(x => x.CorrectAnswersJson).IsRequired().HasMaxLength(ExamPaperQuestion.MaxJsonLength);
            b.Property(x => x.Analysis).HasMaxLength(ExamPaperQuestion.MaxAnalysisLength);
            b.Property(x => x.KnowledgePointsSnapshot).HasMaxLength(ExamPaperQuestion.MaxKnowledgePointsLength);
            b.Property(x => x.SourceSnapshot).HasMaxLength(ExamPaperQuestion.MaxSourceSnapshotLength);
            b.Property(x => x.Score).HasPrecision(18, 2);
            b.Property(x => x.MultipleChoiceScoringMode).IsRequired();
            b.HasIndex(x => new { x.PaperId, x.DisplayOrder }).IsUnique();
            b.HasIndex(x => x.OriginalQuestionId);
            b.HasIndex(x => new { x.Type, x.Difficulty });
            b.HasOne<ExamPaper>()
                .WithMany()
                .HasForeignKey(x => x.PaperId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne<Question>()
                .WithMany()
                .HasForeignKey(x => x.OriginalQuestionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ExamAssignment>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "ExamAssignments", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.ScopeType).IsRequired();
            b.HasIndex(x => new { x.PlanId, x.ScopeType, x.TargetId });
            b.HasIndex(x => new { x.TenantId, x.ScopeType, x.TargetId, x.IsActive });
            b.HasOne<ExamPlan>()
                .WithMany()
                .HasForeignKey(x => x.PlanId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ExamAttempt>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "ExamAttempts", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Status).IsRequired();
            b.Property(x => x.SubmitIdempotencyKey).HasMaxLength(ExamAttempt.MaxIdempotencyKeyLength);
            b.HasIndex(x => new { x.PlanId, x.UserId, x.AttemptNumber }).IsUnique();
            b.HasIndex(x => new { x.UserId, x.Status, x.StartedTime });
            b.HasIndex(x => x.PaperId);
            b.HasOne<ExamPlan>()
                .WithMany()
                .HasForeignKey(x => x.PlanId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ExamPaper>()
                .WithMany()
                .HasForeignKey(x => x.PaperId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ExamAnswer>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "ExamAnswers", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.AnswerJson).IsRequired().HasMaxLength(ExamAnswer.MaxAnswerJsonLength);
            b.Property(x => x.LastSaveIdempotencyKey).HasMaxLength(ExamAnswer.MaxIdempotencyKeyLength);
            b.HasIndex(x => new { x.AttemptId, x.PaperQuestionId }).IsUnique();
            b.HasOne<ExamAttempt>()
                .WithMany()
                .HasForeignKey(x => x.AttemptId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne<ExamPaperQuestion>()
                .WithMany()
                .HasForeignKey(x => x.PaperQuestionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ExamScore>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "ExamScores", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.TotalScore).HasPrecision(18, 2);
            b.Property(x => x.ObtainedScore).HasPrecision(18, 2);
            b.Property(x => x.PassingScore).HasPrecision(18, 2);
            b.Property(x => x.ScoringVersion).IsRequired().HasMaxLength(ExamScore.MaxScoringVersionLength);
            b.HasIndex(x => x.AttemptId).IsUnique();
            b.HasIndex(x => new { x.PlanId, x.UserId });
            b.HasIndex(x => new { x.IsPassed, x.ScoredTime });
            b.HasOne<ExamAttempt>()
                .WithMany()
                .HasForeignKey(x => x.AttemptId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WrongQuestionRecord>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "WrongQuestionRecords", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.QuestionSnapshot).IsRequired().HasMaxLength(WrongQuestionRecord.MaxSnapshotLength);
            b.Property(x => x.UserAnswerJson).IsRequired().HasMaxLength(WrongQuestionRecord.MaxJsonLength);
            b.Property(x => x.CorrectAnswerJson).IsRequired().HasMaxLength(WrongQuestionRecord.MaxJsonLength);
            b.Property(x => x.AnalysisSnapshot).HasMaxLength(WrongQuestionRecord.MaxAnalysisLength);
            b.Property(x => x.MaxScore).HasPrecision(18, 2);
            b.Property(x => x.ObtainedScore).HasPrecision(18, 2);
            b.HasIndex(x => new { x.ScoreId, x.PaperQuestionId });
            b.HasIndex(x => new { x.UserId, x.OriginalQuestionId });
            b.HasIndex(x => x.AttemptId);
            b.HasOne<ExamScore>()
                .WithMany()
                .HasForeignKey(x => x.ScoreId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne<ExamAttempt>()
                .WithMany()
                .HasForeignKey(x => x.AttemptId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ExamPaperQuestion>()
                .WithMany()
                .HasForeignKey(x => x.PaperQuestionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<LearningPath>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "LearningPaths", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(LearningPath.MaxNameLength);
            b.Property(x => x.Description).HasMaxLength(LearningPath.MaxDescriptionLength);
            b.HasIndex(x => new { x.TenantId, x.IsActive, x.CreationTime });
            b.HasIndex(x => x.Name);
        });

        builder.Entity<LearningPathAssignment>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "LearningPathAssignments", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.ScopeType).IsRequired();
            b.HasIndex(x => new { x.LearningPathId, x.ScopeType, x.TargetId });
            b.HasIndex(x => new { x.TenantId, x.ScopeType, x.TargetId, x.IsActive });
            b.HasOne<LearningPath>()
                .WithMany()
                .HasForeignKey(x => x.LearningPathId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<LearningPathRequiredKnowledge>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "LearningPathRequiredKnowledge", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Note).HasMaxLength(KnowPulse.Learning.LearningPathRequiredKnowledge.MaxNoteLength);
            b.HasIndex(x => new { x.LearningPathId, x.DisplayOrder });
            b.HasIndex(x => x.KnowledgeItemId);
            b.HasIndex(x => x.KnowledgeVersionId);
            b.HasOne<LearningPath>()
                .WithMany()
                .HasForeignKey(x => x.LearningPathId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne<KnowledgeItem>()
                .WithMany()
                .HasForeignKey(x => x.KnowledgeItemId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne<KnowledgeVersion>()
                .WithMany()
                .HasForeignKey(x => x.KnowledgeVersionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<LearningPathRecommendedKnowledge>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "LearningPathRecommendedKnowledge", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Reason).HasMaxLength(KnowPulse.Learning.LearningPathRecommendedKnowledge.MaxReasonLength);
            b.HasIndex(x => new { x.LearningPathId, x.DisplayOrder });
            b.HasIndex(x => x.KnowledgeItemId);
            b.HasIndex(x => x.KnowledgeVersionId);
            b.HasOne<LearningPath>()
                .WithMany()
                .HasForeignKey(x => x.LearningPathId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne<KnowledgeItem>()
                .WithMany()
                .HasForeignKey(x => x.KnowledgeItemId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne<KnowledgeVersion>()
                .WithMany()
                .HasForeignKey(x => x.KnowledgeVersionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<LearningPathRequiredExam>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "LearningPathRequiredExams", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Note).HasMaxLength(LearningPathRequiredExam.MaxNoteLength);
            b.HasIndex(x => new { x.LearningPathId, x.DisplayOrder });
            b.HasIndex(x => x.ExamPlanId);
            b.HasOne<LearningPath>()
                .WithMany()
                .HasForeignKey(x => x.LearningPathId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne<ExamPlan>()
                .WithMany()
                .HasForeignKey(x => x.ExamPlanId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<LearningKnowledgeReadRecord>(b =>
        {
            b.ToTable(KnowPulseConsts.DbTablePrefix + "LearningKnowledgeReadRecords", KnowPulseConsts.DbSchema);
            b.ConfigureByConvention();
            b.HasIndex(x => new { x.LearningPathId, x.UserId, x.KnowledgeItemId, x.KnowledgeVersionId }).IsUnique();
            b.HasIndex(x => new { x.UserId, x.LastReadTime });
            b.HasIndex(x => x.CompletedTime);
            b.HasOne<LearningPath>()
                .WithMany()
                .HasForeignKey(x => x.LearningPathId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne<KnowledgeItem>()
                .WithMany()
                .HasForeignKey(x => x.KnowledgeItemId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne<KnowledgeVersion>()
                .WithMany()
                .HasForeignKey(x => x.KnowledgeVersionId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
