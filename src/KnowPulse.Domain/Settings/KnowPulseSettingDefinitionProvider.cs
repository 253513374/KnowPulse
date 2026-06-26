using Volo.Abp.Settings;

namespace KnowPulse.Settings;

public class KnowPulseSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        //Define your own settings here. Example:
        //context.Add(new SettingDefinition(KnowPulseSettings.MySetting1));
    }
}
