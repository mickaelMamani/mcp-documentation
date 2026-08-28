using ApiDocs.Application.Ports;
using ApiDocs.Application.Rendering;
using ApiDocs.Application.Retrieval;
using ApiDocs.Domain;

namespace ApiDocs.Application.UseCases;

internal readonly record struct EndpointMatch(EndpointDescriptor Endpoint, double Score);

internal sealed record SearchEndpointsResult(string Text, IReadOnlyList<EndpointMatch> Matches);

/// <summary>
/// ARCHITECTURE §5.2, <c>search_endpoints</c>: lexical search restricted to the intent bearing
/// chunks, aggregated per operationId, with the matched <c>Use cases</c> rows converted into their
/// target endpoints, then re-weighted by the ranking policy of <see cref="RankingOptions"/>.
/// </summary>
internal sealed class SearchEndpointsUseCase(
    IIndexSnapshotProvider snapshots,
    ITokenCounter tokenCounter,
    RankingOptions ranking)
{
    public const int DefaultLimit = 8;
    public const int MaxLimit = 20;

    private const int Candidates = 30;

    /// <summary>
    /// Fraction of the best score under which a candidate is dropped. With
    /// <c>MinimumNumberShouldMatch = 1</c> a single shared token is enough to reach the result list,
    /// so the tail is noise: it costs tokens and it invites the model to pick the wrong endpoint.
    /// </summary>
    private const double RelativeScoreFloor = 0.25d;

    private static readonly SectionKind[] IntentKinds = [SectionKind.Description, SectionKind.DomainUseCases];

    public SearchEndpointsResult Execute(string query, string? domain = null, int? limit = null)
    {
        var snapshot = snapshots.Current;
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);

        if (string.IsNullOrWhiteSpace(query))
        {
            return new SearchEndpointsResult(EmptyMessage(snapshot, query ?? string.Empty), []);
        }

        string? domainId = null;
        if (!string.IsNullOrWhiteSpace(domain))
        {
            if (!snapshot.TryGetDomain(domain.Trim(), out var known))
            {
                return new SearchEndpointsResult(
                    $"Unknown domain \"{domain}\". Domains: {DomainList(snapshot)}.",
                    []);
            }

            domainId = known.Id;
        }

        var filters = new SearchFilters { DomainId = domainId, Kinds = IntentKinds, IncludeDeprecated = false };
        var hits = snapshot.Search(query, filters, Candidates);

        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        var queryTokens = QueryTokens.Split(query);

        for (var i = 0; i < hits.Count; i++)
        {
            var hit = hits[i];
            var weighted = hit.Score * KindWeights.ForEndpointSearch(hit.Chunk.Kind);

            if (hit.Chunk.Operation is { } operation)
            {
                Accumulate(scores, operation.Value, weighted);
                continue;
            }

            if (hit.Chunk.Kind != SectionKind.DomainUseCases
                || !snapshot.TryGetDomain(hit.Chunk.DomainId, out var domainDescriptor))
            {
                continue;
            }

            var rows = domainDescriptor.UseCases;
            for (var r = 0; r < rows.Length; r++)
            {
                var row = rows[r];
                var overlap = QueryTokens.Overlap(queryTokens, row.Intent);
                if (overlap <= 0d)
                {
                    continue;
                }

                var rowScore = weighted * (0.5d + (0.5d * overlap));
                for (var o = 0; o < row.Operations.Length; o++)
                {
                    Accumulate(scores, row.Operations[o].Value, rowScore);
                }
            }
        }

        var matches = Rank(snapshot, scores, queryTokens, take);
        return new SearchEndpointsResult(Render(snapshot, query, matches), matches);
    }

    private static void Accumulate(Dictionary<string, double> scores, string operationId, double score)
    {
        if (!scores.TryGetValue(operationId, out var current) || score > current)
        {
            scores[operationId] = score;
        }
    }

    private EndpointMatch[] Rank(
        IndexSnapshot snapshot,
        Dictionary<string, double> scores,
        string[] queryTokens,
        int take)
    {
        var affinities = DomainAffinities(snapshot, queryTokens);
        var candidates = new List<EndpointMatch>(scores.Count);

        foreach (var pair in scores)
        {
            if (!snapshot.TryGetEndpoint(pair.Key, out var endpoint) || endpoint.Deprecated)
            {
                continue;
            }

            candidates.Add(new EndpointMatch(endpoint, Reweight(endpoint, pair.Value, queryTokens, affinities)));
        }

        candidates.Sort(static (a, b) =>
        {
            var byScore = b.Score.CompareTo(a.Score);
            return byScore != 0
                ? byScore
                : string.CompareOrdinal(a.Endpoint.Operation.Value, b.Endpoint.Operation.Value);
        });

        if (candidates.Count > 0)
        {
            var floor = candidates[0].Score * RelativeScoreFloor;
            var kept = candidates.Count;
            for (var i = 1; i < candidates.Count; i++)
            {
                if (candidates[i].Score < floor)
                {
                    kept = i;
                    break;
                }
            }

            if (kept < candidates.Count)
            {
                candidates.RemoveRange(kept, candidates.Count - kept);
            }
        }

        if (candidates.Count > take)
        {
            candidates.RemoveRange(take, candidates.Count - take);
        }

        return candidates.ToArray();
    }

    /// <summary>
    /// Coordination and domain coherence, the two signals BM25 cannot express on its own: it scores
    /// each term independently and knows nothing about which domain the query is talking about.
    /// </summary>
    private double Reweight(
        EndpointDescriptor endpoint,
        double score,
        string[] queryTokens,
        Dictionary<string, double>? affinities)
    {
        if (ranking.CoverageWeight > 0d)
        {
            var identity = string.Concat(
                endpoint.Operation.Value, " ", endpoint.Summary, " ", endpoint.Keywords);
            score *= 1d + (ranking.CoverageWeight * QueryTokens.Overlap(queryTokens, identity));
        }

        if (affinities is not null && affinities.TryGetValue(endpoint.DomainId, out var affinity))
        {
            score *= 1d + (ranking.DomainAffinityWeight * affinity);
        }

        return score;
    }

    private Dictionary<string, double>? DomainAffinities(IndexSnapshot snapshot, string[] queryTokens)
    {
        if (ranking.DomainAffinityWeight <= 0d)
        {
            return null;
        }

        var domains = snapshot.Domains;
        var affinities = new Dictionary<string, double>(domains.Length, StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < domains.Length; i++)
        {
            var domain = domains[i];
            var identity = string.Concat(domain.Id, " ", domain.Name, " ", domain.Keywords);
            affinities[domain.Id] = QueryTokens.Overlap(queryTokens, identity);
        }

        return affinities;
    }

    private string Render(IndexSnapshot snapshot, string query, EndpointMatch[] matches)
    {
        if (matches.Length == 0)
        {
            return EmptyMessage(snapshot, query);
        }

        var budget = new OutputBudget(tokenCounter, TokenBudget.SearchEndpoints, separator: "\n");

        for (var i = 0; i < matches.Length; i++)
        {
            var endpoint = matches[i].Endpoint;
            var line =
                $"{i + 1}. {endpoint.Operation.Value} — {endpoint.Method} {endpoint.Route} — {endpoint.Summary} [domain: {endpoint.DomainId}]";
            budget.TryAdd(new OutputBlock(line, endpoint.Operation.Value, endpoint.Operation.Value));
        }

        budget.AddMandatory("Next: call get_endpoint(operationId) for details.");
        return budget.ToString();
    }

    private static string EmptyMessage(IndexSnapshot snapshot, string query) =>
        $"""
         No endpoint matched "{query}". The index is in English — retry with English API terms
         (entity + action, e.g. "folio search"). Domains: {DomainList(snapshot)}.
         """;

    private static string DomainList(IndexSnapshot snapshot)
    {
        var domains = snapshot.Domains;
        var ids = new string[domains.Length];
        for (var i = 0; i < domains.Length; i++)
        {
            ids[i] = domains[i].Id;
        }

        return string.Join(", ", ids);
    }
}
