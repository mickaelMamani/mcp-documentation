using System.Globalization;
using System.Text;

namespace ApiDocs.Application.Retrieval;

/// <summary>
/// Neutral tokenisation used outside Lucene (use-case row scoring, operationId matching):
/// diacritics folded, split on non alphanumeric characters and on case changes, lowercased,
/// original run preserved. Mirrors the neutral analyzer of ARCHITECTURE §4.2 so that both sides
/// agree on what a token is.
/// </summary>
internal static class QueryTokens
{
    public static string[] Split(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var folded = FoldPreservingCase(text);
        var tokens = new List<string>(16);
        var start = -1;

        for (var i = 0; i <= folded.Length; i++)
        {
            var isPart = i < folded.Length && char.IsLetterOrDigit(folded[i]);
            if (isPart)
            {
                if (start < 0)
                {
                    start = i;
                }

                continue;
            }

            if (start >= 0)
            {
                AddRun(tokens, folded.AsSpan(start, i - start));
                start = -1;
            }
        }

        return tokens.ToArray();
    }

    /// <summary>Removes diacritics and lowercases, e.g. <c>volatilité</c> becomes <c>volatilite</c>.</summary>
    public static string Fold(string text) => FoldPreservingCase(text).ToLowerInvariant();

    private static string FoldPreservingCase(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Splits one alphanumeric run on case and letter/digit transitions, keeping the run itself.</summary>
    private static void AddRun(List<string> tokens, ReadOnlySpan<char> run)
    {
        var whole = run.ToString().ToLowerInvariant();
        var partStart = 0;
        var parts = 0;

        for (var i = 1; i <= run.Length; i++)
        {
            var boundary = i == run.Length
                || (char.IsUpper(run[i]) && !char.IsUpper(run[i - 1]))
                || (char.IsDigit(run[i]) != char.IsDigit(run[i - 1]));

            if (!boundary)
            {
                continue;
            }

            var part = run[partStart..i].ToString().ToLowerInvariant();
            if (part.Length > 0 && !string.Equals(part, whole, StringComparison.Ordinal))
            {
                tokens.Add(part);
            }

            parts++;
            partStart = i;
        }

        _ = parts;
        tokens.Add(whole);
    }

    /// <summary>Ratio of <paramref name="queryTokens"/> found in <paramref name="text"/>, in [0, 1].</summary>
    public static double Overlap(string[] queryTokens, string text)
    {
        if (queryTokens.Length == 0)
        {
            return 0d;
        }

        var haystack = Fold(text);
        var matched = 0;

        for (var i = 0; i < queryTokens.Length; i++)
        {
            var token = queryTokens[i];
            if (token.Length >= 4)
            {
                // Prefix tolerance: "folios" in the query still matches "folio" in the row.
                if (haystack.Contains(token, StringComparison.Ordinal) || ContainsPrefix(haystack, token))
                {
                    matched++;
                }
            }
            else if (ContainsWord(haystack, token))
            {
                matched++;
            }
        }

        return (double)matched / queryTokens.Length;
    }

    private static bool ContainsPrefix(string haystack, string token)
    {
        var stem = token.Length > 4 ? token[..^1] : token;
        return haystack.Contains(stem, StringComparison.Ordinal);
    }

    private static bool ContainsWord(string haystack, string token)
    {
        var index = haystack.IndexOf(token, StringComparison.Ordinal);
        while (index >= 0)
        {
            var beforeOk = index == 0 || !char.IsLetterOrDigit(haystack[index - 1]);
            var afterIndex = index + token.Length;
            var afterOk = afterIndex >= haystack.Length || !char.IsLetterOrDigit(haystack[afterIndex]);
            if (beforeOk && afterOk)
            {
                return true;
            }

            index = haystack.IndexOf(token, index + 1, StringComparison.Ordinal);
        }

        return false;
    }
}
