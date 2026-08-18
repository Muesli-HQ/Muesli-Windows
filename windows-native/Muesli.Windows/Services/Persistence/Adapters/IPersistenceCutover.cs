namespace Muesli.Windows.Services.Persistence;

/// <summary>
/// L27 cutover surface. Wave 1 production wiring is off
/// (<see cref="PersistenceCutoverGate.DefaultEnabled"/>). Agent E calls this once during
/// FeatureServiceScope construction when the flag is enabled, before FeatureRuntime loads history.
/// Do not invoke it from FeatureRuntime itself.
/// </summary>
/// <remarks>
/// Agent E hook for <c>FeatureServiceScope.CreateProduction</c> (replace the bare
/// <c>new AppDataStore()</c> history path; do not add from the data lane):
/// <code>
/// if (PersistenceCutoverGate.IsEnabled)
/// {
///     var cutover = new PersistenceCutover(PersistencePaths.DefaultDataDirectory);
///     var migrated = cutover.EnsureMigrated();
///     if (!migrated.Succeeded)
///         throw new InvalidOperationException(migrated.Failure ?? "History cutover failed.");
/// }
/// </code>
/// Then construct <see cref="SqliteLibraryHistoryAdapter"/> and hand it to FeatureRuntime.
/// </remarks>
public interface IPersistenceCutover
{
    JsonToSqliteMigrationResult EnsureMigrated();

    void Rollback(JsonToSqliteMigrationResult result);
}
