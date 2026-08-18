namespace Muesli.Windows.Services.Persistence;

/// <summary>
/// L27 production gate. Default is off: JSON <see cref="AppDataStore"/> remains the running app's
/// history until Agent E wires <see cref="PersistenceCutover"/> in
/// <c>FeatureServiceScope.CreateProduction</c>. This type is not referenced from locked runtime files.
/// </summary>
public static class PersistenceCutoverGate
{
    /// <summary>Stable flag name for settings/env binding when Agent E wires the cutover.</summary>
    public const string FeatureFlagName = "L27SqliteHistoryCutover";

    /// <summary>Production default. Do not flip this to true from the data lane.</summary>
    public const bool DefaultEnabled = false;

    /// <summary>
    /// Always <see cref="DefaultEnabled"/> in this slice. Agent E may later bind this to a setting
    /// keyed by <see cref="FeatureFlagName"/>; until then, calling <see cref="PersistenceCutover.EnsureMigrated"/>
    /// from tests still runs the cutover because the gate only controls production wiring.
    /// </summary>
    public static bool IsEnabled => DefaultEnabled;
}
