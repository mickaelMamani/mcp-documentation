using ApiDocs.Application;
using ApiDocs.Application.Ports;
using ApiDocs.Infrastructure;
using ApiDocs.Mcp.Stdio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

// stdout is the JSON-RPC channel: every log line goes to stderr (ARCHITECTURE §7).
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Information);

var docsPath = builder.Configuration["docs"] ?? "docs";

builder.Services.AddApiDocumentation(docsPath);

builder.Services
    .AddMcpServer(options => options.ServerInfo = new ModelContextProtocol.Protocol.Implementation
    {
        Name = "api-docs",
        Version = "1.0.0",
    })
    .WithStdioServerTransport()
    .WithTools<DocsTools>()
    .WithListResourcesHandler(DocsResources.ListAsync)
    .WithListResourceTemplatesHandler(DocsResources.ListTemplatesAsync)
    .WithReadResourceHandler(DocsResources.ReadAsync);

// ServerInstructions are read when the client initialises, after the index has been built below.
builder.Services.AddOptions<McpServerOptions>().Configure<IIndexSnapshotProvider>((options, snapshots) =>
{
    options.ServerInstructions = snapshots.HasSnapshot
        ? ToolContracts.ServerInstructions(
            snapshots.Current.Platform.Platform,
            snapshots.Current.Platform.PlatformVersion)
        : ToolContracts.ServerInstructions("internal", "unknown");
});

var host = builder.Build();

// The index is built before the transport starts: a client never sees a half-built server.
var report = await host.Services.GetRequiredService<IndexSnapshotProvider>().RebuildAsync();
if (!report.Succeeded)
{
    await Console.Error.WriteLineAsync($"Failed to build the documentation index from \"{docsPath}\": {report.Error}");
    return 1;
}

await host.RunAsync();
return 0;
