using ApiDocs.Domain;
using FluentAssertions;

namespace ApiDocs.Tests;

/// <summary>Chunking rules of ARCHITECTURE §4.1 and §6, checked on the shipped corpus.</summary>
[Collection(DocumentationCollection.Name)]
public sealed class ChunkerTests(DocumentationFixture fixture)
{
    [Fact]
    public void An_endpoint_file_produces_one_chunk_per_section()
    {
        var chunks = fixture.Snapshot.ChunksOf("Plug");

        chunks.Select(chunk => chunk.Section).Should().BeEquivalentTo(
            ["Description", "Parameters", "Response", "Example C#", "Example Python", "Notes"]);
    }

    [Fact]
    public void Example_chunks_carry_their_language()
    {
        var chunks = fixture.Snapshot.ChunksOf("Plug");

        chunks.Single(chunk => chunk.Section == "Example C#").Language.Should().Be(CodeLanguage.CSharp);
        chunks.Single(chunk => chunk.Section == "Example Python").Language.Should().Be(CodeLanguage.Python);
        chunks.Single(chunk => chunk.Section == "Parameters").Language.Should().Be(CodeLanguage.None);
    }

    [Fact]
    public void Section_headings_map_to_the_expected_kinds()
    {
        var chunks = fixture.Snapshot.ChunksOf("Plug");

        chunks.Single(chunk => chunk.Section == "Description").Kind.Should().Be(SectionKind.Description);
        chunks.Single(chunk => chunk.Section == "Parameters").Kind.Should().Be(SectionKind.Parameters);
        chunks.Single(chunk => chunk.Section == "Response").Kind.Should().Be(SectionKind.Response);
        chunks.Single(chunk => chunk.Section == "Notes").Kind.Should().Be(SectionKind.Notes);
        chunks.Where(chunk => chunk.Section.StartsWith("Example", StringComparison.Ordinal))
            .Should().OnlyContain(chunk => chunk.Kind == SectionKind.Example);
    }

    [Fact]
    public void Domain_files_produce_the_five_domain_kinds()
    {
        var kinds = fixture.Snapshot.Chunks
            .Where(chunk => chunk.DomainId == "folio" && chunk.Operation is null)
            .Select(chunk => chunk.Kind)
            .ToArray();

        kinds.Should().BeEquivalentTo([
            SectionKind.DomainOverview,
            SectionKind.DomainUseCases,
            SectionKind.DomainWorkflow,
            SectionKind.DomainAuth,
            SectionKind.DomainErrors,
        ]);
    }

    [Fact]
    public void The_platform_file_produces_platform_chunks()
    {
        fixture.Snapshot.Chunks
            .Where(chunk => chunk.Kind == SectionKind.Platform)
            .Should().HaveCountGreaterThan(5)
            .And.OnlyContain(chunk => chunk.DomainId.Length == 0);
    }

    [Fact]
    public void Summary_and_keywords_are_duplicated_on_every_chunk_of_an_endpoint()
    {
        var chunks = fixture.Snapshot.ChunksOf("Plug");
        var pythonExample = chunks.Single(chunk => chunk.Section == "Example Python");

        pythonExample.Summary.Should().Be("Apply a plug (manual bump) to an existing volatility surface.");
        pythonExample.Keywords.Should().Contain("bumper").And.Contain("surfaceId");
    }

    [Fact]
    public void Breadcrumbs_follow_the_domain_operation_section_shape()
    {
        var parameters = fixture.Snapshot.ChunksOf("GetFolioById").Single(chunk => chunk.Section == "Parameters");

        parameters.Breadcrumb.ToString().Should().Be("Folio › GetFolioById › Parameters");
    }

    [Fact]
    public void Chunk_ids_are_unique_and_stable()
    {
        var ids = fixture.Snapshot.Chunks.Select(chunk => chunk.Id.Value).ToArray();

        ids.Should().OnlyHaveUniqueItems();
        ids.Should().OnlyContain(id => id.Length == 16);
    }

    [Fact]
    public void Endpoint_references_become_resource_links_outside_code_blocks()
    {
        var description = fixture.Snapshot.ChunksOf("Plug").Single(chunk => chunk.Section == "Description");

        description.Body.Should().Contain("[`GetSurface`](endpoint://GetSurface)");
    }

    [Fact]
    public void Code_examples_are_never_rewritten()
    {
        var example = fixture.Snapshot.ChunksOf("Plug").Single(chunk => chunk.Section == "Example Python");

        example.Body.Should().NotContain("endpoint://");
        example.Body.Should().Contain("requests.post");
    }

    [Fact]
    public void Every_chunk_carries_a_token_count()
    {
        fixture.Snapshot.Chunks.Should().OnlyContain(chunk => chunk.TokenCount > 0);
    }

    [Fact]
    public void The_use_cases_table_is_parsed_into_rows()
    {
        fixture.Snapshot.TryGetDomain("folio", out var folio).Should().BeTrue();

        folio.UseCases.Should().HaveCountGreaterThan(8);
        folio.UseCases.Should().Contain(row =>
            row.Intent.Contains("already know its id", StringComparison.Ordinal)
            && row.Operations.Any(operation => operation.Value == "GetFolioById"));
    }
}
