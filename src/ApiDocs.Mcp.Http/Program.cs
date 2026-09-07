using ApiDocs.Application.Ports;
using ApiDocs.Infrastructure;
using ApiDocs.Infrastructure.Ingestion;
using ApiDocs.Mcp;
using ApiDocs.Mcp.Http;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.OpenApi;
using ModelContextProtocol.AspNetCore;
using Scalar.AspNetCore;
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

// OpenAPI description of the operational surface (/health, /admin/reindex), rendered by Scalar on
// /scalar (ADR #29). The MCP surface on /mcp is JSON-RPC and stays out of the document.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => options.SwaggerDoc("v1", new OpenApiInfo
{
    Title = "ApiDocs MCP — operational API",
    Version = "v1",
    Description = "Health and administration endpoints of the ApiDocs MCP production host. "
        + "The MCP surface itself is served on /mcp (JSON-RPC over Streamable HTTP, not described here).",
}));

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

// JSON-RPC, not REST: the MCP endpoints have no meaningful OpenAPI shape.
app.MapMcp(McpRoute).ExcludeFromDescription();

// Liveness and, above all, which revision of the documentation is actually being served: this is
// how an operator confirms a reindex or a restart picked up the new corpus.
app.MapGet("/health", (IIndexSnapshotProvider snapshots, IDocsRevision revision) =>
{
    if (!snapshots.HasSnapshot)
    {
        return Results.Json(new { status = "unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var snapshot = snapshots.Current;
    return Results.Json(new HealthResponse(
        Status: "ok",
        Platform: snapshot.Platform.Platform,
        PlatformVersion: snapshot.Platform.PlatformVersion,
        DocsRevision: revision.Commit,
        Files: snapshot.FileCount,
        Chunks: snapshot.Chunks.Length,
        Endpoints: snapshot.Endpoints.Length,
        IndexBuildMs: (long)snapshot.BuildDuration.TotalMilliseconds,
        ValidationIssues: snapshot.Issues.Length));
})
.WithTags("Operations")
.WithSummary("Snapshot served and documentation revision")
.WithDescription("Liveness probe and deployment check: docsRevision is the commit actually indexed.")
.Produces<HealthResponse>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status503ServiceUnavailable);

// Reload trigger (ADR #28): the documentation publication pipeline calls this after pushing a new
// corpus, instead of restarting the service. Same trust model as /mcp — no in-app auth, access is
// closed upstream (ADR #24). A failed rebuild keeps the previous snapshot and reports 500.
app.MapPost(ReindexEndpoint.Route, async (IIndexSnapshotProvider snapshots, IDocsRevision revision) =>
{
    var (statusCode, body) = await ReindexEndpoint.ExecuteAsync(snapshots, revision, logger);
    return Results.Json(body, statusCode: statusCode);
})
.WithTags("Operations")
.WithSummary("Reload the documentation corpus")
.WithDescription("Re-fetches the configured source, rebuilds the index and swaps the snapshot atomically. "
    + "A failure keeps the previous snapshot in service and answers 500. "
    + "Intended caller: the documentation publication pipeline, after a push.")
.Produces<ReindexResponse>(StatusCodes.Status200OK)
.Produces<ReindexResponse>(StatusCodes.Status500InternalServerError);

// The OpenAPI document and the Scalar reference that renders it. Scalar's assets ship inside the
// package: nothing is fetched from a CDN, consistent with the no-outbound rule (ADR #16, #29).
app.MapSwagger("/openapi/{documentName}.json");
app.MapScalarApiReference();

Log.EndpointReady(
    logger,
    McpRoute,
    report.Endpoints,
    app.Services.GetRequiredService<IDocsRevision>().Commit ?? "n/a");

await app.RunAsync();
return 0;
