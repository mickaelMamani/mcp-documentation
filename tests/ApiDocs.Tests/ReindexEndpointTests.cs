using ApiDocs.Application;
using ApiDocs.Application.Ports;
using ApiDocs.Domain;
using ApiDocs.Infrastructure.Ingestion;
using ApiDocs.Mcp.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApiDocs.Tests;

/// <summary>
/// ADR #28: POST /admin/reindex reports the validation report and statistics of the rebuild, and a
/// failure answers 500 while the previous snapshot — and its revision — stay in service.
/// </summary>
public sealed class ReindexEndpointTests
{
    [Fact]
    public async Task Reports_the_statistics_and_validation_report_of_a_successful_rebuild()
    {
        var report = new RebuildReport(
            Succeeded: true,
            Files: 12,
            Chunks: 87,
            Endpoints: 9,
            ElapsedMilliseconds: 431,
            Issues: [new ValidationIssue("volatility/GetSurface.md", "H2-ORDER", "Sections out of order", ValidationSeverity.Warning)]);

        var result = await ReindexEndpoint.ExecuteAsync(
            new StubSnapshotProvider(report),
            new StubRevision("abc123"),
            NullLogger.Instance);

        result.StatusCode.Should().Be(200);
        result.Body.Status.Should().Be("ok");
        result.Body.DocsRevision.Should().Be("abc123");
        result.Body.Files.Should().Be(12);
        result.Body.Chunks.Should().Be(87);
        result.Body.Endpoints.Should().Be(9);
        result.Body.RebuildMs.Should().Be(431);
        result.Body.ValidationIssues.Should().ContainSingle()
            .Which.Should().Contain("volatility/GetSurface.md").And.Contain("H2-ORDER");
        result.Body.Error.Should().BeNull();
    }

    [Fact]
    public async Task Answers_500_and_the_revision_still_served_when_the_rebuild_fails()
    {
        var report = new RebuildReport(
            Succeeded: false,
            Files: 0,
            Chunks: 0,
            Endpoints: 0,
            ElapsedMilliseconds: 52,
            Issues: [],
            Error: "No manifest.json found");

        var result = await ReindexEndpoint.ExecuteAsync(
            new StubSnapshotProvider(report),
            new StubRevision("abc123"),
            NullLogger.Instance);

        result.StatusCode.Should().Be(500);
        result.Body.Status.Should().Be("failed");
        result.Body.Error.Should().Be("No manifest.json found");
        result.Body.DocsRevision.Should().Be("abc123", "the previous snapshot stays in service");
    }

    private sealed class StubSnapshotProvider(RebuildReport report) : IIndexSnapshotProvider
    {
        public IndexSnapshot Current => throw new InvalidOperationException("Not needed by the endpoint.");

        public bool HasSnapshot => false;

        public Task<RebuildReport> RebuildAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(report);
    }

    private sealed class StubRevision(string? commit) : IDocsRevision
    {
        public string? Commit => commit;
    }
}
