namespace ApiDocs.Domain;

/// <summary>
/// One <c>##</c> section of a documentation file, the unit that is indexed and returned
/// (ARCHITECTURE §4.1). Summary and Keywords are duplicated from the owning endpoint or domain
/// so that a code example stays reachable by a query made of business vocabulary.
/// </summary>
internal sealed record DocumentChunk
{
    public required ChunkId Id { get; init; }

    public required SectionKind Kind { get; init; }

    /// <summary>Owning endpoint; <see langword="null"/> for domain and platform chunks.</summary>
    public OperationId? Operation { get; init; }

    /// <summary>Domain id as written in the manifest, or <see cref="string.Empty"/> for the platform file.</summary>
    public required string DomainId { get; init; }

    public CodeLanguage Language { get; init; } = CodeLanguage.None;

    /// <summary>operationId for an endpoint chunk, section title otherwise. Indexed with a ×4 boost.</summary>
    public required string Title { get; init; }

    public required Breadcrumb Breadcrumb { get; init; }

    public required string Summary { get; init; }

    public required string Keywords { get; init; }

    /// <summary>Section body, endpoint references already rewritten to <c>endpoint://</c> links.</summary>
    public required string Body { get; init; }

    public required int TokenCount { get; init; }

    /// <summary>Repository relative path, forward slashes.</summary>
    public required string FilePath { get; init; }

    public bool Deprecated { get; init; }

    /// <summary>Heading of the section as it appears in the file, e.g. <c>Example C#</c>.</summary>
    public required string Section { get; init; }
}
