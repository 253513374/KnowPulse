using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using KnowPulse.Blazor.Client;
using Volo.Abp;
using Volo.Abp.AspNetCore.Components.Web;
using Volo.Abp.AspNetCore.Components.Server.Theming.MudBlazor;
using Volo.Abp.AspNetCore.Components.WebAssembly.MudBlazorLeptonXLiteTheme.Bundling;
using Volo.Abp.AspNetCore.Components.WebAssembly.Theming.MudBlazor.Bundling;
using Volo.Abp.AspNetCore.Components.WebAssembly.WebApp;
using Volo.Abp.AspNetCore.Mvc.UI.Bundling;
using Volo.Abp.Autofac;
using Volo.Abp.Modularity;

namespace KnowPulse.Blazor;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(AbpAspNetCoreMvcUiBundlingModule),
    typeof(AbpAspNetCoreComponentsServerThemingMudBlazorModule),
    typeof(AbpAspNetCoreComponentsWebAssemblyMudBlazorLeptonXLiteThemeBundlingModule)
)]
public class KnowPulseBlazorModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        PreConfigure<AbpAspNetCoreComponentsWebOptions>(options =>
        {
            options.IsBlazorWebApp = true;
        });
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        //https://github.com/dotnet/aspnetcore/issues/52530
        Configure<RouteOptions>(options =>
        {
            options.SuppressCheckForUnhandledSecurityMetadata = true;
        });

        // Add services to the container.
        context.Services.AddRazorComponents()
            .AddInteractiveWebAssemblyComponents();

        ConfigureBundles();
    }

    private void ConfigureBundles()
    {
        Configure<AbpBundlingOptions>(options =>
        {
            options.StyleBundles.Configure(
                BlazorWebAssemblyMudBlazorStandardBundles.Styles.Global,
                bundle =>
                {
                    bundle.AddFiles("/main.css");
                    bundle.AddFiles("/css/teacher-shell.css");
                    bundle.AddFiles("/css/teacher-materials.css");
                    bundle.AddFiles("/css/admin.css");
                    bundle.AddFiles("/css/rag-admin.css");
                    bundle.AddFiles("/css/forms.css");
                    bundle.AddFiles(new BundleFile("/KnowPulse.Blazor.Client.styles.css", true));
                }
            );

            options.ScriptBundles.Configure(
                BlazorWebAssemblyMudBlazorStandardBundles.Scripts.Global,
                bundle => bundle.AddFiles("/knowpulse.js")
            );
        });
    }

    public override void OnApplicationInitialization(ApplicationInitializationContext context)
    {
        var env = context.GetEnvironment();
        var app = context.GetApplicationBuilder();

        // Configure the HTTP request pipeline.
        if (env.IsDevelopment())
        {
            app.UseWebAssemblyDebugging();
        }
        else
        {
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.MapAbpStaticAssets();
        app.UseRouting();
        app.UseAntiforgery();

        app.UseConfiguredEndpoints(builder =>
        {
            builder.MapRazorComponents<App>()
                .AddInteractiveWebAssemblyRenderMode()
                .AddAdditionalAssemblies(WebAppAdditionalAssembliesHelper.GetAssemblies<KnowPulseBlazorClientModule>());
        });
    }
}
