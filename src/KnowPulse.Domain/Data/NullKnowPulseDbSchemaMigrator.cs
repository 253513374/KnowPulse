using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace KnowPulse.Data;

/* This is used if database provider does't define
 * IKnowPulseDbSchemaMigrator implementation.
 */
public class NullKnowPulseDbSchemaMigrator : IKnowPulseDbSchemaMigrator, ITransientDependency
{
    public Task MigrateAsync()
    {
        return Task.CompletedTask;
    }
}
