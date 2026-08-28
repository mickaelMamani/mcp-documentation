using ApiDocs.Domain;
using ApiDocs.Infrastructure.Ingestion;
using FluentAssertions;

namespace ApiDocs.Tests;

/// <summary>
/// DOC-FORMAT §7 applied to the corpus that ships with the server, plus the rules themselves
/// exercised on deliberately broken files.
/// </summary>
[Collection(DocumentationCollection.Name)]
public sealed class ValidationTests(DocumentationFixture fixture)
{
    [Fact]
    public void The_shipped_corpus_passes_every_validation_rule()
    {
        var errors = fixture.Report.Issues
            .Where(issue => issue.Severity == ValidationSeverity.Error)
            .Select(issue => issue.ToString())
            .ToArray();

        errors.Should().BeEmpty();
    }

    [Fact]
    public void The_shipped_corpus_produces_no_warning_either()
    {
        fixture.Report.Issues.Should().BeEmpty();
        fixture.Report.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Every_manifest_endpoint_is_indexed()
    {
        fixture.Snapshot.Endpoints.Should().HaveCount(12);
        fixture.Snapshot.Domains.Should().HaveCount(2);
    }

    [Fact]
    public void A_renamed_section_is_rejected()
    {
        var issues = ValidateEndpoint(EndpointFile.Replace("## Notes", "## Remarks", StringComparison.Ordinal));

        issues.Should().Contain(issue => issue.Rule == DocumentValidator.Rules.SectionTitles);
    }

    [Fact]
    public void An_example_section_without_a_code_block_is_rejected()
    {
        var broken = EndpointFile.Replace("```python\nprint(1)\n```", "no code here", StringComparison.Ordinal);

        var issues = ValidateEndpoint(broken);

        issues.Should().Contain(issue => issue.Rule == DocumentValidator.Rules.ExampleCodeBlock);
    }

    [Fact]
    public void A_parameters_section_that_is_not_the_expected_table_is_rejected()
    {
        var broken = EndpointFile.Replace(
            "| Name | In | Type | Required | Description |\n|---|---|---|---|---|\n| id | path | string | yes | The id. |",
            "Just prose about the parameters.",
            StringComparison.Ordinal);

        var issues = ValidateEndpoint(broken);

        issues.Should().Contain(issue => issue.Rule == DocumentValidator.Rules.ParametersTable);
    }

    [Fact]
    public void A_summary_longer_than_160_characters_is_rejected()
    {
        var broken = EndpointFile.Replace(
            "summary: Do a thing.",
            "summary: " + new string('x', DocumentValidator.MaxSummaryLength + 1),
            StringComparison.Ordinal);

        var issues = ValidateEndpoint(broken);

        issues.Should().Contain(issue => issue.Rule == DocumentValidator.Rules.SummaryLength);
    }

    [Fact]
    public void Keywords_with_punctuation_are_rejected()
    {
        var broken = EndpointFile.Replace(
            "keywords: thing things do",
            "keywords: thing, things, do",
            StringComparison.Ordinal);

        var issues = ValidateEndpoint(broken);

        issues.Should().Contain(issue => issue.Rule == DocumentValidator.Rules.KeywordsPunctuation);
    }

    [Fact]
    public void A_related_entry_pointing_nowhere_is_rejected()
    {
        var broken = EndpointFile.Replace(
            "related: []",
            "related: [DoesNotExist]",
            StringComparison.Ordinal);

        var issues = ValidateEndpoint(broken);

        issues.Should().Contain(issue => issue.Rule == DocumentValidator.Rules.RelatedExists);
    }

    [Fact]
    public void A_front_matter_that_contradicts_the_manifest_is_rejected()
    {
        var broken = EndpointFile.Replace("route: /things/{id}", "route: /other", StringComparison.Ordinal);

        var issues = ValidateEndpoint(broken);

        issues.Should().Contain(issue => issue.Rule == DocumentValidator.Rules.ManifestConsistency);
    }

    [Fact]
    public void An_unsupported_format_version_is_rejected()
    {
        var broken = EndpointFile.Replace("formatVersion: \"1.0\"", "formatVersion: \"9.9\"", StringComparison.Ordinal);

        var issues = ValidateEndpoint(broken);

        issues.Should().Contain(issue => issue.Rule == DocumentValidator.Rules.FormatVersion);
    }

    [Fact]
    public void The_reference_file_used_by_these_tests_is_itself_valid()
    {
        ValidateEndpoint(EndpointFile).Should().BeEmpty();
    }

    private static List<ValidationIssue> ValidateEndpoint(string fileContent)
    {
        FrontMatterReader.TrySplit(fileContent, out var yaml, out var body, out _).Should().BeTrue();

        var frontMatter = FrontMatterReader.Parse(yaml);
        var sections = MarkdownSections.Split(body, bodyStartLine: 1);
        var manifestEntry = new ManifestEndpoint
        {
            OperationId = "DoThing",
            Method = "GET",
            Route = "/things/{id}",
            Summary = "Do a thing.",
            File = "thing/do-thing.md",
        };

        var issues = new List<ValidationIssue>();
        new DocumentValidator().ValidateEndpoint(
            "thing/do-thing.md",
            frontMatter,
            sections,
            manifestEntry,
            new HashSet<string>(StringComparer.Ordinal) { "DoThing" },
            issues);

        return issues;
    }

    /// <summary>
    /// Normalised to LF. The fixture is a raw string literal, so it carries whatever line endings
    /// the checkout produced, while every search string below uses "\n" escapes, which are always
    /// LF. .gitattributes pins C# sources to LF; this keeps the test honest if that ever relaxes.
    /// </summary>
    private static readonly string EndpointFile =
        RawEndpointFile.Replace("\r\n", "\n", StringComparison.Ordinal);

    private const string RawEndpointFile =
        """
        ---
        formatVersion: "1.0"
        domain: Thing
        operationId: DoThing
        method: GET
        route: /things/{id}
        version: "1.0"
        summary: Do a thing.
        tags: [thing]
        keywords: thing things do
        aliases: []
        related: []
        deprecated: false
        ---

        ## Description
        Does a thing.

        ## Parameters
        | Name | In | Type | Required | Description |
        |---|---|---|---|---|
        | id | path | string | yes | The id. |

        ## Response
        | Status | Meaning |
        |---|---|
        | 200 | Done. |

        ## Example C#
        ```csharp
        Console.WriteLine(1);
        ```

        ## Example Python
        ```python
        print(1)
        ```

        ## Notes
        Nothing special.
        """;
}
