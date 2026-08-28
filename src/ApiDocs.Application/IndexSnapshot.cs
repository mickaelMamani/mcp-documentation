using System.Collections.Frozen;
using ApiDocs.Application.Ports;
using ApiDocs.Domain;

namespace ApiDocs.Application;

/// <summary>
/// Immutable view of the whole documentation: catalogue, chunks and lexical index.
/// Published atomically; a request never sees a partial index (ARCHITECTURE §6).
/// </summary>
internal sealed class IndexSnapshot : IDisposable
{
    private readonly ILexicalIndex _lexicalIndex;

    private IndexSnapshot(
        PlatformInfo platform,
        DomainDescriptor[] domains,
        FrozenDictionary<string, DomainDescriptor> domainsById,
        EndpointDescriptor[] endpoints,
        FrozenDictionary<string, EndpointDescriptor> endpointsByOperationId,
        DocumentChunk[] chunks,
        FrozenDictionary<string, DocumentChunk[]> chunksByOperation,
        ILexicalIndex lexicalIndex,
        ValidationIssue[] issues,
        int fileCount,
        TimeSpan buildDuration)
    {
        Platform = platform;
        Domains = domains;
        _domainsById = domainsById;
        Endpoints = endpoints;
        _endpointsByOperationId = endpointsByOperationId;
        Chunks = chunks;
        _chunksByOperation = chunksByOperation;
        _lexicalIndex = lexicalIndex;
        Issues = issues;
        FileCount = fileCount;
        BuildDuration = buildDuration;
    }

    private readonly FrozenDictionary<string, DomainDescriptor> _domainsById;
    private readonly FrozenDictionary<string, EndpointDescriptor> _endpointsByOperationId;
    private readonly FrozenDictionary<string, DocumentChunk[]> _chunksByOperation;

    public PlatformInfo Platform { get; }

    public DomainDescriptor[] Domains { get; }

    public EndpointDescriptor[] Endpoints { get; }

    public DocumentChunk[] Chunks { get; }

    public ValidationIssue[] Issues { get; }

    public int FileCount { get; }

    public TimeSpan BuildDuration { get; }

    public static IndexSnapshot Create(DocumentSet set, ILexicalIndexFactory indexFactory, TimeSpan buildDuration)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(indexFactory);

        var domains = new DomainDescriptor[set.Domains.Count];
        for (var i = 0; i < domains.Length; i++)
        {
            domains[i] = set.Domains[i];
        }

        var endpoints = new EndpointDescriptor[set.Endpoints.Count];
        for (var i = 0; i < endpoints.Length; i++)
        {
            endpoints[i] = set.Endpoints[i];
        }

        var chunks = new DocumentChunk[set.Chunks.Count];
        for (var i = 0; i < chunks.Length; i++)
        {
            chunks[i] = set.Chunks[i];
        }

        var issues = new ValidationIssue[set.Issues.Count];
        for (var i = 0; i < issues.Length; i++)
        {
            issues[i] = set.Issues[i];
        }

        var endpointsByOperationId = endpoints.ToFrozenDictionary(e => e.Operation.Value, StringComparer.Ordinal);
        var domainsById = domains.ToFrozenDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
        var grouped = new Dictionary<string, List<DocumentChunk>>(StringComparer.Ordinal);
        for (var i = 0; i < chunks.Length; i++)
        {
            if (chunks[i].Operation is not { } operation)
            {
                continue;
            }

            if (!grouped.TryGetValue(operation.Value, out var bucket))
            {
                bucket = [];
                grouped[operation.Value] = bucket;
            }

            bucket.Add(chunks[i]);
        }

        var chunksByOperation = grouped.ToFrozenDictionary(
            static pair => pair.Key,
            static pair => pair.Value.ToArray(),
            StringComparer.Ordinal);

        var index = indexFactory.Build(chunks, endpointsByOperationId.Keys);

        return new IndexSnapshot(
            set.Platform,
            domains,
            domainsById,
            endpoints,
            endpointsByOperationId,
            chunks,
            chunksByOperation,
            index,
            issues,
            set.FileCount,
            buildDuration);
    }

    public IReadOnlyList<SearchHit> Search(string query, SearchFilters filters, int k) =>
        _lexicalIndex.Search(query, filters, k);

    public IReadOnlyList<string> SuggestOperationIds(string operationId, int limit) =>
        _lexicalIndex.SuggestOperationIds(operationId, limit);

    public bool TryGetEndpoint(string operationId, out EndpointDescriptor endpoint) =>
        _endpointsByOperationId.TryGetValue(operationId, out endpoint!);

    /// <summary>All chunks of one endpoint, in file order.</summary>
    public DocumentChunk[] ChunksOf(string operationId) =>
        _chunksByOperation.TryGetValue(operationId, out var chunks) ? chunks : [];

    public bool TryGetDomain(string domainId, out DomainDescriptor domain) =>
        _domainsById.TryGetValue(domainId, out domain!);

    public int EndpointCountOf(string domainId)
    {
        var count = 0;
        var endpoints = Endpoints;
        for (var i = 0; i < endpoints.Length; i++)
        {
            if (string.Equals(endpoints[i].DomainId, domainId, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }

    public void Dispose() => _lexicalIndex.Dispose();
}
