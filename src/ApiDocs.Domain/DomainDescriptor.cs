namespace ApiDocs.Domain;

/// <summary>Catalogue entry of a domain (ARCHITECTURE §5.2, <c>list_domains</c>).</summary>
internal sealed record DomainDescriptor
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Summary { get; init; }

    public required string FilePath { get; init; }

    public OperationId[] Endpoints { get; init; } = [];

    public string Keywords { get; init; } = string.Empty;

    /// <summary>Rows of the <c>## Use cases</c> table, parsed at ingestion.</summary>
    public UseCaseRow[] UseCases { get; init; } = [];

    /// <summary>Full raw Markdown of <c>_domain.md</c>, front matter excluded. Served by <c>doc://</c>.</summary>
    public string RawBody { get; init; } = string.Empty;
}
