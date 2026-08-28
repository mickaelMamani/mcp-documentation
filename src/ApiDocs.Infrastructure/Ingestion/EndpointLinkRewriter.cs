using System.Text;

namespace ApiDocs.Infrastructure.Ingestion;

/// <summary>
/// ARCHITECTURE §5.3: an inline <c>`GetSurface`</c> reference becomes a link to the
/// <c>endpoint://GetSurface</c> resource, so a client that reads resources can follow it.
/// Fenced code blocks are left untouched: an example must stay copy-pasteable.
/// </summary>
internal static class EndpointLinkRewriter
{
    public static string Rewrite(string markdown, IReadOnlyCollection<string> knownOperationIds)
    {
        if (string.IsNullOrEmpty(markdown) || knownOperationIds.Count == 0)
        {
            return markdown;
        }

        var builder = new StringBuilder(markdown.Length + 64);
        var inFence = false;

        using var reader = new StringReader(markdown);
        string? line;
        var first = true;

        while ((line = reader.ReadLine()) is not null)
        {
            if (!first)
            {
                builder.Append('\n');
            }

            first = false;

            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                builder.Append(line);
                continue;
            }

            builder.Append(inFence ? line : RewriteLine(line, knownOperationIds));
        }

        return builder.ToString();
    }

    private static string RewriteLine(string line, IReadOnlyCollection<string> knownOperationIds)
    {
        if (!line.Contains('`', StringComparison.Ordinal))
        {
            return line;
        }

        var builder = new StringBuilder(line.Length + 32);
        var index = 0;

        while (index < line.Length)
        {
            var open = line.IndexOf('`', index);
            if (open < 0)
            {
                builder.Append(line, index, line.Length - index);
                break;
            }

            var close = line.IndexOf('`', open + 1);
            if (close < 0)
            {
                builder.Append(line, index, line.Length - index);
                break;
            }

            builder.Append(line, index, open - index);
            var content = line[(open + 1)..close];

            // A reference already inside a link target must not be rewritten twice.
            var alreadyLinked = close + 1 < line.Length && line[close + 1] == ']';

            if (!alreadyLinked && knownOperationIds.Contains(content))
            {
                builder.Append('[').Append('`').Append(content).Append('`').Append(']')
                    .Append("(endpoint://").Append(content).Append(')');
            }
            else
            {
                builder.Append('`').Append(content).Append('`');
            }

            index = close + 1;
        }

        return builder.ToString();
    }
}
