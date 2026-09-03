using ApiDocs.Application.Ports;
using ApiDocs.Infrastructure;
using ApiDocs.Infrastructure.Ingestion;
using ApiDocs.Mcp;
using ApiDocs.Mcp.Http;
using Microsoft.Extensions.Hosting.WindowsServices;
using ModelContextProtocol.AspNetCore;
using Serilog;
using Log = ApiDocs.Mcp.Http.Log;

const string McpRoute = "/mcp";

// Under the Windows Service Control Manager the working directory is System32, so the content root
// must be the binaries folder for appsettings.json to be found; UseWindowsService reports start and
// stop to the SCM. Both are no-ops outside a Windows service (ADR #26).
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : default,
});
// Cleared before UseWindowsService so the Event Log provider it adds under the SCM survives;
// without clearing, the default console provider would duplicate every line Serilog writes.
builder.Logging.ClearProviders();
builder.Host.UseWindowsService(options => options.ServiceName = "ApiDocs MCP");

// The console sink lives in code, not in configuration: the published copy is run from the repo
// root (ADR #19), where appsettings.json — and any sink declared in it — is out of content root.
// Levels stay in the "Serilog" section of appsettings. writeToProviders keeps forwarding to the
// SCM's Event Log provider; it is the only one left.
builder.Services.AddSerilog(
    (services, loggerConfiguration) => loggerConfiguration
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .WriteTo.Console(),
    writeToProviders: true);

// The corpus is centralised: in production the server checks it out from Git itself (ADR #23).
builder.Services.AddApiDocumentation(builder.Configuration);

builder.Services
    .AddMcpServer()
    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
    .WithApiDocsSurface();

var app = builder.Build();
var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ApiDocs.Mcp.Http");

// The documentation is fetched and indexed before the first request is served: a client never sees
// a half-built server, and a bad configuration fails the deployment instead of the first query.
var report = await app.Services.GetRequiredService<IndexSnapshotProvider>().RebuildAsync();
if (!report.Succeeded)
{
    Log.IndexUnavailable(logger, report.Error ?? "unknown error");
    return 1;
}

app.MapMcp(McpRoute);

// Liveness and, above all, which revision of the documentation is actually being served: the index
// is built once at startup, so this is how an operator confirms a restart picked up the new corpus.
app.MapGet("/health", (IIndexSnapshotProvider snapshots, IDocsRevision revision) =>
{
    if (!snapshots.HasSnapshot)
    {
        return Results.Json(new { status = "unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var snapshot = snapshots.Current;
    return Results.Json(new
    {
        status = "ok",
        platform = snapshot.Platform.Platform,
        platformVersion = snapshot.Platform.PlatformVersion,
        docsRevision = revision.Commit,
        files = snapshot.FileCount,
        chunks = snapshot.Chunks.Length,
        endpoints = snapshot.Endpoints.Length,
        indexBuildMs = (long)snapshot.BuildDuration.TotalMilliseconds,
        validationIssues = snapshot.Issues.Length,
    });
});

// Reload trigger (ADR #28): the documentation publication pipeline calls this after pushing a new
// corpus, instead of restarting the service. Same trust model as /mcp — no in-app auth, access is
// closed upstream (ADR #24). A failed rebuild keeps the previous snapshot and reports 500.
app.MapPost(ReindexEndpoint.Route, async (IIndexSnapshotProvider snapshots, IDocsRevision revision) =>
{
    var (statusCode, body) = await ReindexEndpoint.ExecuteAsync(snapshots, revision, logger);
    return Results.Json(body, statusCode: statusCode);
});

Log.EndpointReady(
    logger,
    McpRoute,
    report.Endpoints,
    app.Services.GetRequiredService<IDocsRevision>().Commit ?? "n/a");

await app.RunAsync();
return 0;
