using Microsoft.Extensions.Logging;

namespace ApiDocs.Mcp.Http;

/// <summary>
/// Startup logs of the production host. Unlike the stdio host these go to stdout: there is no
/// JSON-RPC channel to protect here, the transport is HTTP (ARCHITECTURE §7).
/// </summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Critical,
        Message = "Failed to build the documentation index: {Reason}")]
    public static partial void IndexUnavailable(ILogger logger, string reason);

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Information,
        Message = "MCP endpoint ready on {Route}: {Endpoints} endpoints, documentation revision {Revision}")]
    public static partial void EndpointReady(ILogger logger, string route, int endpoints, string revision);

    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Information,
        Message = "Reindex requested on {Route}")]
    public static partial void ReindexRequested(ILogger logger, string route);

    [LoggerMessage(
        EventId = 3003,
        Level = LogLevel.Warning,
        Message = "Reindex failed; the previous snapshot stays in service: {Reason}")]
    public static partial void ReindexFailed(ILogger logger, string reason);
}
