namespace ApiDocs.Application.Ports;

/// <summary>
/// Holds the snapshot currently served. The swap is atomic; a failed rebuild keeps the previous
/// snapshot in place (ARCHITECTURE §6).
/// </summary>
internal interface IIndexSnapshotProvider
{
    /// <summary>The snapshot to answer requests with.</summary>
    /// <exception cref="InvalidOperationException">No snapshot has been built yet.</exception>
    IndexSnapshot Current { get; }

    bool HasSnapshot { get; }

    Task<RebuildReport> RebuildAsync(CancellationToken cancellationToken = default);
}

internal sealed record RebuildReport(
    bool Succeeded,
    int Files,
    int Chunks,
    int Endpoints,
    long ElapsedMilliseconds,
    IReadOnlyList<Domain.ValidationIssue> Issues,
    string? Error = null);
