# L27 Agent E integration hook

Status: **implementation exists, unwired.** L27, ORG-01, and SEARCH-01 remain incomplete. Do not edit the launch ledger from this note.

Feature flag: `PersistenceCutoverGate.FeatureFlagName` = `L27SqliteHistoryCutover`.
Default: `PersistenceCutoverGate.DefaultEnabled` = **false**. `PersistenceCutoverGate.IsEnabled` is that constant until Agent E binds it.

Agent D does not edit `AppServices.cs`. Agent E adds the following in `FeatureServiceScope.CreateProduction`, after logging/settings exist and **before** FeatureRuntime loads history. JSON `AppDataStore` stays authoritative while the flag is off.

```csharp
if (PersistenceCutoverGate.IsEnabled)
{
    var cutover = new PersistenceCutover(PersistencePaths.DefaultDataDirectory);
    var migrated = cutover.EnsureMigrated();
    if (!migrated.Succeeded)
        throw new InvalidOperationException(migrated.Failure ?? "History cutover failed.");
    var history = SqliteLibraryHistoryAdapter.Open(PersistencePaths.DefaultDataDirectory);
    // Hand `history` to FeatureRuntime instead of AppDataStore.
}
```

Dictionary leftovers stay inside `SqliteLibraryHistoryAdapter` (`windows-dictionary.json`). Settings and session journals stay atomic JSON. Do not delete JSON snapshots; retention is a later point.
