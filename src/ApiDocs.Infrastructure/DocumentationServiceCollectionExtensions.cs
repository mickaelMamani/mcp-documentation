using ApiDocs.Application.Ports;
using ApiDocs.Application.Retrieval;
using ApiDocs.Application.UseCases;
using ApiDocs.Infrastructure.Ingestion;
using ApiDocs.Infrastructure.Retrieval;
using ApiDocs.Infrastructure.Tokenization;
using Microsoft.Extensions.DependencyInjection;

namespace ApiDocs.Infrastructure;

/// <summary>Composition root of the documentation stack; the host only chooses the transport.</summary>
internal static class DocumentationServiceCollectionExtensions
{
    public static IServiceCollection AddApiDocumentation(
        this IServiceCollection services,
        string docsPath,
        LuceneOptions? luceneOptions = null,
        RankingOptions? rankingOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(docsPath);

        services.Configure<DocsOptions>(options => options.Path = docsPath);
        services.AddSingleton(luceneOptions ?? LuceneOptions.Default);
        services.AddSingleton(rankingOptions ?? RankingOptions.Default);

        services.AddSingleton<ITokenCounter, TiktokenTokenCounter>();
        services.AddSingleton<IChunker, MarkdigChunker>();
        services.AddSingleton<IDocumentSource, MarkdownRepositorySource>();
        services.AddSingleton<ILexicalIndexFactory, LuceneLexicalIndexFactory>();
        services.AddSingleton<IndexSnapshotProvider>();
        services.AddSingleton<IIndexSnapshotProvider>(sp => sp.GetRequiredService<IndexSnapshotProvider>());

        services.AddSingleton<ListDomainsUseCase>();
        services.AddSingleton<SearchEndpointsUseCase>();
        services.AddSingleton<GetEndpointUseCase>();
        services.AddSingleton<SearchDocsUseCase>();

        return services;
    }
}
