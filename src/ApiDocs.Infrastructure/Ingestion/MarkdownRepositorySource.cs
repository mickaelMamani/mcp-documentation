using System.Security.Cryptography;
using System.Text.Json;
using ApiDocs.Application.Ports;
using ApiDocs.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApiDocs.Infrastructure.Ingestion;

internal sealed class DocsOptions
{
    public const string SectionName = "Docs";

    /// <summary>Path of the documentation folder, the one holding <c>manifest.json</c>.</summary>
    public string Path { get; set; } = "docs";
}

/// <summary>
/// Reads the documentation folder described by <c>manifest.json</c> (DOC-FORMAT §2 and §3),
/// validates every file and produces the material of a snapshot. A file that fails validation is
/// rejected and reported; the rest of the corpus is still indexed (ARCHITECTURE §6).
/// </summary>
internal sealed class MarkdownRepositorySource(
    IOptions<DocsOptions> options,
    IChunker chunker,
    ILogger<MarkdownRepositorySource> logger) : IDocumentSource
{
    private const string ManifestFileName = "manifest.json";
    private const string PlatformFileName = "_platform.md";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly DocumentValidator _validator = new();
    private readonly string _root = System.IO.Path.GetFullPath(options.Value.Path);

    public async Task<DocumentSet> LoadAsync(CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();
        var manifestPath = System.IO.Path.Combine(_root, ManifestFileName);

        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"No {ManifestFileName} found in \"{_root}\".", manifestPath);
        }

        var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        var manifest = JsonSerializer.Deserialize<DocManifest>(manifestJson, JsonOptions)
            ?? throw new InvalidDataException($"{ManifestFileName} is empty or not a JSON object.");

        _validator.ValidateManifest(ManifestFileName, manifest, issues);

        var knownOperationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var domain in manifest.Domains)
        {
            foreach (var endpoint in domain.Endpoints)
            {
                if (endpoint.OperationId is { Length: > 0 } operationId)
                {
                    knownOperationIds.Add(operationId);
                }
            }
        }

        var fileCount = 1;
        var chunks = new List<DocumentChunk>(256);
        var domains = new List<DomainDescriptor>(manifest.Domains.Count);
        var endpoints = new List<EndpointDescriptor>(knownOperationIds.Count);

        var platform = await LoadPlatformAsync(manifest, knownOperationIds, chunks, issues, cancellationToken)
            .ConfigureAwait(false);
        if (platform.RawBody.Length > 0)
        {
            fileCount++;
        }

        foreach (var manifestDomain in manifest.Domains)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (manifestDomain.Id is not { Length: > 0 } domainId || manifestDomain.File is not { Length: > 0 } domainFile)
            {
                continue;
            }

            var domainName = manifestDomain.Name ?? domainId;
            var descriptor = await LoadDomainAsync(
                manifestDomain,
                domainId,
                domainName,
                domainFile,
                knownOperationIds,
                chunks,
                issues,
                cancellationToken).ConfigureAwait(false);

            if (descriptor is not null)
            {
                domains.Add(descriptor);
                fileCount++;
            }

            foreach (var manifestEndpoint in manifestDomain.Endpoints)
            {
                var endpoint = await LoadEndpointAsync(
                    manifestEndpoint,
                    domainId,
                    domainName,
                    knownOperationIds,
                    chunks,
                    issues,
                    cancellationToken).ConfigureAwait(false);

                if (endpoint is not null)
                {
                    endpoints.Add(endpoint);
                    fileCount++;
                }
            }
        }

        foreach (var issue in issues)
        {
            Log.ValidationFailed(logger, issue.FilePath, issue.Rule, issue.Message);
        }

        return new DocumentSet
        {
            Platform = platform,
            Domains = domains,
            Endpoints = endpoints,
            Chunks = chunks,
            Issues = issues,
            FileCount = fileCount,
        };
    }

    private async Task<PlatformInfo> LoadPlatformAsync(
        DocManifest manifest,
        HashSet<string> knownOperationIds,
        List<DocumentChunk> chunks,
        List<ValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        var info = new PlatformInfo
        {
            FormatVersion = manifest.FormatVersion ?? DocumentValidator.SupportedFormatVersion,
            Platform = manifest.Platform ?? "API",
            PlatformVersion = manifest.PlatformVersion ?? "0",
            Language = manifest.Language ?? "en",
            GeneratedAt = manifest.GeneratedAt,
        };

        var fullPath = System.IO.Path.Combine(_root, PlatformFileName);
        if (!File.Exists(fullPath))
        {
            issues.Add(new ValidationIssue(
                PlatformFileName,
                DocumentValidator.Rules.FileMissing,
                "The platform overview file is missing.",
                ValidationSeverity.Warning));
            return info;
        }

        var text = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
        if (!FrontMatterReader.TrySplit(text, out var yaml, out var body, out _))
        {
            issues.Add(new ValidationIssue(PlatformFileName, DocumentValidator.Rules.FrontMatterMissing, "No YAML front matter."));
            return info;
        }

        var frontMatter = FrontMatterReader.Parse(yaml);
        var fileIssues = new List<ValidationIssue>();
        _validator.ValidatePlatform(PlatformFileName, frontMatter, fileIssues);
        issues.AddRange(fileIssues);

        if (HasError(fileIssues))
        {
            return info;
        }

        var rewritten = EndpointLinkRewriter.Rewrite(body, knownOperationIds);
        chunks.AddRange(chunker.Chunk(new ParsedFile
        {
            Kind = DocumentKind.Platform,
            FilePath = PlatformFileName,
            DomainId = string.Empty,
            Scope = frontMatter.Platform ?? info.Platform,
            Summary = frontMatter.Summary ?? string.Empty,
            Keywords = frontMatter.Keywords ?? string.Empty,
            Body = rewritten,
        }));

        return info with { RawBody = rewritten };
    }

    private async Task<DomainDescriptor?> LoadDomainAsync(
        ManifestDomain manifestDomain,
        string domainId,
        string domainName,
        string relativePath,
        HashSet<string> knownOperationIds,
        List<DocumentChunk> chunks,
        List<ValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        var fullPath = System.IO.Path.Combine(_root, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            issues.Add(new ValidationIssue(relativePath, DocumentValidator.Rules.FileMissing, "File declared in the manifest but not found."));
            return null;
        }

        var text = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
        if (!FrontMatterReader.TrySplit(text, out var yaml, out var body, out _))
        {
            issues.Add(new ValidationIssue(relativePath, DocumentValidator.Rules.FrontMatterMissing, "No YAML front matter."));
            return null;
        }

        var frontMatter = FrontMatterReader.Parse(yaml);
        var sections = MarkdownSections.Split(body, bodyStartLine: 1);

        var fileIssues = new List<ValidationIssue>();
        _validator.ValidateDomain(relativePath, frontMatter, sections, knownOperationIds, fileIssues);
        issues.AddRange(fileIssues);

        if (HasError(fileIssues))
        {
            return null;
        }

        var rewritten = EndpointLinkRewriter.Rewrite(body, knownOperationIds);
        chunks.AddRange(chunker.Chunk(new ParsedFile
        {
            Kind = DocumentKind.Domain,
            FilePath = relativePath,
            DomainId = domainId,
            Scope = domainName,
            Summary = frontMatter.Summary ?? manifestDomain.Summary ?? string.Empty,
            Keywords = frontMatter.Keywords ?? string.Empty,
            Body = rewritten,
        }));

        var endpointIds = new List<OperationId>(frontMatter.Endpoints.Count);
        foreach (var value in frontMatter.Endpoints)
        {
            if (OperationId.TryCreate(value, out var id))
            {
                endpointIds.Add(id);
            }
        }

        return new DomainDescriptor
        {
            Id = domainId,
            Name = domainName,
            Summary = manifestDomain.Summary ?? frontMatter.Summary ?? string.Empty,
            FilePath = relativePath,
            Endpoints = endpointIds.ToArray(),
            Keywords = frontMatter.Keywords ?? string.Empty,
            UseCases = ParseUseCases(sections, knownOperationIds),
            RawBody = rewritten,
        };
    }

    private async Task<EndpointDescriptor?> LoadEndpointAsync(
        ManifestEndpoint manifestEndpoint,
        string domainId,
        string domainName,
        HashSet<string> knownOperationIds,
        List<DocumentChunk> chunks,
        List<ValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        if (manifestEndpoint.File is not { Length: > 0 } relativePath
            || !OperationId.TryCreate(manifestEndpoint.OperationId, out var operationId))
        {
            return null;
        }

        var fullPath = System.IO.Path.Combine(_root, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            issues.Add(new ValidationIssue(relativePath, DocumentValidator.Rules.FileMissing, "File declared in the manifest but not found."));
            return null;
        }

        var text = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
        if (!FrontMatterReader.TrySplit(text, out var yaml, out var body, out _))
        {
            issues.Add(new ValidationIssue(relativePath, DocumentValidator.Rules.FrontMatterMissing, "No YAML front matter."));
            return null;
        }

        var frontMatter = FrontMatterReader.Parse(yaml);
        var sections = MarkdownSections.Split(body, bodyStartLine: 1);

        var fileIssues = new List<ValidationIssue>();
        _validator.ValidateEndpoint(relativePath, frontMatter, sections, manifestEndpoint, knownOperationIds, fileIssues);
        issues.AddRange(fileIssues);

        if (HasError(fileIssues))
        {
            return null;
        }

        var summary = frontMatter.Summary ?? manifestEndpoint.Summary ?? string.Empty;
        var keywords = frontMatter.Keywords ?? string.Empty;
        var deprecated = frontMatter.Deprecated || manifestEndpoint.Deprecated;
        var rewritten = EndpointLinkRewriter.Rewrite(body, knownOperationIds);

        chunks.AddRange(chunker.Chunk(new ParsedFile
        {
            Kind = DocumentKind.Endpoint,
            FilePath = relativePath,
            DomainId = domainId,
            Scope = domainName,
            Operation = operationId,
            Summary = summary,
            Keywords = keywords,
            Deprecated = deprecated,
            Body = rewritten,
        }));

        return new EndpointDescriptor
        {
            Operation = operationId,
            DomainId = domainId,
            Method = (frontMatter.Method ?? manifestEndpoint.Method ?? "GET").ToUpperInvariant(),
            Route = frontMatter.Route ?? manifestEndpoint.Route ?? "/",
            Version = frontMatter.Version,
            Summary = summary,
            Tags = manifestEndpoint.Tags.Count > 0 ? [.. manifestEndpoint.Tags] : [.. frontMatter.Tags],
            Aliases = [.. frontMatter.Aliases],
            Keywords = keywords,
            Related = [.. frontMatter.Related],
            Deprecated = deprecated,
            FilePath = relativePath,
            ContentHash = Hash(text),
            RawBody = rewritten,
        };
    }

    /// <summary>
    /// DOC-FORMAT §4.2: the <c>Use cases</c> table is what turns an intent into endpoints. Rows are
    /// parsed once at ingestion so that <c>search_endpoints</c> only has to score them.
    /// </summary>
    private static UseCaseRow[] ParseUseCases(IReadOnlyList<MarkdownSection> sections, HashSet<string> knownOperationIds)
    {
        foreach (var section in sections)
        {
            if (!string.Equals(section.Title, "Use cases", StringComparison.Ordinal))
            {
                continue;
            }

            var rows = MarkdownSections.TableRows(section.Body);
            var parsed = new List<UseCaseRow>(rows.Count);

            foreach (var cells in rows)
            {
                if (cells.Length < 2)
                {
                    continue;
                }

                var operations = new List<OperationId>(2);
                foreach (var candidate in ExtractInlineCode(cells[1]))
                {
                    if (knownOperationIds.Contains(candidate) && OperationId.TryCreate(candidate, out var id))
                    {
                        operations.Add(id);
                    }
                }

                if (operations.Count == 0)
                {
                    continue;
                }

                parsed.Add(new UseCaseRow(cells[0], operations.ToArray(), cells.Length > 2 ? cells[2] : string.Empty));
            }

            return parsed.ToArray();
        }

        return [];
    }

    private static IEnumerable<string> ExtractInlineCode(string cell)
    {
        var index = 0;
        while (index < cell.Length)
        {
            var open = cell.IndexOf('`', index);
            if (open < 0)
            {
                yield break;
            }

            var close = cell.IndexOf('`', open + 1);
            if (close < 0)
            {
                yield break;
            }

            yield return cell[(open + 1)..close];
            index = close + 1;
        }
    }

    private static bool HasError(List<ValidationIssue> issues)
    {
        for (var i = 0; i < issues.Count; i++)
        {
            if (issues[i].Severity == ValidationSeverity.Error)
            {
                return true;
            }
        }

        return false;
    }

    private static string Hash(string content) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
}
