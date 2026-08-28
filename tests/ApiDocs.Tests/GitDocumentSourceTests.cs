using System.Diagnostics;
using ApiDocs.Application.Ports;
using ApiDocs.Infrastructure;
using ApiDocs.Infrastructure.Ingestion;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ApiDocs.Tests;

/// <summary>
/// ADR #23: in production the corpus is centralised and the server checks it out itself. These
/// tests drive the real <c>git</c> binary against a local repository — the wiring they exercise
/// (configuration binding, source selection, checkout, revision) is exactly what the HTTP host runs.
/// They require Git on the PATH, which the production host requires too.
/// </summary>
[Collection(DocumentationCollection.Name)]
public sealed class GitDocumentSourceTests : IDisposable
{
    private readonly DocumentationFixture _fixture;
    private readonly string _root;
    private readonly string _origin;
    private readonly string _checkout;

    public GitDocumentSourceTests(DocumentationFixture fixture)
    {
        _fixture = fixture;
        _root = Path.Combine(Path.GetTempPath(), "apidocs-git-" + Guid.NewGuid().ToString("n"));
        _origin = Path.Combine(_root, "origin");
        _checkout = Path.Combine(_root, "checkout");

        Directory.CreateDirectory(_origin);
        Git("init", "--initial-branch=main", "--quiet");
        Git("config", "user.email", "tests@example.internal");
        Git("config", "user.name", "ApiDocs tests");
        Copy(Path.Combine(_fixture.RepositoryRoot, "docs"), Path.Combine(_origin, "docs"));
        Git("add", "--all");
        Git("commit", "--quiet", "-m", "Publish the documentation");
    }

    [Fact]
    public async Task Serves_the_corpus_checked_out_from_the_repository()
    {
        using var services = Services(GitSettings());

        var set = await services.GetRequiredService<IDocumentSource>().LoadAsync();

        set.Endpoints.Should().HaveCount(_fixture.Snapshot.Endpoints.Length);
        set.Domains.Should().HaveCount(_fixture.Snapshot.Domains.Length);
        services.GetRequiredService<IDocsRevision>().Commit.Should().MatchRegex("^[0-9a-f]{40}$");
    }

    [Fact]
    public async Task Picks_up_the_new_revision_on_the_next_load()
    {
        using var services = Services(GitSettings());
        var source = services.GetRequiredService<IDocumentSource>();
        var revision = services.GetRequiredService<IDocsRevision>();

        var before = await source.LoadAsync();
        var published = revision.Commit;
        before.Platform.RawBody.Should().NotContain("Sentinel section");

        File.AppendAllText(
            Path.Combine(_origin, "docs", "_platform.md"),
            Environment.NewLine + "Sentinel section added by the tests." + Environment.NewLine);
        Git("commit", "--quiet", "--all", "-m", "Update the platform overview");

        var after = await source.LoadAsync();

        revision.Commit.Should().NotBe(published);
        after.Platform.RawBody.Should().Contain("Sentinel section");
    }

    [Fact]
    public void Keeps_the_plain_folder_reader_when_the_source_is_a_folder()
    {
        using var services = Services(new Dictionary<string, string?>
        {
            ["Docs:Source"] = "Folder",
            ["Docs:Path"] = Path.Combine(_fixture.RepositoryRoot, "docs"),
        });

        services.GetRequiredService<IDocumentSource>().Should().BeOfType<MarkdownRepositorySource>();
        services.GetRequiredService<IDocsRevision>().Commit.Should().BeNull();
    }

    /// <summary>A misconfigured deployment must fail at startup, not on the first client query.</summary>
    [Fact]
    public void Fails_fast_when_the_repository_url_is_missing()
    {
        var settings = GitSettings();
        settings["Docs:Git:RepositoryUrl"] = string.Empty;

        using var services = Services(settings);

        var resolve = () => services.GetRequiredService<IDocumentSource>();

        resolve.Should().Throw<InvalidOperationException>().WithMessage("*RepositoryUrl*");
    }

    private Dictionary<string, string?> GitSettings() => new()
    {
        ["Docs:Source"] = "Git",
        ["Docs:Git:RepositoryUrl"] = new Uri(_origin).AbsoluteUri,
        ["Docs:Git:Reference"] = "main",
        ["Docs:Git:Subdirectory"] = "docs",
        ["Docs:Git:WorkingCopy"] = _checkout,
        ["Docs:Git:Depth"] = "1",
    };

    private static ServiceProvider Services(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddApiDocumentation(configuration);
        return services.BuildServiceProvider();
    }

    private void Git(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = _origin,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!; // git is a prerequisite of these tests
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        process.ExitCode.Should().Be(0, "git {0} failed: {1}", string.Join(' ', arguments), error);
    }

    private static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            Copy(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        // Git marks the objects it writes read-only; clear that before deleting the sandbox.
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_root, recursive: true);
    }
}
