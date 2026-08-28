using ApiDocs.Application.Ports;
using ApiDocs.Application.Retrieval;
using ApiDocs.Application.UseCases;
using ApiDocs.Infrastructure.Ingestion;
using ApiDocs.Infrastructure.Retrieval;
using ApiDocs.Infrastructure.Tokenization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ApiDocs.Infrastructure;

/// <summary>Composition root of the documentation stack; the host only chooses the transport.</summary>
internal static class DocumentationServiceCollectionExtensions
{
    /// <summary>
    /// Documentation read from a folder already present on the machine: the developer loop and the
    /// stdio host, where the corpus sits next to the code.
    /// </summary>
    public static IServiceCollection AddApiDocumentation(
        this IServiceCollection services,
        string docsPath,
        LuceneOptions? luceneOptions = null,
        RankingOptions? rankingOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(docsPath);

        services.Configure<DocsOptions>(options =>
        {
            options.Source = DocsSourceKind.Folder;
            options.Path = docsPath;
        });

        return AddDocumentationCore(services, luceneOptions, rankingOptions);
    }

    /// <summary>
    /// Documentation described by the <c>Docs</c> configuration section: the production host, where
    /// the corpus is centralised and the server fetches it from Git (ADR #23).
    /// </summary>
    public static IServiceCollection AddApiDocumentation(
        this IServiceCollection services,
        IConfiguration configuration,
        LuceneOptions? luceneOptions = null,
        RankingOptions? rankingOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<DocsOptions>(configuration.GetSection(DocsOptions.SectionName));
        services.PostConfigure<DocsOptions>(options =>
        {
            if (options.Source == DocsSourceKind.Git)
            {
                // The corpus is the subdirectory of the working copy the server checks out; from
                // there on it is an ordinary documentation folder.
                options.Path = Path.Combine(options.Git.WorkingCopy, options.Git.Subdirectory);
            }
        });

        return AddDocumentationCore(services, luceneOptions, rankingOptions);
    }

    private static IServiceCollection AddDocumentationCore(
        IServiceCollection services,
        LuceneOptions? luceneOptions,
        RankingOptions? rankingOptions)
    {
        services.AddSingleton(luceneOptions ?? LuceneOptions.Default);
        services.AddSingleton(rankingOptions ?? RankingOptions.Default);

        services.AddSingleton<ITokenCounter, TiktokenTokenCounter>();
        services.AddSingleton<IChunker, MarkdigChunker>();
        services.AddSingleton<MarkdownRepositorySource>();
        services.AddSingleton<GitDocumentSource>();
        services.AddSingleton<IDocumentSource>(sp =>
            sp.GetRequiredService<IOptions<DocsOptions>>().Value.Source == DocsSourceKind.Git
                ? sp.GetRequiredService<GitDocumentSource>()
                : sp.GetRequiredService<MarkdownRepositorySource>());
        services.AddSingleton<IDocsRevision>(sp =>
            sp.GetRequiredService<IDocumentSource>() as IDocsRevision ?? UnknownDocsRevision.Instance);
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
