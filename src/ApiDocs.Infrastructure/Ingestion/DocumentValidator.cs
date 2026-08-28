using ApiDocs.Domain;

namespace ApiDocs.Infrastructure.Ingestion;

/// <summary>
/// The validation rules of DOC-FORMAT §7. A file that produces an <see cref="ValidationSeverity.Error"/>
/// is rejected; the rest of the index is still built (ARCHITECTURE §6).
/// </summary>
internal sealed class DocumentValidator
{
    public const string SupportedFormatVersion = "1.0";

    public const int MaxSummaryLength = 160;

    internal static readonly string[] EndpointSections =
        ["Description", "Parameters", "Response", "Example C#", "Example Python", "Notes"];

    internal static readonly string[] DomainSections =
        ["Overview", "Use cases", "Typical workflow", "Authentication & scopes", "Common errors"];

    internal static readonly string[] ParameterColumns =
        ["Name", "In", "Type", "Required", "Description"];

    private const string ForbiddenKeywordPunctuation = ",;:.!?()[]{}\"'`<>=";

    public static class Rules
    {
        public const string FrontMatterMissing = "front-matter.missing";
        public const string FrontMatterRequired = "front-matter.required";
        public const string FormatVersion = "front-matter.formatVersion";
        public const string ManifestConsistency = "manifest.consistency";
        public const string OperationIdUnique = "operationId.unique";
        public const string SectionTitles = "sections.titles";
        public const string ExampleCodeBlock = "sections.example.codeblock";
        public const string ParametersTable = "sections.parameters.table";
        public const string SummaryLength = "summary.length";
        public const string KeywordsPunctuation = "keywords.punctuation";
        public const string RelatedExists = "related.exists";
        public const string FileMissing = "manifest.file";
    }

    public void ValidateEndpoint(
        string filePath,
        FrontMatter frontMatter,
        IReadOnlyList<MarkdownSection> sections,
        ManifestEndpoint manifestEntry,
        IReadOnlyCollection<string> knownOperationIds,
        List<ValidationIssue> issues)
    {
        ValidateFormatVersion(filePath, frontMatter, issues);

        Require(filePath, "operationId", frontMatter.OperationId, issues);
        Require(filePath, "domain", frontMatter.Domain, issues);
        Require(filePath, "method", frontMatter.Method, issues);
        Require(filePath, "route", frontMatter.Route, issues);
        Require(filePath, "summary", frontMatter.Summary, issues);
        Require(filePath, "keywords", frontMatter.Keywords, issues);

        if (!string.Equals(frontMatter.OperationId, manifestEntry.OperationId, StringComparison.Ordinal))
        {
            issues.Add(new ValidationIssue(
                filePath,
                Rules.ManifestConsistency,
                $"Front matter operationId \"{frontMatter.OperationId}\" does not match the manifest entry \"{manifestEntry.OperationId}\"."));
        }

        if (!string.Equals(frontMatter.Method, manifestEntry.Method, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new ValidationIssue(
                filePath,
                Rules.ManifestConsistency,
                $"Method \"{frontMatter.Method}\" differs from the manifest method \"{manifestEntry.Method}\"."));
        }

        if (!string.Equals(frontMatter.Route, manifestEntry.Route, StringComparison.Ordinal))
        {
            issues.Add(new ValidationIssue(
                filePath,
                Rules.ManifestConsistency,
                $"Route \"{frontMatter.Route}\" differs from the manifest route \"{manifestEntry.Route}\"."));
        }

        ValidateSummary(filePath, frontMatter.Summary, issues);
        ValidateKeywords(filePath, frontMatter.Keywords, issues);
        ValidateSectionTitles(filePath, sections, EndpointSections, issues);
        ValidateExamples(filePath, sections, issues);
        ValidateParameters(filePath, sections, issues);

        foreach (var related in frontMatter.Related)
        {
            if (!knownOperationIds.Contains(related))
            {
                issues.Add(new ValidationIssue(
                    filePath,
                    Rules.RelatedExists,
                    $"related references the unknown operationId \"{related}\"."));
            }
        }
    }

    public void ValidateDomain(
        string filePath,
        FrontMatter frontMatter,
        IReadOnlyList<MarkdownSection> sections,
        IReadOnlyCollection<string> knownOperationIds,
        List<ValidationIssue> issues)
    {
        ValidateFormatVersion(filePath, frontMatter, issues);
        Require(filePath, "domain", frontMatter.Domain, issues);
        Require(filePath, "summary", frontMatter.Summary, issues);
        Require(filePath, "keywords", frontMatter.Keywords, issues);

        ValidateSummary(filePath, frontMatter.Summary, issues);
        ValidateKeywords(filePath, frontMatter.Keywords, issues);
        ValidateSectionTitles(filePath, sections, DomainSections, issues);

        foreach (var operationId in frontMatter.Endpoints)
        {
            if (!knownOperationIds.Contains(operationId))
            {
                issues.Add(new ValidationIssue(
                    filePath,
                    Rules.RelatedExists,
                    $"endpoints references the unknown operationId \"{operationId}\"."));
            }
        }
    }

    public void ValidatePlatform(string filePath, FrontMatter frontMatter, List<ValidationIssue> issues)
    {
        ValidateFormatVersion(filePath, frontMatter, issues);
        Require(filePath, "platform", frontMatter.Platform, issues);
        Require(filePath, "platformVersion", frontMatter.PlatformVersion, issues);
        Require(filePath, "summary", frontMatter.Summary, issues);
        Require(filePath, "keywords", frontMatter.Keywords, issues);

        ValidateSummary(filePath, frontMatter.Summary, issues);
        ValidateKeywords(filePath, frontMatter.Keywords, issues);

        // ARCHITECTURE ADR #8: section titles are free in the platform file.
    }

    public void ValidateManifest(string filePath, DocManifest manifest, List<ValidationIssue> issues)
    {
        if (!string.Equals(manifest.FormatVersion, SupportedFormatVersion, StringComparison.Ordinal))
        {
            issues.Add(new ValidationIssue(
                filePath,
                Rules.FormatVersion,
                $"Unsupported formatVersion \"{manifest.FormatVersion}\"; this server supports {SupportedFormatVersion}."));
        }

        Require(filePath, "platform", manifest.Platform, issues);
        Require(filePath, "platformVersion", manifest.PlatformVersion, issues);
        Require(filePath, "language", manifest.Language, issues);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var domain in manifest.Domains)
        {
            Require(filePath, "domains[].id", domain.Id, issues);
            Require(filePath, "domains[].name", domain.Name, issues);
            Require(filePath, "domains[].summary", domain.Summary, issues);
            Require(filePath, "domains[].file", domain.File, issues);

            foreach (var endpoint in domain.Endpoints)
            {
                Require(filePath, "endpoints[].operationId", endpoint.OperationId, issues);
                Require(filePath, "endpoints[].method", endpoint.Method, issues);
                Require(filePath, "endpoints[].route", endpoint.Route, issues);
                Require(filePath, "endpoints[].summary", endpoint.Summary, issues);
                Require(filePath, "endpoints[].file", endpoint.File, issues);

                if (endpoint.OperationId is { } operationId && !seen.Add(operationId))
                {
                    issues.Add(new ValidationIssue(
                        filePath,
                        Rules.OperationIdUnique,
                        $"operationId \"{operationId}\" appears more than once in the manifest."));
                }
            }
        }
    }

    private static void ValidateFormatVersion(string filePath, FrontMatter frontMatter, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(frontMatter.FormatVersion))
        {
            issues.Add(new ValidationIssue(filePath, Rules.FrontMatterRequired, "formatVersion is missing."));
            return;
        }

        if (!string.Equals(frontMatter.FormatVersion, SupportedFormatVersion, StringComparison.Ordinal))
        {
            issues.Add(new ValidationIssue(
                filePath,
                Rules.FormatVersion,
                $"Unsupported formatVersion \"{frontMatter.FormatVersion}\"; this server supports {SupportedFormatVersion}."));
        }
    }

    private static void Require(string filePath, string field, string? value, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(new ValidationIssue(filePath, Rules.FrontMatterRequired, $"{field} is missing or empty."));
        }
    }

    private static void ValidateSummary(string filePath, string? summary, List<ValidationIssue> issues)
    {
        if (summary is { Length: > MaxSummaryLength })
        {
            issues.Add(new ValidationIssue(
                filePath,
                Rules.SummaryLength,
                $"summary is {summary.Length} characters, the limit is {MaxSummaryLength}."));
        }
    }

    private static void ValidateKeywords(string filePath, string? keywords, List<ValidationIssue> issues)
    {
        if (keywords is null)
        {
            return;
        }

        foreach (var c in keywords)
        {
            if (ForbiddenKeywordPunctuation.Contains(c, StringComparison.Ordinal))
            {
                issues.Add(new ValidationIssue(
                    filePath,
                    Rules.KeywordsPunctuation,
                    $"keywords contains the punctuation character '{c}'; it must be a space separated word list."));
                return;
            }
        }
    }

    private static void ValidateSectionTitles(
        string filePath,
        IReadOnlyList<MarkdownSection> sections,
        string[] allowed,
        List<ValidationIssue> issues)
    {
        if (sections.Count == 0)
        {
            issues.Add(new ValidationIssue(filePath, Rules.SectionTitles, "The file has no '##' section."));
            return;
        }

        foreach (var section in sections)
        {
            if (!allowed.Contains(section.Title, StringComparer.Ordinal))
            {
                issues.Add(new ValidationIssue(
                    filePath,
                    Rules.SectionTitles,
                    $"Unexpected section \"{section.Title}\"; allowed: {string.Join(", ", allowed)}.",
                    ValidationSeverity.Error,
                    section.Line));
            }
        }
    }

    private static void ValidateExamples(
        string filePath,
        IReadOnlyList<MarkdownSection> sections,
        List<ValidationIssue> issues)
    {
        foreach (var section in sections)
        {
            if (!section.Title.StartsWith("Example", StringComparison.Ordinal))
            {
                continue;
            }

            var blocks = MarkdownSections.FencedCodeBlocks(section.Body);
            if (blocks.Count != 1)
            {
                issues.Add(new ValidationIssue(
                    filePath,
                    Rules.ExampleCodeBlock,
                    $"Section \"{section.Title}\" must contain exactly one fenced code block, found {blocks.Count}.",
                    ValidationSeverity.Error,
                    section.Line));
                continue;
            }

            if (string.IsNullOrWhiteSpace(blocks[0].Language))
            {
                issues.Add(new ValidationIssue(
                    filePath,
                    Rules.ExampleCodeBlock,
                    $"The code block of \"{section.Title}\" does not declare its language.",
                    ValidationSeverity.Error,
                    section.Line));
            }
        }
    }

    private static void ValidateParameters(
        string filePath,
        IReadOnlyList<MarkdownSection> sections,
        List<ValidationIssue> issues)
    {
        foreach (var section in sections)
        {
            if (!string.Equals(section.Title, "Parameters", StringComparison.Ordinal))
            {
                continue;
            }

            var header = MarkdownSections.FirstTableHeader(section.Body);
            if (!header.SequenceEqual(ParameterColumns, StringComparer.OrdinalIgnoreCase))
            {
                issues.Add(new ValidationIssue(
                    filePath,
                    Rules.ParametersTable,
                    $"Parameters must be a table with the columns {string.Join(" | ", ParameterColumns)}; found {(header.Length == 0 ? "no table" : string.Join(" | ", header))}.",
                    ValidationSeverity.Error,
                    section.Line));
            }
        }
    }
}
