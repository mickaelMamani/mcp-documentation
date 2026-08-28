using ApiDocs.Application;
using ApiDocs.Application.UseCases;
using FluentAssertions;

namespace ApiDocs.Tests;

/// <summary>Output contracts of ARCHITECTURE §5.2, which the client LLM is instructed to rely on.</summary>
[Collection(DocumentationCollection.Name)]
public sealed class ToolOutputTests(DocumentationFixture fixture)
{
    [Fact]
    public void List_domains_states_the_platform_then_one_line_per_domain()
    {
        var text = fixture.ListDomains.Execute();

        var lines = text.Split('\n');
        lines[0].Should().Be("Platform: Federer API v2.3.0 (docs generated 2026-08-28)");
        lines.Should().Contain("- volatility — Volatility surfaces: retrieval, plugging, calibration. (6 endpoints)");
        lines.Should().Contain("- folio — Folios (portfolios of positions): search, retrieval, lifecycle. (6 endpoints)");
    }

    [Fact]
    public void Search_endpoints_returns_a_numbered_list_and_the_next_step()
    {
        var result = fixture.SearchEndpoints.Execute("folio search by criteria");

        result.Text.Should().StartWith("1. SearchFolio — POST /folios/search — ");
        result.Text.Should().Contain("[domain: folio]");
        result.Text.Should().EndWith("Next: call get_endpoint(operationId) for details.");
    }

    [Fact]
    public void Search_endpoints_explains_itself_when_nothing_matches()
    {
        var result = fixture.SearchEndpoints.Execute("zzzz qqqq");

        result.Matches.Should().BeEmpty();
        result.Text.Should().Contain("No endpoint matched \"zzzz qqqq\"");
        result.Text.Should().Contain("The index is in English");
        result.Text.Should().Contain("Domains: volatility, folio.");
    }

    [Fact]
    public void Search_endpoints_honours_the_domain_filter()
    {
        var result = fixture.SearchEndpoints.Execute("surface", domain: "volatility");

        result.Matches.Should().NotBeEmpty();
        result.Matches.Should().OnlyContain(match => match.Endpoint.DomainId == "volatility");
    }

    [Fact]
    public void Search_endpoints_rejects_an_unknown_domain()
    {
        var result = fixture.SearchEndpoints.Execute("surface", domain: "pricing");

        result.Matches.Should().BeEmpty();
        result.Text.Should().Contain("Unknown domain \"pricing\"");
    }

    [Fact]
    public void Search_endpoints_never_proposes_a_deprecated_endpoint()
    {
        var result = fixture.SearchEndpoints.Execute("list folios", limit: SearchEndpointsUseCase.MaxLimit);

        result.Matches.Should().NotContain(match => match.Endpoint.Operation.Value == "ListFolios");
    }

    [Fact]
    public void Search_endpoints_caps_the_limit()
    {
        var result = fixture.SearchEndpoints.Execute("folio surface volatility position", limit: 100);

        result.Matches.Should().HaveCountLessThanOrEqualTo(SearchEndpointsUseCase.MaxLimit);
    }

    [Fact]
    public void Get_endpoint_starts_with_the_fixed_header()
    {
        var result = fixture.GetEndpoint.Execute("GetFolioById");

        result.Found.Should().BeTrue();
        result.Text.Should().StartWith("# GetFolioById — GET /folios/{folioId}");
        result.Text.Should().Contain("Domain: folio · API version: 2.3");
    }

    [Fact]
    public void Get_endpoint_uses_the_standard_section_order_without_a_query()
    {
        var text = fixture.GetEndpoint.Execute("Plug").Text;

        var order = new[] { "## Description", "## Parameters", "## Response", "## Example C#", "## Example Python", "## Notes" };
        var positions = order.Select(section => text.IndexOf(section, StringComparison.Ordinal)).ToArray();

        positions.Should().OnlyContain(position => position >= 0);
        positions.Should().BeInAscendingOrder();
    }

    [Fact]
    public void Get_endpoint_lists_the_related_endpoints()
    {
        var text = fixture.GetEndpoint.Execute("Plug").Text;

        text.Should().Contain("Related: GetSurface, DeletePlug, Recalibrate");
    }

    [Fact]
    public void Get_endpoint_returns_only_the_requested_language()
    {
        var text = fixture.GetEndpoint.Execute("Plug", language: "python").Text;

        text.Should().Contain("## Example Python");
        text.Should().NotContain("## Example C#");
    }

    [Fact]
    public void Get_endpoint_rejects_an_unknown_language()
    {
        var result = fixture.GetEndpoint.Execute("Plug", language: "rust");

        result.Found.Should().BeFalse();
        result.Text.Should().Contain("Unknown language \"rust\"");
    }

    [Fact]
    public void Get_endpoint_suggests_the_closest_names_for_an_unknown_operation_id()
    {
        var result = fixture.GetEndpoint.Execute("GetFolio");

        result.Found.Should().BeFalse();
        result.Text.Should().StartWith("Unknown operationId \"GetFolio\". Did you mean: GetFolioById, GetFolioPositions");
    }

    [Fact]
    public void Get_endpoint_puts_parameters_first_when_a_query_is_given()
    {
        var text = fixture.GetEndpoint.Execute("Plug", query: "which tenor should I send").Text;

        var parameters = text.IndexOf("## Parameters", StringComparison.Ordinal);
        var others = new[] { "## Description", "## Response", "## Notes" }
            .Select(section => text.IndexOf(section, StringComparison.Ordinal));

        parameters.Should().BeGreaterThan(0);
        others.Should().OnlyContain(position => position > parameters);
    }

    [Fact]
    public void Search_docs_returns_sections_with_their_breadcrumb()
    {
        var result = fixture.SearchDocs.Execute("authentication scopes token");

        result.Hits.Should().NotBeEmpty();
        result.Text.Should().Contain("### Federer API › Authentication");
    }

    [Fact]
    public void Search_docs_explains_itself_when_nothing_matches()
    {
        var result = fixture.SearchDocs.Execute("zzzz qqqq");

        result.Hits.Should().BeEmpty();
        result.Text.Should().Contain("No documentation section matched");
    }

    [Fact]
    public void Tool_descriptions_are_the_ones_of_the_design_document()
    {
        ToolContracts.ListDomains.Should().StartWith("List the API domains with their summary, endpoint count and the platform version.");
        ToolContracts.SearchEndpoints.Should().StartWith("Find candidate endpoints for a task. Call this before get_endpoint.");
        ToolContracts.GetEndpoint.Should().StartWith("Get the full documentation of one endpoint:");
        ToolContracts.SearchDocs.Should().StartWith("Full-text search across all documentation sections");
        ToolContracts.QueryParameter.Should().Contain("Translate the user's request; never pass it verbatim.");
    }

    [Fact]
    public void Server_instructions_carry_the_platform_version_and_the_workflow()
    {
        var instructions = ToolContracts.ServerInstructions("Federer API", "2.3.0");

        instructions.Should().StartWith("This server exposes the internal documentation of the Federer API (version 2.3.0).");
        instructions.Should().Contain("Users may ask in French");
        instructions.Should().Contain("(1) list_domains");
        instructions.Should().Contain("Never invent endpoints or");
    }
}
