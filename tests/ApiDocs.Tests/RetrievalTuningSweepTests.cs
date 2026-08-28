using System.Globalization;
using System.Text;
using ApiDocs.Application;
using ApiDocs.Application.Retrieval;
using ApiDocs.Application.UseCases;
using ApiDocs.Application.Ports;
using ApiDocs.Infrastructure.Ingestion;
using ApiDocs.Infrastructure.Retrieval;
using ApiDocs.Infrastructure.Tokenization;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ApiDocs.Tests;

internal sealed record SweepVariant(string Name, LuceneOptions Lucene, RankingOptions Ranking);

internal sealed record SweepScore(int Questions, double HitAtOne, double HitAtThree, double Mrr)
{
    /// <summary>hit@1 is the metric that exposes a wrong winner; hit@3 hides it behind a runner-up.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"@1 {HitAtOne:P0} · @3 {HitAtThree:P0} · MRR {Mrr:F3}");
}

/// <summary>
/// ADR #7 in practice: boosts and analyzers are prompt code, so they are measured, not argued
/// about. The sweep builds the document set once, then one Lucene index per configuration, and
/// scores every configuration against the authored question set and the held-out terse set.
/// </summary>
[Collection(DocumentationCollection.Name)]
public sealed class RetrievalTuningSweepTests(DocumentationFixture fixture, ITestOutputHelper output)
{
    private const int TopK = 3;

    /// <summary>The queries that exposed the misranking on the published server.</summary>
    private static readonly string[] Probes =
        ["list folios", "folio list", "folios", "list portfolios", "list surfaces", "surfaces", "plug"];

    [Fact]
    public async Task Sweep_measures_every_configuration_and_reports_the_probe_queries()
    {
        var authored = LoadAuthored();
        var terse = authored.Where(question => question.Terse).ToArray();
        var heldOut = LoadHeldOut();

        output.WriteLine($"authored: {authored.Count} ({terse.Length} terse) · held-out: {heldOut.Count}");
        if (heldOut.Count == 0)
        {
            output.WriteLine("WARNING no held-out set on disk: the winner is only defended by the authored questions.");
        }

        var tokenCounter = new TiktokenTokenCounter();
        var source = new MarkdownRepositorySource(
            Options.Create(new DocsOptions { Path = Path.Combine(fixture.RepositoryRoot, "docs") }),
            new MarkdigChunker(tokenCounter),
            NullLogger<MarkdownRepositorySource>.Instance);

        var set = await source.LoadAsync();
        var scored = new List<(SweepVariant Variant, SweepScore All, SweepScore Terse, SweepScore Held)>();

        void Run(string title, IEnumerable<SweepVariant> variants)
        {
            var report = new StringBuilder();
            report.AppendLine(title);
            report.AppendLine("| configuration | authored | terse | held-out |");
            report.AppendLine("|---|---|---|---|");

            foreach (var variant in variants)
            {
                using var snapshot = IndexSnapshot.Create(
                    set, new LuceneLexicalIndexFactory(variant.Lucene), TimeSpan.Zero);
                var useCase = new SearchEndpointsUseCase(
                    new FixedSnapshotProvider(snapshot), tokenCounter, variant.Ranking);

                var entry = (variant, Score(useCase, authored), Score(useCase, terse), Score(useCase, heldOut));
                scored.Add(entry);
                report.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"| {variant.Name} | {entry.Item2} | {entry.Item3} | {entry.Item4} |");
            }

            output.WriteLine(report.ToString());
        }

        Run("### Grid A — which levers matter", LeverCrossing());
        Run("### Grid B — weight refinement around the winner", WeightRefinement());

        var baseline = scored[0];

        // Rank on hit@1: putting the right endpoint second still costs the model a wrong turn.
        // Held-out counts double — it is the only set the tuning could not have been fitted to.
        static double Quality((SweepVariant Variant, SweepScore All, SweepScore Terse, SweepScore Held) s) =>
            s.All.HitAtOne + s.Terse.HitAtOne + (2 * s.Held.HitAtOne);

        var ceiling = scored.Max(Quality);
        _ = heldOut.Count;

        // Anti-overfit rule: the metric plateaus well before the edge of the grid, so among every
        // configuration within one held-out question of the ceiling, take the one that distorts BM25
        // least. A bigger multiplier that buys nothing measurable is a bigger bet on this corpus.
        static double Distortion((SweepVariant Variant, SweepScore All, SweepScore Terse, SweepScore Held) s) =>
            s.Variant.Ranking.CoverageWeight + s.Variant.Ranking.DomainAffinityWeight;

        // No tolerance: a configuration that drops even one question is dropping a real query. Among
        // those that reach the ceiling, take the least distorting one.
        var best = scored
            .Where(s => Quality(s) >= ceiling)
            .OrderBy(Distortion)
            .ThenByDescending(s => s.All.Mrr + s.Terse.Mrr + (2 * s.Held.Mrr))
            .First();

        output.WriteLine($"BASELINE {baseline.Variant.Name}\n  authored {baseline.All} · terse {baseline.Terse} · held-out {baseline.Held}");
        output.WriteLine($"BEST     {best.Variant.Name}\n  authored {best.All} · terse {best.Terse} · held-out {best.Held}");

        Probe(set, tokenCounter, baseline.Variant, "BASELINE");
        Probe(set, tokenCounter, best.Variant, "BEST");
        Misses(set, tokenCounter, best.Variant, authored, "authored");
        Misses(set, tokenCounter, best.Variant, heldOut, "held-out");

        best.All.HitAtOne.Should().BeGreaterThanOrEqualTo(
            baseline.All.HitAtOne,
            "the sweep must never select a configuration worse than the one already shipped");
    }

    private void Probe(DocumentSet set, TiktokenTokenCounter tokenCounter, SweepVariant variant, string label)
    {
        using var snapshot = IndexSnapshot.Create(set, new LuceneLexicalIndexFactory(variant.Lucene), TimeSpan.Zero);
        var useCase = new SearchEndpointsUseCase(new FixedSnapshotProvider(snapshot), tokenCounter, variant.Ranking);

        output.WriteLine($"probe queries under {label} ({variant.Name}):");
        foreach (var probe in Probes)
        {
            var top = useCase.Execute(probe, limit: TopK).Matches
                .Select(match => match.Endpoint.Operation.Value);
            output.WriteLine($"  {probe,-18} -> {string.Join(", ", top)}");
        }
    }

    /// <summary>What the chosen configuration still gets wrong, named rather than averaged away.</summary>
    private void Misses(
        DocumentSet set,
        TiktokenTokenCounter tokenCounter,
        SweepVariant variant,
        IReadOnlyList<BenchmarkQuestion> questions,
        string label)
    {
        if (questions.Count == 0)
        {
            return;
        }

        using var snapshot = IndexSnapshot.Create(set, new LuceneLexicalIndexFactory(variant.Lucene), TimeSpan.Zero);
        var useCase = new SearchEndpointsUseCase(new FixedSnapshotProvider(snapshot), tokenCounter, variant.Ranking);

        output.WriteLine($"remaining misses on {label} under the chosen configuration:");
        var any = false;

        foreach (var question in questions)
        {
            var query = FrenchToEnglish.Reformulate(question.Question);
            var matches = useCase.Execute(query, limit: SearchEndpointsUseCase.MaxLimit).Matches;

            var rank = 0;
            for (var i = 0; i < matches.Count; i++)
            {
                if (question.Expected.Contains(matches[i].Endpoint.Operation.Value, StringComparer.Ordinal))
                {
                    rank = i + 1;
                    break;
                }
            }

            if (rank == 1)
            {
                continue;
            }

            any = true;
            var top = string.Join(", ", matches.Take(TopK).Select(match => match.Endpoint.Operation.Value));
            output.WriteLine(
                $"  {question.Id} \"{query}\" want [{string.Join(", ", question.Expected)}] rank {(rank > 0 ? rank : -1)} · got {top}");
        }

        if (!any)
        {
            output.WriteLine("  none");
        }
    }

    /// <summary>The four levers of the misranking, crossed. The first entry is the shipped configuration.</summary>
    private static IEnumerable<SweepVariant> LeverCrossing()
    {
        foreach (var fold in new[] { false, true })
        {
            foreach (var b in new[] { 0.75f, 0.35f })
            {
                foreach (var cover in new[] { 0d, 0.75d })
                {
                    foreach (var affinity in new[] { 0d, 0.75d })
                    {
                        yield return Variant(fold, b, cover, affinity);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Weight refinement past the first winner, with plural folding on AND off: the first grid put
    /// the optimum on its own edge, and folding alone measured worse than the baseline, so both the
    /// edge and that lever's real contribution have to be checked rather than assumed.
    /// </summary>
    private static IEnumerable<SweepVariant> WeightRefinement()
    {
        foreach (var fold in new[] { false, true })
        {
            foreach (var cover in new[] { 1d, 1.5d, 2d, 3d })
            {
                foreach (var affinity in new[] { 1d, 1.5d, 2d, 3d })
                {
                    yield return Variant(fold, b: 0.75f, cover, affinity);
                }
            }
        }
    }

    private static SweepVariant Variant(bool foldPlurals, float b, double coverage, double affinity)
    {
        var lucene = new LuceneOptions { FoldPlurals = foldPlurals, Bm25B = b };
        var ranking = new RankingOptions { CoverageWeight = coverage, DomainAffinityWeight = affinity };
        return new SweepVariant($"{lucene} {ranking}", lucene, ranking);
    }

    private static SweepScore Score(SearchEndpointsUseCase useCase, IReadOnlyList<BenchmarkQuestion> questions)
    {
        if (questions.Count == 0)
        {
            return new SweepScore(0, 0d, 0d, 0d);
        }

        var firsts = 0;
        var hits = 0;
        var reciprocal = 0d;

        foreach (var question in questions)
        {
            var query = FrenchToEnglish.Reformulate(question.Question);
            var result = useCase.Execute(query, limit: SearchEndpointsUseCase.MaxLimit);

            var rank = 0;
            for (var i = 0; i < result.Matches.Count; i++)
            {
                if (question.Expected.Contains(result.Matches[i].Endpoint.Operation.Value, StringComparer.Ordinal))
                {
                    rank = i + 1;
                    break;
                }
            }

            if (rank == 0)
            {
                continue;
            }

            reciprocal += 1d / rank;
            if (rank == 1)
            {
                firsts++;
            }

            if (rank <= TopK)
            {
                hits++;
            }
        }

        return new SweepScore(
            questions.Count,
            (double)firsts / questions.Count,
            (double)hits / questions.Count,
            reciprocal / questions.Count);
    }

    private List<BenchmarkQuestion> LoadAuthored() =>
        Deserialize(File.ReadAllText(fixture.BenchmarkFile))
            .Where(question => question.Expected.Count > 0)
            .ToList();

    /// <summary>
    /// Queries written by agents that only read <c>docs/</c> and never saw the fix, so a
    /// configuration cannot be tuned into looking good on them.
    /// </summary>
    private static List<BenchmarkQuestion> LoadHeldOut()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "benchmark", "heldout-terse.yaml");
        return File.Exists(path) ? Deserialize(File.ReadAllText(path)) : [];
    }

    private static List<BenchmarkQuestion> Deserialize(string yaml) =>
        new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build()
            .Deserialize<List<BenchmarkQuestion>>(yaml) ?? [];
}
