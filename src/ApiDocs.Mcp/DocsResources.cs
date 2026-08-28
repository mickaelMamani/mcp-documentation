using ApiDocs.Application.Ports;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ApiDocs.Mcp;

/// <summary>
/// The resources of ARCHITECTURE §5.3. <c>resources/list</c> exposes every endpoint with its
/// summary: a free table of contents for the clients that read it. The bodies are the raw Markdown
/// files, without any token budget.
/// </summary>
internal static class DocsResources
{
    private const string EndpointScheme = "endpoint://";
    private const string DocScheme = "doc://";
    private const string PlatformUri = "doc://_platform";
    private const string MarkdownMimeType = "text/markdown";

    public static ValueTask<ListResourcesResult> ListAsync(
        RequestContext<ListResourcesRequestParams> context,
        CancellationToken cancellationToken)
    {
        var snapshot = Snapshots(context).Current;
        var resources = new List<Resource>(snapshot.Endpoints.Length + snapshot.Domains.Length + 1);

        for (var i = 0; i < snapshot.Endpoints.Length; i++)
        {
            var endpoint = snapshot.Endpoints[i];
            resources.Add(new Resource
            {
                Uri = EndpointScheme + endpoint.Operation.Value,
                Name = endpoint.Operation.Value,
                Description = $"{endpoint.Method} {endpoint.Route} — {endpoint.Summary}",
                MimeType = MarkdownMimeType,
            });
        }

        for (var i = 0; i < snapshot.Domains.Length; i++)
        {
            var domain = snapshot.Domains[i];
            resources.Add(new Resource
            {
                Uri = $"{DocScheme}{domain.Id}/_domain",
                Name = $"{domain.Id} domain overview",
                Description = domain.Summary,
                MimeType = MarkdownMimeType,
            });
        }

        if (snapshot.Platform.RawBody.Length > 0)
        {
            resources.Add(new Resource
            {
                Uri = PlatformUri,
                Name = "platform overview",
                Description = $"{snapshot.Platform.Platform} conventions: base URLs, authentication, pagination, errors.",
                MimeType = MarkdownMimeType,
            });
        }

        return ValueTask.FromResult(new ListResourcesResult { Resources = resources });
    }

    public static ValueTask<ListResourceTemplatesResult> ListTemplatesAsync(
        RequestContext<ListResourceTemplatesRequestParams> context,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ListResourceTemplatesResult
        {
            ResourceTemplates =
            [
                new ResourceTemplate
                {
                    UriTemplate = "endpoint://{operationId}",
                    Name = "endpoint",
                    Description = "Full Markdown documentation of one endpoint, by exact operationId.",
                    MimeType = MarkdownMimeType,
                },
                new ResourceTemplate
                {
                    UriTemplate = "doc://{domain}/_domain",
                    Name = "domain",
                    Description = "Overview, use cases, workflow, scopes and errors of one domain.",
                    MimeType = MarkdownMimeType,
                },
            ],
        });

    public static ValueTask<ReadResourceResult> ReadAsync(
        RequestContext<ReadResourceRequestParams> context,
        CancellationToken cancellationToken)
    {
        var uri = context.Params?.Uri ?? string.Empty;
        var snapshot = Snapshots(context).Current;

        if (uri.StartsWith(EndpointScheme, StringComparison.Ordinal))
        {
            var operationId = uri[EndpointScheme.Length..];
            if (!snapshot.TryGetEndpoint(operationId, out var endpoint))
            {
                throw new McpException($"Unknown endpoint resource \"{uri}\".");
            }

            var header = $"# {endpoint.Operation.Value} — {endpoint.Method} {endpoint.Route}\n{endpoint.Summary}\n";
            return Text(uri, header + "\n" + endpoint.RawBody);
        }

        if (string.Equals(uri, PlatformUri, StringComparison.Ordinal))
        {
            return snapshot.Platform.RawBody.Length > 0
                ? Text(uri, snapshot.Platform.RawBody)
                : throw new McpException("The platform overview is not available.");
        }

        if (uri.StartsWith(DocScheme, StringComparison.Ordinal))
        {
            var path = uri[DocScheme.Length..];
            var separator = path.IndexOf('/', StringComparison.Ordinal);
            var domainId = separator < 0 ? path : path[..separator];

            if (snapshot.TryGetDomain(domainId, out var domain) && domain.RawBody.Length > 0)
            {
                return Text(uri, domain.RawBody);
            }
        }

        throw new McpException($"Unknown resource \"{uri}\".");
    }

    private static IIndexSnapshotProvider Snapshots<T>(RequestContext<T> context) =>
        context.Services?.GetRequiredService<IIndexSnapshotProvider>()
        ?? throw new McpException("The server is not initialised.");

    private static ValueTask<ReadResourceResult> Text(string uri, string text) =>
        ValueTask.FromResult(new ReadResourceResult
        {
            Contents =
            [
                new TextResourceContents
                {
                    Uri = uri,
                    MimeType = MarkdownMimeType,
                    Text = text,
                },
            ],
        });
}
