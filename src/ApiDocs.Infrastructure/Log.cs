using Microsoft.Extensions.Logging;

namespace ApiDocs.Infrastructure;

/// <summary>
/// Structured logs of ARCHITECTURE §8. In stdio mode every one of them goes to stderr: stdout is
/// the JSON-RPC channel.
/// </summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Index rebuilt: {Files} files, {Chunks} chunks, {Endpoints} endpoints in {ElapsedMs} ms")]
    public static partial void IndexRebuilt(ILogger logger, int files, int chunks, int endpoints, long elapsedMs);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "Validation failed for {File}: [{Rule}] {Reason}")]
    public static partial void ValidationFailed(ILogger logger, string file, string rule, string reason);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Error,
        Message = "Index rebuild failed; the previous snapshot is kept")]
    public static partial void RebuildFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Debug,
        Message = "Tool {Tool} returned {Hits} results, {Tokens} tokens in {ElapsedMs} ms")]
    public static partial void ToolInvoked(ILogger logger, string tool, int hits, int tokens, long elapsedMs);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Information,
        Message = "Documentation checked out from {Repository} at {Reference} ({Commit})")]
    public static partial void DocsCheckedOut(ILogger logger, string repository, string reference, string commit);
}
