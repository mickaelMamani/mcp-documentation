using ApiDocs.Domain;

namespace ApiDocs.Application.Ports;

/// <summary>Restrictions applied as <c>Must</c> clauses (ARCHITECTURE §4.2 step 3).</summary>
internal sealed record SearchFilters
{
    public static SearchFilters None { get; } = new();

    public string? DomainId { get; init; }

    /// <summary>Restrict to the chunks of one endpoint.</summary>
    public string? OperationId { get; init; }

    public SectionKind[]? Kinds { get; init; }

    public CodeLanguage? Language { get; init; }

    /// <summary>Deprecated endpoints are excluded by default.</summary>
    public bool IncludeDeprecated { get; init; }
}

internal readonly record struct SearchHit(DocumentChunk Chunk, double Score);

/// <summary>Lexical (BM25) retrieval over the chunks of one snapshot.</summary>
internal interface ILexicalIndex : IDisposable
{
    IReadOnlyList<SearchHit> Search(string query, SearchFilters filters, int k);

    /// <summary>Closest known operationIds, used to answer an unknown operationId (ARCHITECTURE §5.2).</summary>
    IReadOnlyList<string> SuggestOperationIds(string operationId, int limit);
}

/// <summary>Builds the lexical index of a snapshot. One index per snapshot, never shared.</summary>
internal interface ILexicalIndexFactory
{
    ILexicalIndex Build(IReadOnlyList<DocumentChunk> chunks, IReadOnlyCollection<string> knownOperationIds);
}
