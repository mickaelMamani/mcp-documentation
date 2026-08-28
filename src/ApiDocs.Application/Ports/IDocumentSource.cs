using ApiDocs.Domain;

namespace ApiDocs.Application.Ports;

/// <summary>Kind of file found in the documentation folder (DOC-FORMAT §2).</summary>
internal enum DocumentKind
{
    Endpoint,
    Domain,
    Platform,
}

/// <summary>
/// A documentation file after front matter parsing and validation, before chunking.
/// Carries everything the chunker needs so that it never has to look anything up.
/// </summary>
internal sealed record ParsedFile
{
    public required DocumentKind Kind { get; init; }

    /// <summary>Repository relative path, forward slashes.</summary>
    public required string FilePath { get; init; }

    /// <summary>Manifest domain id; <see cref="string.Empty"/> for the platform file.</summary>
    public required string DomainId { get; init; }

    /// <summary>Display name used as the first segment of the breadcrumb.</summary>
    public required string Scope { get; init; }

    public OperationId? Operation { get; init; }

    public required string Summary { get; init; }

    public required string Keywords { get; init; }

    public bool Deprecated { get; init; }

    /// <summary>Markdown body, front matter removed, endpoint references already rewritten.</summary>
    public required string Body { get; init; }
}

/// <summary>Everything an index snapshot is built from.</summary>
internal sealed record DocumentSet
{
    public required PlatformInfo Platform { get; init; }

    public required IReadOnlyList<DomainDescriptor> Domains { get; init; }

    public required IReadOnlyList<EndpointDescriptor> Endpoints { get; init; }

    public required IReadOnlyList<DocumentChunk> Chunks { get; init; }

    public required IReadOnlyList<ValidationIssue> Issues { get; init; }

    public int FileCount { get; init; }
}

/// <summary>Reads the documentation folder and produces the material of a snapshot.</summary>
internal interface IDocumentSource
{
    Task<DocumentSet> LoadAsync(CancellationToken cancellationToken = default);
}
