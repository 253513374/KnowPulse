using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using KnowPulse.EntityFrameworkCore;
using KnowPulse.Education;
using KnowPulse.WeChat;
using Volo.Abp.OpenIddict.ExtensionGrantTypes;
using Volo.Abp.OpenIddict;
using KnowPulse.KnowledgeIndexing;
using KnowPulse.KnowledgeBase;
using KnowPulse.MultiTenancy;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite.Bundling;
using Microsoft.OpenApi;
using OpenIddict.Validation.AspNetCore;
using LegacyWeChatTokenExtensionGrant = KnowPulse.Authentication.WeChatTokenExtensionGrant;
using BoundWeChatTokenExtensionGrant = KnowPulse.WeChat.WeChatTokenExtensionGrant;
using Volo.Abp;
using Volo.Abp.Account;
using Volo.Abp.Account.Web;
using Volo.Abp.AspNetCore.MultiTenancy;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc.UI.Bundling;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.Shared;
using Volo.Abp.AspNetCore.Serilog;
using Volo.Abp.Auditing;
using Volo.Abp.Autofac;
using Volo.Abp.BlobStoring;
using Volo.Abp.BlobStoring.Minio;
using Volo.Abp.Content;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Volo.Abp.Security.Claims;
using Volo.Abp.Swashbuckle;
using Volo.Abp.UI.Navigation.Urls;
using Volo.Abp.VirtualFileSystem;

namespace KnowPulse;

[DependsOn(
    typeof(KnowPulseHttpApiModule),
    typeof(AbpAutofacModule),
    typeof(AbpAspNetCoreMultiTenancyModule),
    typeof(KnowPulseApplicationModule),
    typeof(KnowPulseEntityFrameworkCoreModule),
    typeof(AbpBlobStoringMinioModule),
    typeof(AbpAspNetCoreMvcUiLeptonXLiteThemeModule),
    typeof(AbpAccountWebOpenIddictModule),
    typeof(AbpAspNetCoreSerilogModule),
    typeof(AbpSwashbuckleModule)
)]
public class KnowPulseHttpApiHostModule : AbpModule
{
    /// <summary>
    /// 预配置 OpenIddict 本地验证服务。
    /// </summary>
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        PreConfigure<OpenIddictServerBuilder>(builder => builder.Configure(options => options.GrantTypes.Add(LegacyWeChatTokenExtensionGrant.GrantType)));

        PreConfigure<OpenIddictServerBuilder>(builder => builder.Configure(options => options.GrantTypes.Add(WeChatLoginValues.GrantType)));
        PreConfigure<OpenIddictBuilder>(builder =>
        {
            builder.AddValidation(options =>
            {
                options.AddAudiences("KnowPulse");
                options.UseLocalServer();
                options.UseAspNetCore();
            });
        });
    }

    /// <summary>
    /// 注册 HTTP Host 层认证、审计、Blob、Swagger、CORS 和虚拟文件系统配置。
    /// </summary>
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();
        var hostingEnvironment = context.Services.GetHostingEnvironment();

        ConfigureAuthentication(context);
        context.Services.AddSingleton<WeChatRequestLimiter>();
        Configure<AbpOpenIddictExtensionGrantsOptions>(options => options.Grants.Add(WeChatLoginValues.GrantType, new BoundWeChatTokenExtensionGrant()));
        ConfigureBundles();
        ConfigureUrls(configuration);
        ConfigureAuditing();
        ConfigureBlobStoring(configuration);
        ConfigureKnowledgeIndexingWorker(context, configuration);
        ConfigureConventionalControllers();
        ConfigureVirtualFileSystem(context);
        ConfigureCors(context, configuration);
        ConfigureSwaggerServices(context, configuration);
    }

    /// <summary>
    /// 注册 P3 知识索引后台消费者。
    /// </summary>
    private static void ConfigureKnowledgeIndexingWorker(
        ServiceConfigurationContext context,
        IConfiguration configuration)
    {
        context.Services.Configure<KnowledgeIndexJobWorkerOptions>(
            configuration.GetSection(KnowledgeIndexJobWorkerOptions.SectionName));
        context.Services.AddHostedService<KnowledgeIndexJobBackgroundWorker>();
        context.Services.AddHostedService<PersonalKnowledgeExportBackgroundWorker>();
        context.Services.AddHostedService<AiQuestionGenerationBackgroundWorker>();
        context.Services.AddHostedService<EducationAssessmentDeadlineBackgroundWorker>();
        context.Services.AddHostedService<EducationLearningReportBackgroundWorker>();
        context.Services.AddHostedService<EducationTeachingPlanGenerationBackgroundWorker>();
    }

    /// <summary>
    /// 配置 Bearer token 认证转发和动态 claims。
    /// </summary>
    private void ConfigureAuthentication(ServiceConfigurationContext context)
    {
        context.Services.Configure<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(
            Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme, options =>
            {
                options.Events.OnRedirectToLogin = redirect =>
                {
                    if (redirect.Request.Path.StartsWithSegments("/api")) redirect.Response.StatusCode = 401;
                    else redirect.Response.Redirect(redirect.RedirectUri);
                    return System.Threading.Tasks.Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = redirect =>
                {
                    if (redirect.Request.Path.StartsWithSegments("/api")) redirect.Response.StatusCode = 403;
                    else redirect.Response.Redirect(redirect.RedirectUri);
                    return System.Threading.Tasks.Task.CompletedTask;
                };
            });
        Configure<AbpOpenIddictExtensionGrantsOptions>(options => options.Grants.Add(LegacyWeChatTokenExtensionGrant.GrantType, new LegacyWeChatTokenExtensionGrant()));
        context.Services.ForwardIdentityAuthenticationForBearer(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        context.Services.Configure<AbpClaimsPrincipalFactoryOptions>(options =>
        {
            options.IsDynamicClaimsEnabled = true;
        });
    }

    /// <summary>
    /// 配置 LeptonX 全局样式 bundle。
    /// </summary>
    private void ConfigureBundles()
    {
        Configure<AbpBundlingOptions>(options =>
        {
            options.StyleBundles.Configure(
                LeptonXLiteThemeBundles.Styles.Global,
                bundle =>
                {
                    bundle.AddFiles("/global-styles.css");
                }
            );
        });
    }

    /// <summary>
    /// 配置应用根地址和允许跳转地址。
    /// </summary>
    private void ConfigureUrls(IConfiguration configuration)
    {
        Configure<AppUrlOptions>(options =>
        {
            options.Applications["MVC"].RootUrl = configuration["App:SelfUrl"];
            options.RedirectAllowedUrls.AddRange(configuration["App:RedirectAllowedUrls"]?.Split(',') ?? Array.Empty<string>());

            options.Applications["Angular"].RootUrl = configuration["App:ClientUrl"];
            options.Applications["Angular"].Urls[AccountUrlNames.PasswordReset] = "account/reset-password";
        });
    }

    /// <summary>
    /// 配置 ABP 审计策略，避免大正文和二进制流进入通用审计日志。
    /// </summary>
    private void ConfigureAuditing()
    {
        Configure<AbpAuditingOptions>(options =>
        {
            options.ApplicationName = "KnowPulse";
            options.Contributors.Add(new PrivateNotebookAuditContributor());
            options.IsEnabledForGetRequests = true;
            options.IgnoredTypes.Add(typeof(WeChatAccountBinding));
            options.IgnoredTypes.Add(typeof(WeChatBindingCodeDto));
            options.IgnoredTypes.Add(typeof(IssueWeChatBindingCodeDto));
            options.IgnoredTypes.Add(typeof(ExchangeWeChatBindingCodeDto));
            options.IgnoredTypes.Add(typeof(CreateKnowledgeVersionDto));
            options.IgnoredTypes.Add(typeof(CreateKnowledgeItemDto));
            options.IgnoredTypes.Add(typeof(UpdateKnowledgeItemDto));
            options.IgnoredTypes.Add(typeof(PersonalKnowledgeOrganizationInput));
            options.IgnoredTypes.Add(typeof(UpdatePersonalKnowledgeOrganizationInput));
            options.IgnoredTypes.Add(typeof(GetPersonalKnowledgeItemListInput));
            options.IgnoredTypes.Add(typeof(GetKnowledgeManagementListInput));
            options.IgnoredTypes.Add(typeof(IRemoteStreamContent));
            options.IgnoredTypes.Add(typeof(RemoteStreamContent));
            options.IgnoredTypes.Add(typeof(KnowledgeVersion));
            options.IgnoredTypes.Add(typeof(KnowledgeChunk));
            options.IgnoredTypes.Add(typeof(EducationAssessmentDraftContentDto));
            options.IgnoredTypes.Add(typeof(SaveEducationAssessmentResponseDto));
            options.IgnoredTypes.Add(typeof(EducationAssessmentSubmissionPreviewDto));
            options.IgnoredTypes.Add(typeof(EducationMaterialContentDto));
            options.IgnoredTypes.Add(typeof(EducationMaterialSourcePreviewDto));
            options.IgnoredTypes.Add(typeof(EducationMaterialSourceDto));
            options.IgnoredTypes.Add(typeof(SaveEducationAssessmentGradeDraftDto));
            options.IgnoredTypes.Add(typeof(ConfirmEducationAssessmentGradeDto));
            options.IgnoredTypes.Add(typeof(PublishEducationAssessmentResultsDto));
            options.IgnoredTypes.Add(typeof(RequestEducationAssessmentScoreReviewDto));
            options.IgnoredTypes.Add(typeof(ResolveEducationAssessmentScoreReviewDto));
            options.IgnoredTypes.Add(typeof(EducationAssessmentResponseRevision));
            options.IgnoredTypes.Add(typeof(EducationAssessmentGradeRevision));
            options.IgnoredTypes.Add(typeof(EducationAssessmentScoreReviewRequest));
            options.IgnoredTypes.Add(typeof(CreateEducationTeacherObservationDto));
            options.IgnoredTypes.Add(typeof(SaveEducationFeedbackPhraseDto));
            options.IgnoredTypes.Add(typeof(EducationFeedbackPhraseRevision));
            options.IgnoredTypes.Add(typeof(SaveEducationFeedbackDraftDto));
            options.IgnoredTypes.Add(typeof(PublishEducationFeedbackDto));
            options.IgnoredTypes.Add(typeof(WithdrawEducationFeedbackDto));
            options.IgnoredTypes.Add(typeof(EducationTeacherObservation));
            options.IgnoredTypes.Add(typeof(EducationStudentFeedbackRevision));
            options.IgnoredTypes.Add(typeof(SaveEducationEvidenceQualificationDto));
            options.IgnoredTypes.Add(typeof(SaveEducationOriginRelationDto));
            options.IgnoredTypes.Add(typeof(SaveEducationHintReviewDto));
            options.IgnoredTypes.Add(typeof(EducationEvidenceQualificationRevision));
            options.IgnoredTypes.Add(typeof(EducationQuestionOriginRelationRevision));
            options.IgnoredTypes.Add(typeof(EducationExposureHintReviewRevision));
            options.IgnoredTypes.Add(typeof(EducationHintContextDto));
            options.IgnoredTypes.Add(typeof(EducationLearningReportJob));
            options.IgnoredTypes.Add(typeof(EducationLearningReportSnapshot));
            options.IgnoredTypes.Add(typeof(EducationLearningReportStudentSnapshot));
            options.IgnoredTypes.Add(typeof(EducationLearningReportSource));
            options.IgnoredTypes.Add(typeof(EducationLearningReportTrendDto));
            options.IgnoredTypes.Add(typeof(GenerateEducationTeachingPlanDraftDto));
            options.IgnoredTypes.Add(typeof(RetryEducationTeachingPlanGenerationDto));
            options.IgnoredTypes.Add(typeof(EducationTeachingPlanGenerationDto));
            options.IgnoredTypes.Add(typeof(EducationTeachingPlanGenerationListDto));
            options.IgnoredTypes.Add(typeof(GetEducationTeachingPlanGenerationsInput));
            options.IgnoredTypes.Add(typeof(EducationPlanGenerationRequest));
            options.IgnoredTypes.Add(typeof(EducationTeachingPlanGenerationJob));
            options.IgnoredTypes.Add(typeof(DecideEducationPlanExecutionDto));
            options.IgnoredTypes.Add(typeof(EducationPlanExecutionReviewDto));
            options.IgnoredTypes.Add(typeof(EducationPlanDecisionDto));
            options.IgnoredTypes.Add(typeof(EducationPlanExecutionEvidence));
            options.IgnoredTypes.Add(typeof(EducationTeachingPlanDecisionRevision));
            options.IgnoredTypes.Add(typeof(GetEducationLearningTrendInput));
            options.IgnoredTypes.Add(typeof(EducationLearningReportStudentResult));
            options.IgnoredTypes.Add(typeof(SaveEducationMasteryRuleDto));
            options.IgnoredTypes.Add(typeof(EducationMasteryRuleRevision));
            options.IgnoredTypes.Add(typeof(EducationStudentGrowthSnapshot));
            options.IgnoredTypes.Add(typeof(EducationStudentGrowthSource));
            options.IgnoredTypes.Add(typeof(EducationStudentGrowthValue));
            options.IgnoredTypes.Add(typeof(EducationStudentGrowthForSchoolDto));
            options.IgnoredTypes.Add(typeof(EducationStudentGrowthContextsForSchoolDto));
            options.IgnoredTypes.Add(typeof(EducationStudentGrowthEvidenceForSchoolDto));
            options.IgnoredTypes.Add(typeof(SaveEducationTeachingPlanDraftDto));
            options.IgnoredTypes.Add(typeof(TransitionEducationTeachingPlanDto));
            options.IgnoredTypes.Add(typeof(EducationTeachingPlanRevision));
            options.IgnoredTypes.Add(typeof(EducationTeachingPlanHintReviewRevision));
            options.IgnoredTypes.Add(typeof(EducationTeachingPlanHintContextDto));
            options.IgnoredTypes.Add(typeof(SaveEducationMyActivityDto));
            options.IgnoredTypes.Add(typeof(SaveEducationActivityFollowUpDto));
            options.IgnoredTypes.Add(typeof(EducationTeachingActivityRevision));
            options.IgnoredTypes.Add(typeof(EducationStudentActivityEventDto));
            options.IgnoredTypes.Add(typeof(EducationTeacherActivityEventDto));
            options.IgnoredTypes.Add(typeof(EducationTeacherActivityProgressDto));
            options.IgnoredTypes.Add(typeof(EducationMyActivitiesDto));
            options.IgnoredTypes.Add(typeof(BindEducationFormalAssessmentDto));
            options.IgnoredTypes.Add(typeof(ReviewEducationFormalAssessmentDto));
            options.IgnoredTypes.Add(typeof(EducationFormalAssessmentPreviewDto));
            options.IgnoredTypes.Add(typeof(EducationFormalAssessmentDetailsDto));
            options.IgnoredTypes.Add(typeof(EducationFormalQuestionPreviewDto));
            options.IgnoredTypes.Add(typeof(EducationFormalReviewDto));
            options.IgnoredTypes.Add(typeof(EducationTeachingFormalAssessment));
            options.IgnoredTypes.Add(typeof(EducationTeachingFormalReview));
            options.IgnoredTypes.Add(typeof(EducationTeachingPlanHintReviewRevisionDto));
            options.IgnoredTypes.Add(typeof(EducationTeachingPlanBasis));
            options.IgnoredTypes.Add(typeof(EducationTeachingPlanContent));
            options.IgnoredTypes.Add(typeof(EducationStudentTeachingPlanContent));
            options.IgnoredTypes.Add(typeof(SaveEducationStudyTaskDto));
            options.IgnoredTypes.Add(typeof(PublishEducationStudyTaskDto));
            options.IgnoredTypes.Add(typeof(WithdrawEducationStudyTaskDto));
            options.IgnoredTypes.Add(typeof(SaveEducationStudyFollowUpDto));
            options.IgnoredTypes.Add(typeof(EducationStudyTaskContent));
            options.IgnoredTypes.Add(typeof(EducationStudyTaskRevision));
            options.IgnoredTypes.Add(typeof(EducationStudyEvent));
            options.IgnoredTypes.Add(typeof(EducationStudyRevisionDto));
            options.IgnoredTypes.Add(typeof(EducationStudyTaskDto));
            options.IgnoredTypes.Add(typeof(EducationStudyRosterDto));
            options.IgnoredTypes.Add(typeof(EducationMyStudyTaskDto));
            options.IgnoredTypes.Add(typeof(EducationTeacherStudyRecipientDto));
            options.IgnoredTypes.Add(typeof(EducationStudentStudyEventDto));
            options.IgnoredTypes.Add(typeof(EducationTeacherStudyEventDto));
            options.IgnoredTypes.Add(typeof(EducationStudyProgressDto));
            options.IgnoredTypes.Add(typeof(EducationStudyHintContextDto));
            options.IgnoredTypes.Add(typeof(EducationStudyHintReviewRevisionDto));
            options.IgnoredTypes.Add(typeof(EducationStudyHintReview));
            options.IgnoredTypes.Add(typeof(EducationStudyHintReviewRevision));
            options.IgnoredTypes.Add(typeof(SaveEducationCorrectionDraftDto));
            options.IgnoredTypes.Add(typeof(PublishEducationCorrectionDto));
            options.IgnoredTypes.Add(typeof(WithdrawEducationCorrectionDto));
            options.IgnoredTypes.Add(typeof(PreviewEducationCorrectionDto));
            options.IgnoredTypes.Add(typeof(SubmitEducationCorrectionDto));
            options.IgnoredTypes.Add(typeof(PublishEducationCorrectionReviewDto));
            options.IgnoredTypes.Add(typeof(EducationCorrectionContent));
            options.IgnoredTypes.Add(typeof(EducationCorrectionInstructions));
            options.IgnoredTypes.Add(typeof(EducationCorrectionTargetDto));
            options.IgnoredTypes.Add(typeof(EducationCorrectionPublishPreviewDto));
            options.IgnoredTypes.Add(typeof(EducationCorrectionTaskRevision));
            options.IgnoredTypes.Add(typeof(EducationCorrectionSubmission));
            options.IgnoredTypes.Add(typeof(EducationCorrectionReview));
            options.IgnoredTypes.Add(typeof(EducationCorrectionRevisionDto));
            options.IgnoredTypes.Add(typeof(EducationCorrectionTaskDto));
            options.IgnoredTypes.Add(typeof(EducationCorrectionSubmissionPreviewDto));
            options.IgnoredTypes.Add(typeof(EducationCorrectionSubmissionDto));
            options.IgnoredTypes.Add(typeof(EducationCorrectionReviewDto));
            options.IgnoredTypes.Add(typeof(EducationMyCorrectionDto));
            options.IgnoredTypes.Add(typeof(EducationCorrectionProgressDto));
            options.IgnoredTypes.Add(typeof(EducationCorrectionHistoryDto));
            options.IgnoredTypes.Add(typeof(EducationLearningWorkloadDto));
            options.IgnoredTypes.Add(typeof(EducationLearningWorkloadItemDto));
            options.IgnoredTypes.Add(typeof(PreviewEducationNotebookSourceDto));
            options.IgnoredTypes.Add(typeof(AddEducationNotebookSourceDto));
            options.IgnoredTypes.Add(typeof(RemoveEducationNotebookSourceDto));
            options.IgnoredTypes.Add(typeof(EducationNotebookSourcePreviewDto));
            options.IgnoredTypes.Add(typeof(EducationNotebookSourceDto));
            options.IgnoredTypes.Add(typeof(EducationNotebookSourcesDto));
            options.IgnoredTypes.Add(typeof(EducationNotebookSourcePointer));
            options.IgnoredTypes.Add(typeof(StartEducationPracticeDto));
            options.IgnoredTypes.Add(typeof(PersonalNotebookContentDto));
            options.IgnoredTypes.Add(typeof(PersonalNotebookMetadataDto));
            options.IgnoredTypes.Add(typeof(PersonalNotebookVersionDto));
            options.IgnoredTypes.Add(typeof(CreatePersonalNotebookDto));
            options.IgnoredTypes.Add(typeof(SavePersonalNotebookDto));
            options.IgnoredTypes.Add(typeof(PersonalNotebookDto));
            options.IgnoredTypes.Add(typeof(PersonalNotebookVersionContentDto));
            options.IgnoredTypes.Add(typeof(GetEducationPracticeListDto));
            options.IgnoredTypes.Add(typeof(PreviewEducationPracticeDto));
            options.IgnoredTypes.Add(typeof(SubmitEducationPracticeDto));
            options.IgnoredTypes.Add(typeof(EducationPracticeDto));
            options.IgnoredTypes.Add(typeof(EducationPracticeFeedbackDto));
            options.IgnoredTypes.Add(typeof(EducationPracticeEvaluation));
            options.IgnoredTypes.Add(typeof(EducationQuestionPractice));
            options.IgnoredTypes.Add(typeof(EducationPracticeImage));
        });
    }

    /// <summary>
    /// 配置知识附件 Blob 存储容器。
    /// </summary>
    private void ConfigureBlobStoring(IConfiguration configuration)
    {
        Configure<AbpBlobStoringOptions>(options =>
        {
            options.Containers.Configure<KnowledgeAttachmentBlobContainer>(container =>
            {
                container.UseMinio(minio =>
                {
                    minio.BucketName = configuration["BlobStoring:Minio:BucketName"] ?? "knowpulse-knowledge";
                    minio.EndPoint = configuration["BlobStoring:Minio:EndPoint"] ?? "localhost:9000";
                    minio.AccessKey = configuration["BlobStoring:Minio:AccessKey"] ?? "knowpulse";
                    minio.SecretKey = configuration["BlobStoring:Minio:SecretKey"] ?? "knowpulse-local-password";
                    minio.WithSSL = configuration.GetValue("BlobStoring:Minio:WithSSL", false);
                    minio.CreateBucketIfNotExists = configuration.GetValue("BlobStoring:Minio:CreateBucketIfNotExists", true);
                });
            });
            options.Containers.Configure<EducationAssessmentImageBlobContainer>(container =>
            {
                container.UseMinio(minio =>
                {
                    minio.BucketName = configuration["BlobStoring:Minio:EducationAnswerBucketName"] ?? "knowpulse-education-answers";
                    minio.EndPoint = configuration["BlobStoring:Minio:EndPoint"] ?? "localhost:9000";
                    minio.AccessKey = configuration["BlobStoring:Minio:AccessKey"] ?? "knowpulse";
                    minio.SecretKey = configuration["BlobStoring:Minio:SecretKey"] ?? "knowpulse-local-password";
                    minio.WithSSL = configuration.GetValue("BlobStoring:Minio:WithSSL", false);
                    minio.CreateBucketIfNotExists = configuration.GetValue("BlobStoring:Minio:CreateBucketIfNotExists", true);
                });
            });
            options.Containers.Configure<EducationCorrectionImageBlobContainer>(container =>
            {
                container.UseMinio(minio =>
                {
                    minio.BucketName = configuration["BlobStoring:Minio:EducationAnswerBucketName"] ?? "knowpulse-education-answers";
                    minio.EndPoint = configuration["BlobStoring:Minio:EndPoint"] ?? "localhost:9000";
                    minio.AccessKey = configuration["BlobStoring:Minio:AccessKey"] ?? "knowpulse";
                    minio.SecretKey = configuration["BlobStoring:Minio:SecretKey"] ?? "knowpulse-local-password";
                    minio.WithSSL = configuration.GetValue("BlobStoring:Minio:WithSSL", false);
                    minio.CreateBucketIfNotExists = configuration.GetValue("BlobStoring:Minio:CreateBucketIfNotExists", true);
                });
            });
            options.Containers.Configure<EducationPracticeImageBlobContainer>(container =>
            {
                container.UseMinio(minio =>
                {
                    minio.BucketName = configuration["BlobStoring:Minio:EducationAnswerBucketName"] ?? "knowpulse-education-answers";
                    minio.EndPoint = configuration["BlobStoring:Minio:EndPoint"] ?? "localhost:9000";
                    minio.AccessKey = configuration["BlobStoring:Minio:AccessKey"] ?? "knowpulse";
                    minio.SecretKey = configuration["BlobStoring:Minio:SecretKey"] ?? "knowpulse-local-password";
                    minio.WithSSL = configuration.GetValue("BlobStoring:Minio:WithSSL", false);
                    minio.CreateBucketIfNotExists = configuration.GetValue("BlobStoring:Minio:CreateBucketIfNotExists", true);
                });
            });
        });
    }

    /// <summary>
    /// 开发环境下将嵌入式虚拟文件替换为物理源码文件。
    /// </summary>
    private void ConfigureVirtualFileSystem(ServiceConfigurationContext context)
    {
        var hostingEnvironment = context.Services.GetHostingEnvironment();

        if (hostingEnvironment.IsDevelopment())
        {
            Configure<AbpVirtualFileSystemOptions>(options =>
            {
                options.FileSets.ReplaceEmbeddedByPhysical<KnowPulseDomainSharedModule>(
                    Path.Combine(hostingEnvironment.ContentRootPath,
                        $"..{Path.DirectorySeparatorChar}KnowPulse.Domain.Shared"));
                options.FileSets.ReplaceEmbeddedByPhysical<KnowPulseDomainModule>(
                    Path.Combine(hostingEnvironment.ContentRootPath,
                        $"..{Path.DirectorySeparatorChar}KnowPulse.Domain"));
                options.FileSets.ReplaceEmbeddedByPhysical<KnowPulseApplicationContractsModule>(
                    Path.Combine(hostingEnvironment.ContentRootPath,
                        $"..{Path.DirectorySeparatorChar}KnowPulse.Application.Contracts"));
                options.FileSets.ReplaceEmbeddedByPhysical<KnowPulseApplicationModule>(
                    Path.Combine(hostingEnvironment.ContentRootPath,
                        $"..{Path.DirectorySeparatorChar}KnowPulse.Application"));
            });
        }
    }

    /// <summary>
    /// 启用应用层 conventional controller。
    /// </summary>
    private void ConfigureConventionalControllers()
    {
        Configure<AbpAspNetCoreMvcOptions>(options =>
        {
            options.ConventionalControllers.FormBodyBindingIgnoredTypes.Add(typeof(UploadEducationAssessmentAnswerImageDto));
            options.ConventionalControllers.Create(typeof(KnowPulseApplicationModule).Assembly);
        });
    }

    /// <summary>
    /// 配置 Swagger/OpenAPI 文档和 OAuth 集成。
    /// </summary>
    private static void ConfigureSwaggerServices(ServiceConfigurationContext context, IConfiguration configuration)
    {
        context.Services.AddAbpSwaggerGenWithOAuth(
            configuration["AuthServer:Authority"]!,
            new Dictionary<string, string>
            {
                    {"KnowPulse", "KnowPulse API"}
            },
            options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo { Title = "KnowPulse API", Version = "v1" });
                options.DocInclusionPredicate((docName, description) => true);
                options.CustomSchemaIds(type => type.FullName);
            });
    }

    /// <summary>
    /// 配置跨域来源和 ABP 暴露 header。
    /// </summary>
    private void ConfigureCors(ServiceConfigurationContext context, IConfiguration configuration)
    {
        context.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(builder =>
            {
                builder
                    .WithOrigins(configuration["App:CorsOrigins"]?
                        .Split(",", StringSplitOptions.RemoveEmptyEntries)
                        .Select(o => o.RemovePostFix("/"))
                        .ToArray() ?? Array.Empty<string>())
                    .WithAbpExposedHeaders()
                    .SetIsOriginAllowedToAllowWildcardSubdomains()
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });
    }

    /// <summary>
    /// 配置 ASP.NET Core 请求管道。
    /// </summary>
    public override void OnApplicationInitialization(ApplicationInitializationContext context)
    {
        var app = context.GetApplicationBuilder();
        var env = context.GetEnvironment();

        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseAbpRequestLocalization();

        if (!env.IsDevelopment())
        {
            app.UseWhen(http => !http.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) &&
                !http.Request.Path.StartsWithSegments("/connect", StringComparison.OrdinalIgnoreCase), branch => branch.UseErrorPage());
        }

        app.UseCorrelationId();
        app.MapAbpStaticAssets();
        app.UseRouting();
        app.UseCors();
        app.UseAuthentication();
        app.UseAbpOpenIddictValidation();

        if (MultiTenancyConsts.IsEnabled)
        {
            app.UseMultiTenancy();
        }
        app.UseUnitOfWork();
        app.UseDynamicClaims();
        app.UseMiddleware<WeChatBindingSessionMiddleware>();
        app.UseAuthorization();

        app.UseSwagger();
        app.UseAbpSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "KnowPulse API");

            var configuration = context.ServiceProvider.GetRequiredService<IConfiguration>();
            c.OAuthClientId(configuration["AuthServer:SwaggerClientId"]);
            c.OAuthScopes("KnowPulse");
        });

        app.UseAuditing();
        app.UseAbpSerilogEnrichers();
        app.UseConfiguredEndpoints();
    }
}
