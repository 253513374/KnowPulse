using System.Threading.Tasks;

namespace KnowPulse.Data;

public interface IKnowPulseDbSchemaMigrator
{
    Task MigrateAsync();
}
