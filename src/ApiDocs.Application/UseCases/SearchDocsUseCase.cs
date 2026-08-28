using ApiDocs.Application.Ports;
using ApiDocs.Application.Rendering;
using ApiDocs.Application.Retrieval;
using ApiDocs.Domain;

namespace ApiDocs.Application.UseCases;

internal sealed record SearchDocsResult(string Text, IReadOnlyList<SearchHit> Hits);

/// <summary>
/// ARCHITECTURE §5.2, <c>search_docs</c>: free text search over every section, for the
/// cross-cutting questions no single endpoint answers.
/// </summary>
internal sealed class SearchDocsUseCase(IIndexSnapshotProvider snapshots, ITokenCounter tokenCounter)
{
    public const int DefaultLimit = 5;
    public const int MaxLimit = 10;

    private const int Candidates = 30;

    public SearchDocsResult Execute(string query, string? domain = null, int? limit = null)
    {
        var snapshot = snapshots.Current;
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);

        if (string.IsNullOrWhiteSpace(query))
        {
            return new SearchDocsResult(EmptyMessage(query ?? string.Empty), []);
        }

        string? domainId = null;
        if (!string.IsNullOrWhiteSpace(domain))
        {
            if (!snapshot.TryGetDomain(domain.Trim(), out var known))
            {
                var ids = new string[snapshot.Domains.Length];
                for (var i = 0; i < ids.Length; i++)
                {
                    ids[i] = snapshot.Domains[i].Id;
                }

                return new SearchDocsResult($"Unknown domain \"{domain}\". Domains: {string.Join(", ", ids)}.", []);
            }

            domainId = known.Id;
        }

        var hits = snapshot.Search(
            query,
            new SearchFilters { DomainId = domainId, IncludeDeprecated = false },
            Candidates);

        var reranked = new List<SearchHit>(hits.Count);
        for (var i = 0; i < hits.Count; i++)
        {
            var hit = hits[i];
            reranked.Add(new SearchHit(hit.Chunk, hit.Score * KindWeights.ForDocsSearch(hit.Chunk.Kind)));
        }

        reranked.Sort(static (a, b) => b.Score.CompareTo(a.Score));
        if (reranked.Count > take)
        {
            reranked.RemoveRange(take, reranked.Count - take);
        }

        if (reranked.Count == 0)
        {
            return new SearchDocsResult(EmptyMessage(query), []);
        }

        var budget = new OutputBudget(tokenCounter, TokenBudget.SearchDocs);
        for (var i = 0; i < reranked.Count; i++)
        {
            var chunk = reranked[i].Chunk;
            var text = $"### {chunk.Breadcrumb}\n{chunk.Body.Trim()}";
            budget.TryAdd(new OutputBlock(text, chunk.Breadcrumb.ToString(), chunk.Operation?.Value));
        }

        budget.AppendOverflowFooter();
        return new SearchDocsResult(budget.ToString(), reranked);
    }

    private static string EmptyMessage(string query) =>
        $"""
         No documentation section matched "{query}". The documentation is in English — retry with
         English terms (e.g. "authentication scopes", "pagination page size").
         """;
}
