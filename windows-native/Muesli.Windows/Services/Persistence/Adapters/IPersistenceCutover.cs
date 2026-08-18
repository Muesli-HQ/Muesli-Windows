namespace Muesli.Windows.Services.Persistence;

/// <summary>
/// L27 cutover surface. Unused in Wave 1. The integration owner calls this once during
/// FeatureServiceScope construction, before FeatureRuntime loads history. Do not invoke it from
/// FeatureRuntime itself.
/// </summary>
public interface IPersistenceCutover
{
    JsonToSqliteMigrationResult EnsureMigrated();

    void Rollback(JsonToSqliteMigrationResult result);
}
