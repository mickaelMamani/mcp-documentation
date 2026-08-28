using ApiDocs.Application.Ports;
using ApiDocs.Domain;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Search.Similarities;
using Lucene.Net.Store;

namespace ApiDocs.Infrastructure.Retrieval;

/// <summary>
/// BM25 retrieval over the chunks of one snapshot. The index lives in a <see cref="RAMDirectory"/>
/// and is never shared between snapshots (ARCHITECTURE §6). Queries are built with query objects,
/// never with a text query parser, so a user question can never alter the query semantics.
/// </summary>
internal sealed class LuceneLexicalIndex : ILexicalIndex
{
    private const int MaxQueryTokens = 24;
    private const int PrefixMinimumLength = 4;
    private const int FuzzyMinimumLength = 6;

    private readonly RAMDirectory _directory;
    private readonly Analyzer _analyzer;
    private readonly DirectoryReader _reader;
    private readonly IndexSearcher _searcher;
    private readonly DocumentChunk[] _chunks;
    private readonly Dictionary<string, string> _operationIdsByFoldedName;

    private LuceneLexicalIndex(
        RAMDirectory directory,
        Analyzer analyzer,
        DirectoryReader reader,
        IndexSearcher searcher,
        DocumentChunk[] chunks,
        Dictionary<string, string> operationIdsByFoldedName)
    {
        _directory = directory;
        _analyzer = analyzer;
        _reader = reader;
        _searcher = searcher;
        _chunks = chunks;
        _operationIdsByFoldedName = operationIdsByFoldedName;
    }

    public static LuceneLexicalIndex Build(
        IReadOnlyList<DocumentChunk> chunks,
        IReadOnlyCollection<string> knownOperationIds,
        LuceneOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var analyzer = DocAnalyzers.CreatePerField(DocAnalyzers.LoadSynonyms(), options.FoldPlurals);
        var directory = new RAMDirectory();

        var config = new IndexWriterConfig(DocAnalyzers.Version, analyzer)
        {
            OpenMode = OpenMode.CREATE,
        };

        var stored = new DocumentChunk[chunks.Count];
        using (var writer = new IndexWriter(directory, config))
        {
            for (var i = 0; i < chunks.Count; i++)
            {
                stored[i] = chunks[i];
                writer.AddDocument(ToDocument(chunks[i], i));
            }

            writer.Commit();
        }

        var reader = DirectoryReader.Open(directory);
        var searcher = new IndexSearcher(reader)
        {
            Similarity = new BM25Similarity(options.Bm25K1, options.Bm25B),
        };

        var byFoldedName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var operationId in knownOperationIds)
        {
            byFoldedName[operationId.ToLowerInvariant()] = operationId;
        }

        return new LuceneLexicalIndex(directory, analyzer, reader, searcher, stored, byFoldedName);
    }

    private static Document ToDocument(DocumentChunk chunk, int ordinal) => new()
    {
        new StringField(DocFields.Id, chunk.Id.Value, Field.Store.YES),
        new StoredField(DocFields.Ordinal, ordinal),
        new StringField(DocFields.Kind, chunk.Kind.ToString(), Field.Store.NO),
        new StringField(DocFields.OperationId, chunk.Operation?.Value ?? string.Empty, Field.Store.NO),
        new StringField(DocFields.Domain, chunk.DomainId, Field.Store.NO),
        new StringField(DocFields.Language, chunk.Language.ToId() ?? string.Empty, Field.Store.NO),
        new StringField(DocFields.Deprecated, chunk.Deprecated ? "true" : "false", Field.Store.NO),
        new TextField(DocFields.Title, chunk.Title, Field.Store.NO),
        new TextField(DocFields.Breadcrumb, chunk.Breadcrumb.ToString(), Field.Store.NO),
        new TextField(DocFields.Summary, chunk.Summary, Field.Store.NO),
        new TextField(DocFields.Keywords, chunk.Keywords, Field.Store.NO),
        new TextField(DocFields.Body, chunk.Body, Field.Store.NO),
    };

    public IReadOnlyList<SearchHit> Search(string query, SearchFilters filters, int k)
    {
        ArgumentNullException.ThrowIfNull(filters);

        if (string.IsNullOrWhiteSpace(query) || _chunks.Length == 0)
        {
            return [];
        }

        var should = BuildShouldClauses(query);
        if (should is null)
        {
            return [];
        }

        var root = new BooleanQuery();
        root.Add(should, Occur.MUST);
        AddFilters(root, filters);

        var top = _searcher.Search(root, Math.Max(1, k));
        var hits = new List<SearchHit>(top.ScoreDocs.Length);

        for (var i = 0; i < top.ScoreDocs.Length; i++)
        {
            var scoreDoc = top.ScoreDocs[i];
            var ordinal = _searcher.Doc(scoreDoc.Doc).GetField(DocFields.Ordinal)?.GetInt32Value();
            if (ordinal is not { } index || index < 0 || index >= _chunks.Length)
            {
                continue;
            }

            hits.Add(new SearchHit(_chunks[index], scoreDoc.Score));
        }

        return hits;
    }

    private BooleanQuery? BuildShouldClauses(string query)
    {
        var surfaceTokens = SurfaceTokens(query);
        if (surfaceTokens.Count == 0)
        {
            return null;
        }

        var should = new BooleanQuery { MinimumNumberShouldMatch = 1 };
        var clauses = 0;

        foreach (var token in surfaceTokens)
        {
            var folded = Application.Retrieval.QueryTokens.Fold(token);

            // Prefix and fuzzy clauses bypass the analysis chain, so they need the token in the same
            // surface form the non stemmed fields were indexed with, plural folding included.
            var literalTerms = AnalyzeToTerms(DocFields.Keywords, token);
            var literal = literalTerms.Count > 0 ? literalTerms[0] : folded;

            // The caller named the endpoint: nothing outranks that.
            if (_operationIdsByFoldedName.TryGetValue(folded, out var exactOperationId))
            {
                should.Add(
                    new TermQuery(new Term(DocFields.OperationId, exactOperationId)) { Boost = DocFields.ExactOperationIdBoost },
                    Occur.SHOULD);
                clauses++;
            }

            foreach (var (field, boost) in DocFields.SearchableFields)
            {
                foreach (var term in AnalyzeToTerms(field, token))
                {
                    should.Add(new TermQuery(new Term(field, term)) { Boost = boost }, Occur.SHOULD);
                    clauses++;
                }
            }

            if (literal.Length >= PrefixMinimumLength)
            {
                foreach (var field in DocFields.LiteralFields)
                {
                    should.Add(new PrefixQuery(new Term(field, literal)) { Boost = 0.5f }, Occur.SHOULD);
                    clauses++;
                }
            }

            if (literal.Length >= FuzzyMinimumLength)
            {
                should.Add(new FuzzyQuery(new Term(DocFields.Title, literal), maxEdits: 1) { Boost = 1f }, Occur.SHOULD);
                should.Add(new FuzzyQuery(new Term(DocFields.Keywords, literal), maxEdits: 1) { Boost = 1f }, Occur.SHOULD);
                clauses += 2;
            }
        }

        return clauses == 0 ? null : should;
    }

    private static void AddFilters(BooleanQuery root, SearchFilters filters)
    {
        if (!string.IsNullOrWhiteSpace(filters.DomainId))
        {
            root.Add(new TermQuery(new Term(DocFields.Domain, filters.DomainId)), Occur.MUST);
        }

        if (!string.IsNullOrWhiteSpace(filters.OperationId))
        {
            root.Add(new TermQuery(new Term(DocFields.OperationId, filters.OperationId)), Occur.MUST);
        }

        if (filters.Language is { } language && language != CodeLanguage.None)
        {
            root.Add(new TermQuery(new Term(DocFields.Language, language.ToId())), Occur.MUST);
        }

        if (filters.Kinds is { Length: > 0 } kinds)
        {
            var kindQuery = new BooleanQuery { MinimumNumberShouldMatch = 1 };
            for (var i = 0; i < kinds.Length; i++)
            {
                kindQuery.Add(new TermQuery(new Term(DocFields.Kind, kinds[i].ToString())), Occur.SHOULD);
            }

            root.Add(kindQuery, Occur.MUST);
        }

        if (!filters.IncludeDeprecated)
        {
            root.Add(new TermQuery(new Term(DocFields.Deprecated, "false")), Occur.MUST);
        }
    }

    /// <summary>Runs one query token through the analysis chain of a field, so that a query term is
    /// always in the same form as the indexed terms (stemmed, folded, expanded by synonyms).</summary>
    private List<string> AnalyzeToTerms(string field, string text)
    {
        var terms = new List<string>(4);
        using var stream = _analyzer.GetTokenStream(field, text);
        var termAttribute = stream.AddAttribute<ICharTermAttribute>();

        stream.Reset();
        while (stream.IncrementToken())
        {
            var term = termAttribute.ToString();
            if (term.Length > 0 && !terms.Contains(term, StringComparer.Ordinal))
            {
                terms.Add(term);
            }
        }

        stream.End();
        return terms;
    }

    /// <summary>Splits the raw query into words, keeping their case so that camelCase can be split
    /// by the field analyzers.</summary>
    private static List<string> SurfaceTokens(string query)
    {
        var tokens = new List<string>(8);
        var start = -1;

        for (var i = 0; i <= query.Length && tokens.Count < MaxQueryTokens; i++)
        {
            var isPart = i < query.Length && (char.IsLetterOrDigit(query[i]) || query[i] == '_');
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
                tokens.Add(query[start..i]);
                start = -1;
            }
        }

        return tokens;
    }

    public IReadOnlyList<string> SuggestOperationIds(string operationId, int limit)
    {
        if (string.IsNullOrWhiteSpace(operationId) || limit <= 0)
        {
            return [];
        }

        var needle = operationId.ToLowerInvariant();
        var scored = new List<(string OperationId, double Distance)>(_operationIdsByFoldedName.Count);

        foreach (var pair in _operationIdsByFoldedName)
        {
            scored.Add((pair.Value, NameDistance(needle, pair.Key)));
        }

        scored.Sort(static (a, b) =>
        {
            var byDistance = a.Distance.CompareTo(b.Distance);
            return byDistance != 0 ? byDistance : string.CompareOrdinal(a.OperationId, b.OperationId);
        });

        var threshold = Math.Max(3d, needle.Length / 2d);
        var suggestions = new List<string>(limit);
        for (var i = 0; i < scored.Count && suggestions.Count < limit; i++)
        {
            // Beyond half the length of the name the "did you mean" is noise.
            if (scored[i].Distance <= threshold)
            {
                suggestions.Add(scored[i].OperationId);
            }
        }

        return suggestions;
    }

    /// <summary>
    /// A truncated or extended name is a much better guess than a name at the same edit distance
    /// that shares no prefix: "GetFolio" is "GetFolioById" cut short, not a misspelling of
    /// "CreateFolio".
    /// </summary>
    private static double NameDistance(string needle, string candidate)
    {
        if (candidate.StartsWith(needle, StringComparison.Ordinal))
        {
            return (candidate.Length - needle.Length) * 0.25d;
        }

        if (needle.StartsWith(candidate, StringComparison.Ordinal))
        {
            return (needle.Length - candidate.Length) * 0.25d;
        }

        return EditDistance(needle, candidate);
    }

    private static int EditDistance(string a, string b)
    {
        if (a.Length == 0)
        {
            return b.Length;
        }

        if (b.Length == 0)
        {
            return a.Length;
        }

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    public void Dispose()
    {
        _reader.Dispose();
        _analyzer.Dispose();
        _directory.Dispose();
    }
}

internal sealed class LuceneLexicalIndexFactory(LuceneOptions options) : ILexicalIndexFactory
{
    public ILexicalIndex Build(IReadOnlyList<DocumentChunk> chunks, IReadOnlyCollection<string> knownOperationIds) =>
        LuceneLexicalIndex.Build(chunks, knownOperationIds, options);
}
