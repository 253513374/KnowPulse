using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using KnowPulse.Data;
using Volo.Abp.DependencyInjection;

namespace KnowPulse.EntityFrameworkCore;

public class EntityFrameworkCoreKnowPulseDbSchemaMigrator
    : IKnowPulseDbSchemaMigrator, ITransientDependency
{
    private readonly IServiceProvider _serviceProvider;

    public EntityFrameworkCoreKnowPulseDbSchemaMigrator(
        IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task MigrateAsync()
    {
        /* We intentionally resolve the KnowPulseDbContext
         * from IServiceProvider (instead of directly injecting it)
         * to properly get the connection string of the current tenant in the
         * current scope.
         */

        await _serviceProvider
            .GetRequiredService<KnowPulseDbContext>()
            .Database
            .MigrateAsync();
    }
}
