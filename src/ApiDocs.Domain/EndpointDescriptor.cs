namespace ApiDocs.Domain;

/// <summary>
/// Catalogue entry of an endpoint, merged from <c>manifest.json</c> and from the file front matter
/// (ARCHITECTURE §4.1).
/// </summary>
internal sealed record EndpointDescriptor
{
    public required OperationId Operation { get; init; }

    public required string DomainId { get; init; }

    public required string Method { get; init; }

    public required string Route { get; init; }

    public string? Version { get; init; }

    public required string Summary { get; init; }

    public string[] Tags { get; init; } = [];

    public string[] Aliases { get; init; } = [];

    public string Keywords { get; init; } = string.Empty;

    public string[] Related { get; init; } = [];

    public bool Deprecated { get; init; }

    public required string FilePath { get; init; }

    public required string ContentHash { get; init; }

    /// <summary>Full raw Markdown of the file, front matter excluded. Served by <c>endpoint://</c>.</summary>
    public required string RawBody { get; init; }

    public string Signature => $"{Method} {Route}";
}
