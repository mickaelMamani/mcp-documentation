using Microsoft.Extensions.Logging;

namespace ApiDocs.Mcp.Stdio;

/// <summary>
/// Host side structured logs (ARCHITECTURE §8). The <c>query</c> is never logged: it can carry
/// business information (ARCHITECTURE §7).
/// </summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Debug,
        Message = "Tool {Tool} returned {Hits} results, {Tokens} tokens in {ElapsedMs} ms")]
    public static partial void ToolInvoked(ILogger logger, string tool, int hits, int tokens, long elapsedMs);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Debug,
        Message = "Resource {Uri} read")]
    public static partial void ResourceRead(ILogger logger, string uri);
}
