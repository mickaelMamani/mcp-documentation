using ApiDocs.Application.Ports;
using ApiDocs.Infrastructure;
using ApiDocs.Infrastructure.Ingestion;
using ApiDocs.Mcp;
using ModelContextProtocol.AspNetCore;
using Log = ApiDocs.Mcp.Http.Log;

const string McpRoute = "/mcp";

var builder = WebApplication.CreateBuilder(args);

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

Log.EndpointReady(
    logger,
    McpRoute,
    report.Endpoints,
    app.Services.GetRequiredService<IDocsRevision>().Commit ?? "n/a");

await app.RunAsync();
return 0;
