using KnowPulse.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace KnowPulse.Controllers;

/* Inherit your controllers from this class.
 */
public abstract class KnowPulseController : AbpControllerBase
{
    protected KnowPulseController()
    {
        LocalizationResource = typeof(KnowPulseResource);
    }
}
