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
using KnowPulse.KnowledgeIndexing;
using KnowPulse.KnowledgeBase;
using KnowPulse.MultiTenancy;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite.Bundling;
using Microsoft.OpenApi;
using OpenIddict.Validation.AspNetCore;
using KnowPulse.Authentication;
using Volo.Abp.OpenIddict.ExtensionGrantTypes;
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
        PreConfigure<OpenIddictServerBuilder>(builder => builder.Configure(options => options.GrantTypes.Add(WeChatTokenExtensionGrant.GrantType)));
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
        Configure<AbpOpenIddictExtensionGrantsOptions>(options => options.Grants.Add(WeChatTokenExtensionGrant.GrantType, new WeChatTokenExtensionGrant()));
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
            options.IsEnabledForGetRequests = true;
            options.IgnoredTypes.Add(typeof(CreateKnowledgeVersionDto));
            options.IgnoredTypes.Add(typeof(IRemoteStreamContent));
            options.IgnoredTypes.Add(typeof(RemoteStreamContent));
            options.IgnoredTypes.Add(typeof(KnowledgeVersion));
            options.IgnoredTypes.Add(typeof(KnowledgeChunk));
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
            app.UseErrorPage();
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
