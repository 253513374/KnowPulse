using Microsoft.Extensions.Localization;
using KnowPulse.Localization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Ui.Branding;

namespace KnowPulse;

[Dependency(ReplaceServices = true)]
public class KnowPulseBrandingProvider : DefaultBrandingProvider
{
    private IStringLocalizer<KnowPulseResource> _localizer;

    public KnowPulseBrandingProvider(IStringLocalizer<KnowPulseResource> localizer)
    {
        _localizer = localizer;
    }

    public override string AppName => _localizer["AppName"];
}
