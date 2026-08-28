using ApiDocs.Application.Ports;
using ApiDocs.Domain;

namespace ApiDocs.Infrastructure.Ingestion;

/// <summary>
/// Turns a parsed file into one chunk per <c>##</c> section, typed by its heading, and copies the
/// summary and the keywords of the owner onto every chunk (ARCHITECTURE §4.1): an
/// <c>Example Python</c> chunk stays reachable by "plug surface python" even though its code
/// contains none of those words.
/// </summary>
internal sealed class MarkdigChunker(ITokenCounter tokenCounter) : IChunker
{
    public IReadOnlyList<DocumentChunk> Chunk(ParsedFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var sections = MarkdownSections.Split(file.Body, bodyStartLine: 1);
        if (sections.Count == 0)
        {
            return [];
        }

        var chunks = new List<DocumentChunk>(sections.Count);

        foreach (var section in sections)
        {
            if (string.IsNullOrWhiteSpace(section.Body))
            {
                continue;
            }

            var kind = MapKind(file.Kind, section.Title, out var language);
            var breadcrumb = file.Kind switch
            {
                DocumentKind.Endpoint => Breadcrumb.ForEndpoint(file.Scope, file.Operation!.Value.Value, section.Title),
                DocumentKind.Domain => Breadcrumb.ForDomain(file.Scope, section.Title),
                _ => Breadcrumb.ForPlatform(file.Scope, section.Title),
            };

            var title = file.Kind == DocumentKind.Endpoint
                ? file.Operation!.Value.Value
                : $"{file.Scope} {section.Title}";

            chunks.Add(new DocumentChunk
            {
                Id = ChunkId.From(file.FilePath, breadcrumb),
                Kind = kind,
                Operation = file.Operation,
                DomainId = file.DomainId,
                Language = language,
                Title = title,
                Breadcrumb = breadcrumb,
                Summary = file.Summary,
                Keywords = file.Keywords,
                Body = section.Body,
                TokenCount = tokenCounter.Count(section.Body),
                FilePath = file.FilePath,
                Deprecated = file.Deprecated,
                Section = section.Title,
            });
        }

        return chunks;
    }

    private static SectionKind MapKind(DocumentKind documentKind, string title, out CodeLanguage language)
    {
        language = CodeLanguage.None;

        if (documentKind == DocumentKind.Platform)
        {
            return SectionKind.Platform;
        }

        if (documentKind == DocumentKind.Domain)
        {
            return title switch
            {
                "Overview" => SectionKind.DomainOverview,
                "Use cases" => SectionKind.DomainUseCases,
                "Typical workflow" => SectionKind.DomainWorkflow,
                "Authentication & scopes" => SectionKind.DomainAuth,
                "Common errors" => SectionKind.DomainErrors,
                _ => SectionKind.DomainOverview,
            };
        }

        switch (title)
        {
            case "Description":
                return SectionKind.Description;
            case "Parameters":
                return SectionKind.Parameters;
            case "Response":
                return SectionKind.Response;
            case "Notes":
                return SectionKind.Notes;
            case "Example C#":
                language = CodeLanguage.CSharp;
                return SectionKind.Example;
            case "Example Python":
                language = CodeLanguage.Python;
                return SectionKind.Example;
            default:
                return SectionKind.Notes;
        }
    }
}
