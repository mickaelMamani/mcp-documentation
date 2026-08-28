using ApiDocs.Application;
using ApiDocs.Application.Ports;

namespace ApiDocs.Tests;

/// <summary>
/// Serves one already built snapshot. Lets the tuning sweep evaluate many index configurations in
/// process without rebuilding the document set each time.
/// </summary>
internal sealed class FixedSnapshotProvider(IndexSnapshot snapshot) : IIndexSnapshotProvider
{
    public IndexSnapshot Current { get; } = snapshot;

    public bool HasSnapshot => true;

    public Task<RebuildReport> RebuildAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The sweep provider serves a fixed snapshot.");
}
