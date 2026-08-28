using System.Text;
using ApiDocs.Application.Ports;
using ApiDocs.Application.Rendering;
using ApiDocs.Domain;

namespace ApiDocs.Application.UseCases;

internal sealed record GetEndpointResult(string Text, bool Found, int Tokens);

/// <summary>
/// ARCHITECTURE §5.2, <c>get_endpoint</c>: fixed header, then the sections either in the standard
/// order or ranked by lexical relevance when a query is given, under a 2 500 token budget.
/// </summary>
internal sealed class GetEndpointUseCase(IIndexSnapshotProvider snapshots, ITokenCounter tokenCounter)
{
    private const int SuggestionCount = 3;
    private const int SectionCandidates = 20;

    /// <summary>Standard layout of DOC-FORMAT §4.1.</summary>
    private static readonly SectionKind[] StandardOrder =
    [
        SectionKind.Description,
        SectionKind.Parameters,
        SectionKind.Response,
        SectionKind.Example,
        SectionKind.Notes,
    ];

    public GetEndpointResult Execute(string operationId, string? query = null, string? language = null)
    {
        var snapshot = snapshots.Current;

        if (string.IsNullOrWhiteSpace(operationId))
        {
            return new GetEndpointResult(
                "operationId is required. Call search_endpoints first to get an exact operationId.",
                Found: false,
                Tokens: 0);
        }

        var requested = operationId.Trim();
        if (!snapshot.TryGetEndpoint(requested, out var endpoint))
        {
            return new GetEndpointResult(UnknownOperation(snapshot, requested), Found: false, Tokens: 0);
        }

        CodeLanguage? wanted = null;
        if (!string.IsNullOrWhiteSpace(language))
        {
            if (!CodeLanguageExtensions.TryParse(language, out var parsed))
            {
                return new GetEndpointResult(
                    $"Unknown language \"{language}\". Use \"csharp\" or \"python\", or omit it for both.",
                    Found: false,
                    Tokens: 0);
            }

            wanted = parsed;
        }

        var sections = OrderSections(snapshot, endpoint, query, wanted);
        var budget = new OutputBudget(tokenCounter, TokenBudget.GetEndpoint);
        budget.AddMandatory(Header(endpoint));

        for (var i = 0; i < sections.Length; i++)
        {
            var chunk = sections[i];
            var text = $"## {chunk.Section}\n{chunk.Body.Trim()}";
            budget.TryAdd(new OutputBlock(text, chunk.Breadcrumb.ToString(), endpoint.Operation.Value));
        }

        if (endpoint.Related.Length > 0)
        {
            budget.AddMandatory($"Related: {string.Join(", ", endpoint.Related)}");
        }

        budget.AppendOverflowFooter();
        return new GetEndpointResult(budget.ToString(), Found: true, budget.UsedTokens);
    }

    private static string Header(EndpointDescriptor endpoint)
    {
        var header = new StringBuilder();
        header.Append("# ").Append(endpoint.Operation.Value)
            .Append(" — ").Append(endpoint.Method).Append(' ').Append(endpoint.Route).Append('\n');
        header.Append(endpoint.Summary).Append('\n');
        header.Append("Domain: ").Append(endpoint.DomainId);

        if (!string.IsNullOrWhiteSpace(endpoint.Version))
        {
            header.Append(" · API version: ").Append(endpoint.Version);
        }

        if (endpoint.Deprecated)
        {
            header.Append("\n**Deprecated** — prefer the replacement named in the description.");
        }

        return header.ToString();
    }

    private static DocumentChunk[] OrderSections(
        IndexSnapshot snapshot,
        EndpointDescriptor endpoint,
        string? query,
        CodeLanguage? language)
    {
        var all = snapshot.ChunksOf(endpoint.Operation.Value);
        var kept = new List<DocumentChunk>(all.Length);

        for (var i = 0; i < all.Length; i++)
        {
            var chunk = all[i];
            if (chunk.Kind == SectionKind.Example && language is { } wanted && chunk.Language != wanted)
            {
                continue;
            }

            kept.Add(chunk);
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            kept.Sort(static (a, b) =>
            {
                var byKind = Array.IndexOf(StandardOrder, a.Kind).CompareTo(Array.IndexOf(StandardOrder, b.Kind));
                return byKind != 0 ? byKind : a.Language.CompareTo(b.Language);
            });

            return kept.ToArray();
        }

        var filters = new SearchFilters
        {
            OperationId = endpoint.Operation.Value,
            IncludeDeprecated = true,
        };

        var hits = snapshot.Search(query, filters, SectionCandidates);
        var ranks = new Dictionary<string, double>(StringComparer.Ordinal);
        for (var i = 0; i < hits.Count; i++)
        {
            ranks[hits[i].Chunk.Id.Value] = hits[i].Score;
        }

        // Parameters is always returned first: it is the section the caller almost always needs next.
        kept.Sort((a, b) =>
        {
            var byScore = Weight(b, ranks).CompareTo(Weight(a, ranks));
            return byScore != 0
                ? byScore
                : Array.IndexOf(StandardOrder, a.Kind).CompareTo(Array.IndexOf(StandardOrder, b.Kind));
        });

        return kept.ToArray();
    }

    private const double ParametersPriority = 1e6;

    private static double Weight(DocumentChunk chunk, Dictionary<string, double> ranks)
    {
        var score = ranks.TryGetValue(chunk.Id.Value, out var value) ? value : 0d;
        return chunk.Kind == SectionKind.Parameters ? score + ParametersPriority : score;
    }

    private static string UnknownOperation(IndexSnapshot snapshot, string operationId)
    {
        var suggestions = snapshot.SuggestOperationIds(operationId, SuggestionCount);
        return suggestions.Count == 0
            ? $"Unknown operationId \"{operationId}\". Call search_endpoints to find the right one."
            : $"Unknown operationId \"{operationId}\". Did you mean: {string.Join(", ", suggestions)}?";
    }
}
