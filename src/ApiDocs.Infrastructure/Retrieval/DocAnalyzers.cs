using System.Reflection;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.En;
using Lucene.Net.Analysis.Miscellaneous;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Analysis.Synonym;
using Lucene.Net.Analysis.Util;
using Lucene.Net.Util;

namespace ApiDocs.Infrastructure.Retrieval;

/// <summary>
/// The per-field analysis chains of ARCHITECTURE §4.2.
/// <list type="bullet">
/// <item><description><c>Title</c>, <c>Breadcrumb</c>: word delimiter, so <c>GetFolioById</c> is
/// also found by "folio" and by "get folio", with the original token kept.</description></item>
/// <item><description><c>Summary</c>, <c>Body</c>: English stemming plus the business synonyms.</description></item>
/// <item><description><c>Keywords</c>: neutral, no stemming — the field is bilingual.</description></item>
/// </list>
/// </summary>
internal static class DocAnalyzers
{
    public const LuceneVersion Version = LuceneVersion.LUCENE_48;

    private const WordDelimiterFlags DelimiterFlags =
        WordDelimiterFlags.GENERATE_WORD_PARTS
        | WordDelimiterFlags.GENERATE_NUMBER_PARTS
        | WordDelimiterFlags.SPLIT_ON_CASE_CHANGE
        | WordDelimiterFlags.SPLIT_ON_NUMERICS
        | WordDelimiterFlags.CATENATE_WORDS
        | WordDelimiterFlags.PRESERVE_ORIGINAL;

    public static Analyzer CreatePerField(SynonymMap synonyms, bool foldPlurals = false)
    {
        var identifier = CreateIdentifierAnalyzer(foldPlurals);
        var neutral = CreateNeutralAnalyzer(foldPlurals);
        var english = CreateEnglishAnalyzer(synonyms);

        var perField = new Dictionary<string, Analyzer>(StringComparer.Ordinal)
        {
            [DocFields.Title] = identifier,
            [DocFields.Breadcrumb] = identifier,
            [DocFields.Keywords] = neutral,
            [DocFields.Summary] = english,
            [DocFields.Body] = english,
        };

        return new PerFieldAnalyzerWrapper(neutral, perField);
    }

    /// <summary>Word delimiter chain used for <c>Title</c> and <c>Breadcrumb</c>.</summary>
    public static Analyzer CreateIdentifierAnalyzer(bool foldPlurals = false) => Analyzer.NewAnonymous((_, reader) =>
    {
        Tokenizer source = new WhitespaceTokenizer(Version, reader);
        TokenStream stream = new WordDelimiterFilter(Version, source, DelimiterFlags, null);
        stream = new LowerCaseFilter(Version, stream);
        stream = new ASCIIFoldingFilter(stream);
        if (foldPlurals)
        {
            stream = new EnglishMinimalStemFilter(stream);
        }

        return new TokenStreamComponents(source, stream);
    });

    /// <summary>Neutral chain used for <c>Keywords</c>: lowercase and folding, never stemming.</summary>
    public static Analyzer CreateNeutralAnalyzer(bool foldPlurals = false) => Analyzer.NewAnonymous((_, reader) =>
    {
        Tokenizer source = new WhitespaceTokenizer(Version, reader);
        TokenStream stream = new LowerCaseFilter(Version, source);
        stream = new ASCIIFoldingFilter(stream);
        if (foldPlurals)
        {
            // Plural mark only: the field stays bilingual, so no English stemmer here.
            stream = new EnglishMinimalStemFilter(stream);
        }

        return new TokenStreamComponents(source, stream);
    });

    /// <summary>English chain with the business synonyms applied before stemming.</summary>
    public static Analyzer CreateEnglishAnalyzer(SynonymMap synonyms) => Analyzer.NewAnonymous((_, reader) =>
    {
        Tokenizer source = new StandardTokenizer(Version, reader);
        TokenStream stream = new StandardFilter(Version, source);
        stream = new EnglishPossessiveFilter(Version, stream);
        stream = new LowerCaseFilter(Version, stream);
        stream = new ASCIIFoldingFilter(stream);
        stream = new StopFilter(Version, stream, EnglishAnalyzer.DefaultStopSet);
        stream = new SynonymFilter(stream, synonyms, ignoreCase: true);
        stream = new PorterStemFilter(stream);
        return new TokenStreamComponents(source, stream);
    });

    /// <summary>Loads the versioned <c>synonyms.txt</c> embedded next to this class.</summary>
    public static SynonymMap LoadSynonyms()
    {
        var assembly = typeof(DocAnalyzers).GetTypeInfo().Assembly;
        var resource = Array.Find(
            assembly.GetManifestResourceNames(),
            name => name.EndsWith("synonyms.txt", StringComparison.Ordinal));

        var builder = new SynonymMap.Builder(dedup: true);
        if (resource is null)
        {
            return builder.Build();
        }

        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var words = trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (words.Length < 2)
            {
                continue;
            }

            foreach (var from in words)
            {
                foreach (var to in words)
                {
                    if (!string.Equals(from, to, StringComparison.Ordinal))
                    {
                        builder.Add(new CharsRef(from), new CharsRef(to), includeOrig: true);
                    }
                }
            }
        }

        return builder.Build();
    }
}
