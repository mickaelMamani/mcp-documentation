using ApiDocs.Application;
using ApiDocs.Application.Ports;
using ApiDocs.Application.UseCases;
using ApiDocs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ApiDocs.Tests;

/// <summary>
/// Builds the real <c>docs/</c> corpus once for the whole test run: the tests measure the server
/// against the documentation it actually ships with.
/// </summary>
public sealed class DocumentationFixture : IDisposable
{
    private readonly ServiceProvider _services;

    public DocumentationFixture()
    {
        RepositoryRoot = FindRepositoryRoot();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddApiDocumentation(Path.Combine(RepositoryRoot, "docs"));

        _services = services.BuildServiceProvider();

        Report = _services.GetRequiredService<IndexSnapshotProvider>()
            .RebuildAsync()
            .GetAwaiter()
            .GetResult();
    }

    public string RepositoryRoot { get; }

    internal RebuildReport Report { get; }

    internal IndexSnapshot Snapshot => _services.GetRequiredService<IIndexSnapshotProvider>().Current;

    internal ListDomainsUseCase ListDomains => _services.GetRequiredService<ListDomainsUseCase>();

    internal SearchEndpointsUseCase SearchEndpoints => _services.GetRequiredService<SearchEndpointsUseCase>();

    internal GetEndpointUseCase GetEndpoint => _services.GetRequiredService<GetEndpointUseCase>();

    internal SearchDocsUseCase SearchDocs => _services.GetRequiredService<SearchDocsUseCase>();

    internal ITokenCounter TokenCounter => _services.GetRequiredService<ITokenCounter>();

    public string BenchmarkFile => Path.Combine(AppContext.BaseDirectory, "benchmark", "questions.yaml");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "docs", "manifest.json")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root holding docs/manifest.json.");
    }

    public void Dispose() => _services.Dispose();
}

[CollectionDefinition(Name)]
public sealed class DocumentationCollection : ICollectionFixture<DocumentationFixture>
{
    public const string Name = "documentation";
}
