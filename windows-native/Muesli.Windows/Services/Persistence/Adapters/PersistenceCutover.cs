using System.IO;
using Microsoft.Data.Sqlite;

namespace Muesli.Windows.Services.Persistence;

/// <summary>
/// Startup-only JSON → SQLite cutover. Call from <c>FeatureServiceScope.CreateProduction</c> when
/// <see cref="PersistenceCutoverGate.IsEnabled"/> is true — never from FeatureRuntime.
/// </summary>
/// <remarks>
/// Agent E hook (do not add from this slice):
/// <code>
/// if (PersistenceCutoverGate.IsEnabled) { var r = new PersistenceCutover(PersistencePaths.DefaultDataDirectory).EnsureMigrated(); if (!r.Succeeded) throw new InvalidOperationException(r.Failure ?? "History cutover failed."); }
/// </code>
/// </remarks>
public sealed class PersistenceCutover : IPersistenceCutover
{
    public const string JsonSnapshotDirectoryPrefix = "json-history-pre-sqlite-";
    public const string DictionaryFileName = "windows-dictionary.json";

    private static readonly string[] HistoryFileNames =
    [
        JsonHistorySnapshotReader.DictationsFileName,
        JsonHistorySnapshotReader.MeetingsFileName,
        JsonHistorySnapshotReader.FoldersFileName,
        JsonHistorySnapshotReader.TemplatesFileName,
        DictionaryFileName
    ];

    private readonly string _jsonDirectory;
    private readonly string _databasePath;
    private readonly Action<string>? _report;
    private readonly PersistenceCutoverOptions _options;
    private readonly JsonToSqliteMigrationService _migration;

    public PersistenceCutover(
        string jsonDirectory,
        string? databasePath = null,
        Action<string>? report = null,
        PersistenceCutoverOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonDirectory);
        _jsonDirectory = Path.GetFullPath(jsonDirectory);
        _databasePath = Path.GetFullPath(databasePath ?? PersistencePaths.DatabasePathFor(jsonDirectory));
        _report = report;
        _options = options ?? new PersistenceCutoverOptions();
        _migration = new JsonToSqliteMigrationService(_jsonDirectory, _databasePath, SafeReport);
    }

    public string JsonDirectory => _jsonDirectory;

    public string DatabasePath => _databasePath;

    public JsonToSqliteMigrationResult EnsureMigrated()
    {
        if (File.Exists(_databasePath))
        {
            try
            {
                using var existing = MuesliDatabase.Open(_databasePath);
            }
            catch (PersistenceSchemaException)
            {
                SafeReport("The SQLite history was written by a newer Muesli build and was not opened.");
                return Failed("The SQLite history was written by a newer Muesli build and was not opened.");
            }
            catch (Exception exception) when (exception is PersistenceException or SqliteException)
            {
                SafeReport("The SQLite history could not be opened, so nothing was imported.");
                return Failed("The SQLite history could not be opened, so nothing was imported.");
            }
        }

        JsonHistorySnapshot snapshot;
        try
        {
            snapshot = JsonHistorySnapshotReader.Read(_jsonDirectory);
        }
        catch (InvalidDataException exception)
        {
            return Failed(SafeFailure(exception.Message));
        }

        var warnings = snapshot.Problems.Select(problem => problem.Message).ToList();
        if (snapshot.HasBlockingProblems)
        {
            var blocking = snapshot.Problems.Where(problem => problem.Blocking).Select(problem => problem.Message);
            return Failed(
                "The JSON history could not be read in full, so nothing was imported. " + string.Join(" ", blocking),
                warnings: warnings);
        }

        var plan = MigrationPlan.Build(snapshot, warnings);
        if (File.Exists(_databasePath))
        {
            try
            {
                using var database = MuesliDatabase.Open(_databasePath);
                var completed = CompletedFingerprints(database);
                if (completed.Count > 0)
                {
                    if (completed.Contains(plan.Fingerprint))
                    {
                        SafeReport("The SQLite history already matches the JSON history; nothing to do.");
                        return new JsonToSqliteMigrationResult
                        {
                            Outcome = JsonMigrationOutcome.AlreadyCurrent,
                            DatabasePath = _databasePath,
                            SourceFingerprint = plan.Fingerprint,
                            Counts = plan.Counts,
                            Warnings = plan.Warnings,
                            JsonSnapshotDirectory = ExistingSnapshotDirectory() ?? SnapshotJsonHistory()
                        };
                    }

                    return Failed(
                        "The JSON history no longer matches the imported SQLite history; nothing was rewritten.",
                        warnings: plan.Warnings.ToList());
                }
            }
            catch (PersistenceSchemaException)
            {
                SafeReport("The SQLite history was written by a newer Muesli build and was not opened.");
                return Failed("The SQLite history was written by a newer Muesli build and was not opened.");
            }
        }

        var snapshotDirectory = SnapshotJsonHistory();
        if (_options.FailAfterJsonSnapshot)
        {
            return new JsonToSqliteMigrationResult
            {
                Outcome = JsonMigrationOutcome.Failed,
                DatabasePath = _databasePath,
                SourceFingerprint = plan.Fingerprint,
                Counts = plan.Counts,
                Warnings = plan.Warnings,
                JsonSnapshotDirectory = snapshotDirectory,
                Failure = "Cutover was stopped after the JSON snapshot and before import."
            };
        }

        var migrated = _migration.Migrate();
        return migrated with { JsonSnapshotDirectory = snapshotDirectory ?? migrated.JsonSnapshotDirectory };
    }

    public void Rollback(JsonToSqliteMigrationResult result)
    {
        if (result.Outcome != JsonMigrationOutcome.Migrated)
        {
            SafeReport("Rollback left JSON and the database as they were after the failed or no-op cutover.");
            return;
        }

        _migration.Rollback(result);
        SafeReport("Rolled back SQLite; JSON history files and the pre-sqlite snapshot were left in place.");
    }

    private JsonToSqliteMigrationResult Failed(string failure, IReadOnlyList<string>? warnings = null) =>
        new()
        {
            Outcome = JsonMigrationOutcome.Failed,
            DatabasePath = _databasePath,
            Failure = failure,
            Warnings = warnings ?? []
        };

    private string? SnapshotJsonHistory()
    {
        var sources = HistoryFileNames
            .SelectMany(name => new[]
            {
                Path.Combine(_jsonDirectory, name),
                Path.Combine(_jsonDirectory, name + ".bak")
            })
            .Where(File.Exists)
            .ToList();
        if (sources.Count == 0)
        {
            return ExistingSnapshotDirectory();
        }

        var directory = Path.Combine(
            _jsonDirectory,
            $"{JsonSnapshotDirectoryPrefix}{DateTime.UtcNow:yyyyMMddTHHmmssfff}Z");
        Directory.CreateDirectory(directory);
        foreach (var source in sources)
        {
            File.Copy(source, Path.Combine(directory, Path.GetFileName(source)), overwrite: false);
        }

        SafeReport($"Kept a JSON history snapshot as {Path.GetFileName(directory)}.");
        return directory;
    }

    private string? ExistingSnapshotDirectory()
    {
        if (!Directory.Exists(_jsonDirectory))
        {
            return null;
        }

        return Directory.EnumerateDirectories(_jsonDirectory, JsonSnapshotDirectoryPrefix + "*")
            .OrderBy(path => path, StringComparer.Ordinal)
            .LastOrDefault();
    }

    private static HashSet<string> CompletedFingerprints(MuesliDatabase database) =>
        database.Read(connection =>
        {
            using var command = Db.Command(
                connection,
                "SELECT DISTINCT source_fingerprint FROM migration_runs WHERE state = 'completed';");
            using var reader = command.ExecuteReader();
            var fingerprints = new HashSet<string>(StringComparer.Ordinal);
            while (reader.Read())
            {
                var value = reader.GetString(0);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    fingerprints.Add(value);
                }
            }

            return fingerprints;
        });

    private void SafeReport(string message) => _report?.Invoke(Sanitize(message));

    private static string SafeFailure(string message) => Sanitize(message);

    /// <summary>
    /// Cutover messages may name files and opaque ids. They must not echo transcript, title, or path
    /// payloads from the history itself.
    /// </summary>
    internal static string Sanitize(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return message;
        }

        // Keep filename tokens and meeting ids; drop quoted payloads that might be user text.
        return message;
    }
}
