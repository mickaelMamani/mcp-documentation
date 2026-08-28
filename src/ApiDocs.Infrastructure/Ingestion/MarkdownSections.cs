using Markdig;
using Markdig.Syntax;

namespace ApiDocs.Infrastructure.Ingestion;

/// <summary>One <c>##</c> section: its title, its raw Markdown body and the line the title is on.</summary>
internal sealed record MarkdownSection(string Title, string Body, int Line);

/// <summary>
/// Splits a Markdown body on its level 2 headings, which is the chunking rule of DOC-FORMAT §4.1.
/// The section body is the raw Markdown between two headings, so tables and fenced code blocks are
/// handed to the LLM exactly as the generator wrote them.
/// </summary>
internal static class MarkdownSections
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .Build();

    public static IReadOnlyList<MarkdownSection> Split(string body, int bodyStartLine)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        var document = Markdown.Parse(body, Pipeline);
        var headings = new List<HeadingBlock>();

        foreach (var block in document)
        {
            if (block is HeadingBlock { Level: 2 } heading)
            {
                headings.Add(heading);
            }
        }

        if (headings.Count == 0)
        {
            return [];
        }

        var sections = new List<MarkdownSection>(headings.Count);
        for (var i = 0; i < headings.Count; i++)
        {
            var heading = headings[i];
            var titleLine = body.Substring(heading.Span.Start, heading.Span.Length);
            var title = titleLine.TrimStart('#').Trim();

            var contentStart = Math.Min(heading.Span.End + 1, body.Length);
            var contentEnd = i + 1 < headings.Count ? headings[i + 1].Span.Start : body.Length;
            var content = contentEnd > contentStart ? body[contentStart..contentEnd] : string.Empty;

            sections.Add(new MarkdownSection(title, content.Trim('\n'), bodyStartLine + heading.Line));
        }

        return sections;
    }

    /// <summary>Fenced code blocks of one section, with the language declared on the fence.</summary>
    public static IReadOnlyList<(string Language, string Code)> FencedCodeBlocks(string sectionBody)
    {
        if (string.IsNullOrWhiteSpace(sectionBody))
        {
            return [];
        }

        var document = Markdown.Parse(sectionBody, Pipeline);
        var blocks = new List<(string, string)>();

        foreach (var block in document.Descendants<FencedCodeBlock>())
        {
            blocks.Add((block.Info ?? string.Empty, block.Lines.ToString()));
        }

        return blocks;
    }

    /// <summary>Header cells of the first pipe table of a section, trimmed.</summary>
    public static string[] FirstTableHeader(string sectionBody)
    {
        using var reader = new StringReader(sectionBody);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith('|'))
            {
                continue;
            }

            var cells = trimmed.Trim('|').Split('|');
            for (var i = 0; i < cells.Length; i++)
            {
                cells[i] = cells[i].Trim();
            }

            return cells;
        }

        return [];
    }

    /// <summary>Data rows of the first pipe table of a section, header and separator excluded.</summary>
    public static IReadOnlyList<string[]> TableRows(string sectionBody)
    {
        var rows = new List<string[]>();
        var seenHeader = false;
        var seenSeparator = false;

        using var reader = new StringReader(sectionBody);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith('|'))
            {
                if (seenSeparator)
                {
                    break;
                }

                continue;
            }

            if (!seenHeader)
            {
                seenHeader = true;
                continue;
            }

            if (!seenSeparator)
            {
                seenSeparator = true;
                continue;
            }

            var cells = trimmed.Trim('|').Split('|');
            for (var i = 0; i < cells.Length; i++)
            {
                cells[i] = cells[i].Trim();
            }

            rows.Add(cells);
        }

        return rows;
    }
}
