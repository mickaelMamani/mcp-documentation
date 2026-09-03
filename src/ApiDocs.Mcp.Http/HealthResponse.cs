namespace ApiDocs.Mcp.Http;

/// <summary>
/// Body of <c>GET /health</c>: the snapshot actually served. <c>DocsRevision</c> is the commit the
/// corpus was read from (<see langword="null"/> in Folder mode) — the way an operator confirms a
/// reindex or a restart picked the new corpus up.
/// </summary>
internal sealed record HealthResponse(
    string Status,
    string Platform,
    string PlatformVersion,
    string? DocsRevision,
    int Files,
    int Chunks,
    int Endpoints,
    long IndexBuildMs,
    int ValidationIssues);
