using ApiDocs.Application;
using ApiDocs.Application.Ports;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace ApiDocs.Mcp;

/// <summary>
/// The MCP surface of ARCHITECTURE §5: four tools, the resources and the server instructions.
/// Both hosts register the very same surface — only the transport differs (ADR #6, ADR #22), so a
/// tool description can never drift between stdio and HTTP.
/// </summary>
internal static class McpSurface
{
    private const string ServerName = "api-docs";
    private const string ServerVersion = "1.0.0";

    public static IMcpServerBuilder WithApiDocsSurface(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder
            .WithTools<DocsTools>()
            .WithListResourcesHandler(DocsResources.ListAsync)
            .WithListResourceTemplatesHandler(DocsResources.ListTemplatesAsync)
            .WithReadResourceHandler(DocsResources.ReadAsync);

        // Identity and instructions are read when a client initialises, after the index has been
        // built by the host: the platform name and version come from the corpus itself.
        builder.Services
            .AddOptions<McpServerOptions>()
            .Configure<IIndexSnapshotProvider>((options, snapshots) =>
            {
                options.ServerInfo = new ModelContextProtocol.Protocol.Implementation
                {
                    Name = ServerName,
                    Version = ServerVersion,
                };

                options.ServerInstructions = snapshots.HasSnapshot
                    ? ToolContracts.ServerInstructions(
                        snapshots.Current.Platform.Platform,
                        snapshots.Current.Platform.PlatformVersion)
                    : ToolContracts.ServerInstructions("internal", "unknown");
            });

        return builder;
    }
}
