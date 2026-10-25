using Microsoft.Extensions.Localization;
using KnowPulse.Localization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Ui.Branding;

namespace KnowPulse.Blazor.Client;

[Dependency(ReplaceServices = true)]
public class KnowPulseBrandingProvider : DefaultBrandingProvider
{
    public override string AppName => "知脉空间";
}
