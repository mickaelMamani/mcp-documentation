using System.ComponentModel;
using System.Diagnostics;
using ApiDocs.Application;
using ApiDocs.Application.UseCases;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace ApiDocs.Mcp.Stdio;

/// <summary>
/// The four tools of ARCHITECTURE §5.2. Descriptions come from <see cref="ToolContracts"/> and are
/// copied verbatim from the design document: they are prompt code, versioned and benchmarked.
/// </summary>
[McpServerToolType]
internal sealed class DocsTools(
    ListDomainsUseCase listDomains,
    SearchEndpointsUseCase searchEndpoints,
    GetEndpointUseCase getEndpoint,
    SearchDocsUseCase searchDocs,
    ILogger<DocsTools> logger)
{
    [McpServerTool(Name = ToolContracts.ListDomainsName, ReadOnly = true, Idempotent = true)]
    [Description(ToolContracts.ListDomains)]
    public string ListDomains()
    {
        var stopwatch = Stopwatch.StartNew();
        var text = listDomains.Execute();
        Log.ToolInvoked(logger, ToolContracts.ListDomainsName, hits: 0, tokens: 0, stopwatch.ElapsedMilliseconds);
        return text;
    }

    [McpServerTool(Name = ToolContracts.SearchEndpointsName, ReadOnly = true, Idempotent = true)]
    [Description(ToolContracts.SearchEndpoints)]
    public string SearchEndpoints(
        [Description(ToolContracts.QueryParameter)] string query,
        [Description(ToolContracts.DomainParameter)] string? domain = null,
        [Description(ToolContracts.LimitParameter)] int? limit = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = searchEndpoints.Execute(query, domain, limit);
        Log.ToolInvoked(
            logger,
            ToolContracts.SearchEndpointsName,
            result.Matches.Count,
            tokens: 0,
            stopwatch.ElapsedMilliseconds);
        return result.Text;
    }

    [McpServerTool(Name = ToolContracts.GetEndpointName, ReadOnly = true, Idempotent = true)]
    [Description(ToolContracts.GetEndpoint)]
    public string GetEndpoint(
        [Description(ToolContracts.OperationIdParameter)] string operationId,
        [Description(ToolContracts.GetEndpointQueryParameter)] string? query = null,
        [Description(ToolContracts.LanguageParameter)] string? language = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = getEndpoint.Execute(operationId, query, language);
        Log.ToolInvoked(
            logger,
            ToolContracts.GetEndpointName,
            result.Found ? 1 : 0,
            result.Tokens,
            stopwatch.ElapsedMilliseconds);
        return result.Text;
    }

    [McpServerTool(Name = ToolContracts.SearchDocsName, ReadOnly = true, Idempotent = true)]
    [Description(ToolContracts.SearchDocs)]
    public string SearchDocs(
        [Description(ToolContracts.QueryParameter)] string query,
        [Description(ToolContracts.DomainParameter)] string? domain = null,
        [Description(ToolContracts.LimitParameter)] int? limit = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = searchDocs.Execute(query, domain, limit);
        Log.ToolInvoked(
            logger,
            ToolContracts.SearchDocsName,
            result.Hits.Count,
            tokens: 0,
            stopwatch.ElapsedMilliseconds);
        return result.Text;
    }
}
