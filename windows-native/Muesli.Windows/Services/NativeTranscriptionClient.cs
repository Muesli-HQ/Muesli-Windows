using System.IO;

namespace Muesli.Windows.Services;

internal interface ITranscriptionModelSession : IDisposable
{
    Task<ModelOperationResult> InitializeAsync();
    Task<TranscriptionResult> TranscribeAsync(byte[] audioBytes);

    /// <summary>
    /// Transcribes a media file. <paramref name="progress"/> and <paramref name="cancellationToken"/>
    /// are honoured at every point the work can actually be subdivided; see the implementations for
    /// which stages are interruptible.
    /// </summary>
    Task<TranscriptionResult> TranscribeFileAsync(
        string title,
        string filePath,
        IProgress<TranscriptionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

internal interface ITranscriptionModelSessionFactory
{
    ITranscriptionModelSession Create(TranscriptionModelDefinition model);
}

internal sealed class NativeTranscriptionModelSessionFactory : ITranscriptionModelSessionFactory
{
    public ITranscriptionModelSession Create(TranscriptionModelDefinition model) =>
        model.Kind == NativeAsrModelKind.Parakeet
            ? new NativeParakeetClient()
            : new NativeOfflineAsrClient(model);
}

/// <summary>
/// Owns exactly one role-specific recognizer. Model selection is instance scoped:
/// callers must explicitly create one client for dictation or final-meeting work.
/// </summary>
public sealed class NativeTranscriptionClient : IDisposable
{
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly ITranscriptionModelSessionFactory _sessionFactory;
    private readonly Func<TranscriptionModelDefinition, bool> _isReady;
    private TranscriptionModelDefinition _model;
    private ITranscriptionModelSession? _session;
    private bool _disposed;

    public NativeTranscriptionClient(string? modelId = null)
        : this(TranscriptionModelCatalog.GetRequired(modelId ?? TranscriptionModelCatalog.DefaultModelId),
            new NativeTranscriptionModelSessionFactory())
    {
    }

    internal NativeTranscriptionClient(
        TranscriptionModelDefinition model,
        ITranscriptionModelSessionFactory sessionFactory,
        Func<TranscriptionModelDefinition, bool>? isReady = null)
    {
        _model = model;
        _sessionFactory = sessionFactory;
        _isReady = isReady ?? TranscriptionModelReadiness.IsVerified;
    }

    public string EngineId => $"native-sherpa-onnx/{_model.Kind.ToString().ToLowerInvariant()}";
    public string ModelId => _model.Id;
    public string ModelDisplayName => _model.DisplayName;
    public TranscriptionModelDefinition SelectedModel => _model;
    public bool IsSelectedModelReady => TranscriptionModelReadiness.IsVerified(_model);

    public async Task SwitchModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        var next = TranscriptionModelCatalog.GetRequired(modelId);
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (_model.Id.Equals(next.Id, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            DisposeSession();
            _model = next;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task ReleaseModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (_model.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase))
            {
                DisposeSession();
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public Task<ModelOperationResult> InitializeAsync() => UseSessionAsync(session => session.InitializeAsync());

    public Task<TranscriptionResult> TranscribeAsync(byte[] audioBytes) =>
        UseSessionAsync(session => session.TranscribeAsync(audioBytes));

    public Task<TranscriptionResult> TranscribeFileAsync(
        string title,
        string filePath,
        IProgress<TranscriptionProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        UseSessionAsync(
            session => session.TranscribeFileAsync(title, filePath, progress, cancellationToken),
            cancellationToken);

    private async Task<T> UseSessionAsync<T>(
        Func<ITranscriptionModelSession, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        // Waiting behind another transcription is itself part of the wait the user sees, so it has
        // to be cancellable too.
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (!_isReady(_model))
            {
                throw new InvalidOperationException(
                    $"{_model.DisplayName} is not downloaded and verified. Prepare it from Models first.");
            }

            _session ??= _sessionFactory.Create(_model);
            return await operation(_session);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private void DisposeSession()
    {
        _session?.Dispose();
        _session = null;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public static long ModelCacheSizeBytes() => TranscriptionModelCatalog.CacheSizeBytes();

    public static void OpenModelCacheDirectory() => TranscriptionModelCatalog.OpenCacheDirectory();

    public static void ClearAllModelCaches()
    {
        NativeParakeetClient.ClearModelCache();
        if (Directory.Exists(TranscriptionModelCatalog.ModelCacheDirectory))
        {
            Directory.Delete(TranscriptionModelCatalog.ModelCacheDirectory, recursive: true);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _operationGate.Wait();
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DisposeSession();
        }
        finally
        {
            _operationGate.Release();
            _operationGate.Dispose();
        }
    }
}
