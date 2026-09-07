using ApiDocs.Application.Ports;
using ApiDocs.Infrastructure.Ingestion;

namespace ApiDocs.Mcp.Http;

/// <summary>
/// <c>POST /admin/reindex</c> — the reload trigger of ADR #28: the documentation publication
/// pipeline calls it after pushing a new corpus, instead of restarting the service. The rebuild
/// itself lives in <see cref="IIndexSnapshotProvider"/> and is atomic: a failure keeps the
/// previous snapshot served, and this endpoint reports it with a 500 and the revision still
/// in service. Not part of the MCP surface (ADR #22 covers tools and resources, not
/// administration), so it belongs to the HTTP host alone.
/// </summary>
internal static class ReindexEndpoint
{
    public const string Route = "/admin/reindex";

    public static async Task<ReindexResult> ExecuteAsync(
        IIndexSnapshotProvider snapshots,
        IDocsRevision revision,
        ILogger logger)
    {
        Log.ReindexRequested(logger, Route);

        // Detached from the request on purpose: a publication pipeline whose HTTP call times out
        // must not abort a fetch or swap that is about to complete. The rebuild either publishes
        // a whole snapshot or leaves the previous one; there is nothing useful to cancel.
        var report = await snapshots.RebuildAsync(CancellationToken.None);

        if (!report.Succeeded)
        {
            Log.ReindexFailed(logger, report.Error ?? "unknown error");
            return new ReindexResult(
                StatusCodes.Status500InternalServerError,
                new ReindexResponse(
                    Status: "failed",
                    DocsRevision: revision.Commit,
                    Files: 0,
                    Chunks: 0,
                    Endpoints: 0,
                    RebuildMs: report.ElapsedMilliseconds,
                    ValidationIssues: [],
                    Error: report.Error));
        }

        var issues = new string[report.Issues.Count];
        for (var i = 0; i < issues.Length; i++)
        {
            issues[i] = report.Issues[i].ToString();
        }

        return new ReindexResult(
            StatusCodes.Status200OK,
            new ReindexResponse(
                Status: "ok",
                DocsRevision: revision.Commit,
                Files: report.Files,
                Chunks: report.Chunks,
                Endpoints: report.Endpoints,
                RebuildMs: report.ElapsedMilliseconds,
                ValidationIssues: issues,
                Error: null));
    }
}

/// <summary>Validation report and statistics of one reindex, mirroring what §5.2 specifies.</summary>
internal sealed record ReindexResponse(
    string Status,
    string? DocsRevision,
    int Files,
    int Chunks,
    int Endpoints,
    long RebuildMs,
    string[] ValidationIssues,
    string? Error);

internal readonly record struct ReindexResult(int StatusCode, ReindexResponse Body);
