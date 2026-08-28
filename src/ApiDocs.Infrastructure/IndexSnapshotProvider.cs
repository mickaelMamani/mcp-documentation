using System.Diagnostics;
using ApiDocs.Application;
using ApiDocs.Application.Ports;
using ApiDocs.Domain;
using Microsoft.Extensions.Logging;

namespace ApiDocs.Infrastructure;

/// <summary>
/// Holds the snapshot currently served and swaps it atomically (ARCHITECTURE §6). A rebuild that
/// throws leaves the previous snapshot in place; a request never observes a partial index.
/// </summary>
internal sealed class IndexSnapshotProvider(
    IDocumentSource source,
    ILexicalIndexFactory indexFactory,
    ILogger<IndexSnapshotProvider> logger) : IIndexSnapshotProvider, IDisposable
{
    private readonly SemaphoreSlim _rebuildGate = new(1, 1);
    private IndexSnapshot? _current;

    public bool HasSnapshot => Volatile.Read(ref _current) is not null;

    public IndexSnapshot Current =>
        Volatile.Read(ref _current)
        ?? throw new InvalidOperationException("The documentation index is not built yet.");

    public async Task<RebuildReport> RebuildAsync(CancellationToken cancellationToken = default)
    {
        await _rebuildGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stopwatch = Stopwatch.StartNew();
            IndexSnapshot snapshot;
            DocumentSet set;

            try
            {
                set = await source.LoadAsync(cancellationToken).ConfigureAwait(false);
                snapshot = IndexSnapshot.Create(set, indexFactory, stopwatch.Elapsed);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Log.RebuildFailed(logger, exception);
                return new RebuildReport(
                    Succeeded: false,
                    Files: 0,
                    Chunks: 0,
                    Endpoints: 0,
                    stopwatch.ElapsedMilliseconds,
                    Array.Empty<ValidationIssue>(),
                    exception.Message);
            }

            stopwatch.Stop();
            var previous = Interlocked.Exchange(ref _current, snapshot);
            previous?.Dispose();

            Log.IndexRebuilt(
                logger,
                snapshot.FileCount,
                snapshot.Chunks.Length,
                snapshot.Endpoints.Length,
                stopwatch.ElapsedMilliseconds);

            return new RebuildReport(
                Succeeded: true,
                snapshot.FileCount,
                snapshot.Chunks.Length,
                snapshot.Endpoints.Length,
                stopwatch.ElapsedMilliseconds,
                snapshot.Issues);
        }
        finally
        {
            _rebuildGate.Release();
        }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _current, null)?.Dispose();
        _rebuildGate.Dispose();
    }
}
