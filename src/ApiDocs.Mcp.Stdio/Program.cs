using ApiDocs.Infrastructure;
using ApiDocs.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// stdout is the JSON-RPC channel: every log line goes to stderr (ARCHITECTURE §7).
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Information);

var docsPath = builder.Configuration["docs"] ?? "docs";

builder.Services.AddApiDocumentation(docsPath);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithApiDocsSurface();

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
