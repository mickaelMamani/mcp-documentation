using System.Globalization;
using System.Text;
using ApiDocs.Application.UseCases;
using FluentAssertions;
using Xunit.Abstractions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ApiDocs.Tests;

public sealed class BenchmarkQuestion
{
    public string Id { get; set; } = string.Empty;

    public string Question { get; set; } = string.Empty;

    public List<string> Expected { get; set; } = [];

    public string? ExpectedChunk { get; set; }

    public List<string> ExpectedSections { get; set; } = [];

    public string Kind { get; set; } = string.Empty;

    /// <summary>A 1 to 3 token query, the shape a coding agent actually sends.</summary>
    public bool Terse { get; set; }
}

/// <summary>
/// The retrieval benchmark of ARCHITECTURE §9. It is a build gate: hit@3 ≥ 0.90 on
/// <c>search_endpoints</c>, and an average of at most 2 500 tokens returned per
/// <c>get_endpoint</c> call. MRR is reported so that a regression shows up before it crosses the
/// threshold.
/// </summary>
[Collection(DocumentationCollection.Name)]
public sealed class RetrievalBenchmarkTests(DocumentationFixture fixture, ITestOutputHelper output)
{
    private const double HitAtThreeThreshold = 0.90d;
    private const int TopK = 3;

    [Fact]
    public void The_question_set_covers_every_kind_and_both_languages()
    {
        var questions = LoadQuestions();

        questions.Should().HaveCountGreaterThanOrEqualTo(30);
        questions.Select(question => question.Kind).Distinct()
            .Should().BeEquivalentTo(["intent", "parameter", "example", "crosscutting"]);
        questions.Count(question => IsFrench(question.Question))
            .Should().BeGreaterThanOrEqualTo(8, "the benchmark must exercise the FR → EN reformulation");
        questions.Select(question => question.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Search_endpoints_returns_the_expected_endpoint_in_the_top_three()
    {
        var questions = LoadQuestions().Where(question => question.Expected.Count > 0).ToArray();
        var report = new StringBuilder();
        var reciprocalRanks = 0d;
        var hits = 0;
        var misses = new List<string>();

        report.AppendLine("| id | kind | rank | reformulated query | top 3 |");
        report.AppendLine("|---|---|---|---|---|");

        foreach (var question in questions)
        {
            var query = FrenchToEnglish.Reformulate(question.Question);
            var result = fixture.SearchEndpoints.Execute(query, limit: SearchEndpointsUseCase.MaxLimit);
            var rank = RankOfFirstExpected(result, question.Expected);

            if (rank > 0)
            {
                reciprocalRanks += 1d / rank;
            }

            if (rank is > 0 and <= TopK)
            {
                hits++;
            }
            else
            {
                misses.Add($"{question.Id} \"{question.Question}\" -> {query} (rank {(rank > 0 ? rank : -1)})");
            }

            report.Append(CultureInfo.InvariantCulture, $"| {question.Id} | {question.Kind} | ")
                .Append(rank > 0 ? rank.ToString(CultureInfo.InvariantCulture) : "-")
                .Append(" | ").Append(query).Append(" | ")
                .Append(string.Join(", ", result.Matches.Take(TopK).Select(match => match.Endpoint.Operation.Value)))
                .AppendLine(" |");
        }

        var hitAtThree = (double)hits / questions.Length;
        var mrr = reciprocalRanks / questions.Length;

        var terse = questions.Where(question => question.Terse).ToArray();
        var terseHits = terse.Count(question =>
        {
            var result = fixture.SearchEndpoints.Execute(
                FrenchToEnglish.Reformulate(question.Question),
                limit: SearchEndpointsUseCase.MaxLimit);
            var rank = RankOfFirstExpected(result, question.Expected);
            return rank is > 0 and <= TopK;
        });

        output.WriteLine(report.ToString());
        output.WriteLine($"search_endpoints on {questions.Length} questions: hit@3 = {hitAtThree:P1}, MRR = {mrr:F3}");
        if (terse.Length > 0)
        {
            output.WriteLine($"  of which terse (1-3 tokens): hit@3 = {(double)terseHits / terse.Length:P1} on {terse.Length} questions");
        }
        foreach (var miss in misses)
        {
            output.WriteLine("MISS " + miss);
        }

        hitAtThree.Should().BeGreaterThanOrEqualTo(
            HitAtThreeThreshold,
            "ARCHITECTURE §1 requires the right operationId in the top 3 on at least 90% of the question set");
    }

    [Fact]
    public void Parameter_questions_get_the_parameters_section_from_get_endpoint()
    {
        var questions = LoadQuestions()
            .Where(question => question.ExpectedChunk is { Length: > 0 })
            .ToArray();

        questions.Should().NotBeEmpty();

        foreach (var question in questions)
        {
            var query = FrenchToEnglish.Reformulate(question.Question);
            var operationId = question.Expected[0];
            var result = fixture.GetEndpoint.Execute(operationId, query);

            result.Found.Should().BeTrue();
            result.Text.Should().Contain(
                "## " + question.ExpectedChunk,
                $"{question.Id} asks about a parameter of {operationId}");

            var header = result.Text.IndexOf("# " + operationId, StringComparison.Ordinal);
            var section = result.Text.IndexOf("## " + question.ExpectedChunk, StringComparison.Ordinal);
            section.Should().BeGreaterThan(header, "the header always comes first");
        }
    }

    [Fact]
    public void Cross_cutting_questions_are_answered_by_search_docs()
    {
        var questions = LoadQuestions()
            .Where(question => question.ExpectedSections.Count > 0)
            .ToArray();

        questions.Should().NotBeEmpty();

        var misses = new List<string>();

        foreach (var question in questions)
        {
            var query = FrenchToEnglish.Reformulate(question.Question);
            var result = fixture.SearchDocs.Execute(query, limit: TopK);
            var breadcrumbs = result.Hits.Take(TopK)
                .Select(hit => hit.Chunk.Breadcrumb.ToString())
                .ToArray();

            output.WriteLine($"{question.Id} \"{query}\" -> {string.Join(" | ", breadcrumbs)}");

            var matched = question.ExpectedSections.Any(expected =>
                breadcrumbs.Any(breadcrumb => breadcrumb.Contains(expected, StringComparison.Ordinal)));

            if (!matched)
            {
                misses.Add($"{question.Id}: expected one of [{string.Join(", ", question.ExpectedSections)}]");
            }
        }

        misses.Should().BeEmpty();
    }

    [Fact]
    public void Get_endpoint_stays_within_its_token_budget()
    {
        var totals = new List<int>();

        foreach (var endpoint in fixture.Snapshot.Endpoints)
        {
            var operationId = endpoint.Operation.Value;

            var standard = fixture.GetEndpoint.Execute(operationId);
            var withQuery = fixture.GetEndpoint.Execute(operationId, "parameters and response");

            foreach (var result in new[] { standard, withQuery })
            {
                result.Found.Should().BeTrue();
                var tokens = fixture.TokenCounter.Count(result.Text);
                tokens.Should().BeLessThanOrEqualTo(
                    2500,
                    $"get_endpoint({operationId}) must respect the 2 500 token budget of ARCHITECTURE §4.5");
                totals.Add(tokens);
            }
        }

        var average = totals.Average();
        output.WriteLine($"get_endpoint over {totals.Count} calls: average {average:F0} tokens, max {totals.Max()}");

        average.Should().BeLessThanOrEqualTo(2500);
    }

    [Fact]
    public void Search_endpoints_and_search_docs_stay_within_their_budgets()
    {
        foreach (var question in LoadQuestions())
        {
            var query = FrenchToEnglish.Reformulate(question.Question);

            var endpoints = fixture.SearchEndpoints.Execute(query, limit: SearchEndpointsUseCase.MaxLimit);
            fixture.TokenCounter.Count(endpoints.Text).Should().BeLessThanOrEqualTo(500);

            var docs = fixture.SearchDocs.Execute(query, limit: SearchDocsUseCase.MaxLimit);
            fixture.TokenCounter.Count(docs.Text).Should().BeLessThanOrEqualTo(1700);
        }
    }

    private static int RankOfFirstExpected(SearchEndpointsResult result, List<string> expected)
    {
        for (var i = 0; i < result.Matches.Count; i++)
        {
            if (expected.Contains(result.Matches[i].Endpoint.Operation.Value, StringComparer.Ordinal))
            {
                return i + 1;
            }
        }

        return 0;
    }

    private static bool IsFrench(string question) =>
        question.Any(c => "éèêàçûôîùï".Contains(c, StringComparison.Ordinal))
        || question.StartsWith("Comment", StringComparison.Ordinal)
        || question.StartsWith("quel", StringComparison.OrdinalIgnoreCase);

    private List<BenchmarkQuestion> LoadQuestions()
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var yaml = File.ReadAllText(fixture.BenchmarkFile);
        return deserializer.Deserialize<List<BenchmarkQuestion>>(yaml) ?? [];
    }
}
