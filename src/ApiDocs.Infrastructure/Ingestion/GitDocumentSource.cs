using System.Diagnostics;
using System.Globalization;
using ApiDocs.Application.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApiDocs.Infrastructure.Ingestion;

/// <summary>Where the corpus comes from (ARCHITECTURE §6.1).</summary>
internal enum DocsSourceKind
{
    /// <summary>A folder already present on the machine: the developer loop and the stdio host.</summary>
    Folder = 0,

    /// <summary>A Git repository the server checks out itself: the production host.</summary>
    Git = 1,
}

internal sealed class GitDocsOptions
{
    /// <summary>Clone URL of the repository holding the generated documentation.</summary>
    public string RepositoryUrl { get; set; } = string.Empty;

    /// <summary>Branch or tag to serve. A commit SHA only works if the server allows fetching one.</summary>
    public string Reference { get; set; } = "main";

    /// <summary>Folder of the repository holding <c>manifest.json</c>; empty when it is the root.</summary>
    public string Subdirectory { get; set; } = "docs";

    /// <summary>Local working copy, kept between restarts so a restart only fetches the delta.</summary>
    public string WorkingCopy { get; set; } = "docs-checkout";

    /// <summary>Fetch depth; 0 fetches the whole history. One commit is all the server reads.</summary>
    public int Depth { get; set; } = 1;

    /// <summary>Budget for a single git invocation. Exceeding it fails the startup, it never hangs.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Git executable, resolved through PATH by default.</summary>
    public string Executable { get; set; } = "git";
}

/// <summary>Revision of the corpus currently served, surfaced by the health endpoint.</summary>
internal interface IDocsRevision
{
    /// <summary>Commit actually checked out, or <see langword="null"/> for a plain folder.</summary>
    string? Commit { get; }
}

internal sealed class UnknownDocsRevision : IDocsRevision
{
    public static readonly UnknownDocsRevision Instance = new();

    private UnknownDocsRevision()
    {
    }

    public string? Commit => null;
}

/// <summary>
/// Brings the documentation to the server before every index build: fetch the configured reference,
/// check it out, then hand the working copy over to <see cref="MarkdownRepositorySource"/>
/// (ADR #23). The corpus stays a folder of Markdown files, so the ingestion, the validator and the
/// chunker are unchanged — only its provenance differs from the developer loop.
/// </summary>
internal sealed class GitDocumentSource : IDocumentSource, IDocsRevision
{
    private const string HeadRef = "FETCH_HEAD";

    private readonly GitDocsOptions _git;
    private readonly MarkdownRepositorySource _inner;
    private readonly ILogger<GitDocumentSource> _logger;
    private string? _commit;

    public GitDocumentSource(
        IOptions<DocsOptions> options,
        MarkdownRepositorySource inner,
        ILogger<GitDocumentSource> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _git = options.Value.Git;
        _inner = inner;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_git.RepositoryUrl))
        {
            throw new InvalidOperationException(
                $"{DocsOptions.SectionName}:Git:{nameof(GitDocsOptions.RepositoryUrl)} is required when the documentation source is Git.");
        }

        if (string.IsNullOrWhiteSpace(_git.Reference))
        {
            throw new InvalidOperationException(
                $"{DocsOptions.SectionName}:Git:{nameof(GitDocsOptions.Reference)} is required when the documentation source is Git.");
        }
    }

    public string? Commit => Volatile.Read(ref _commit);

    public async Task<DocumentSet> LoadAsync(CancellationToken cancellationToken = default)
    {
        var commit = await CheckoutAsync(cancellationToken).ConfigureAwait(false);
        var set = await _inner.LoadAsync(cancellationToken).ConfigureAwait(false);

        // Published only after the corpus at this commit has been read: a reload that fails must
        // leave both the served snapshot and the revision reported by /health on the previous
        // state (ADR #28).
        Volatile.Write(ref _commit, commit);
        return set;
    }

    /// <summary>
    /// Idempotent: the first run initialises the working copy, the next ones only move it to the
    /// configured reference. A failure throws, so <see cref="IndexSnapshotProvider"/> keeps the
    /// snapshot it already serves instead of publishing a truncated corpus (ARCHITECTURE §6).
    /// </summary>
    private async Task<string> CheckoutAsync(CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(_git.WorkingCopy);
        Directory.CreateDirectory(root);

        if (Directory.Exists(Path.Combine(root, ".git")))
        {
            await RunAsync(root, cancellationToken, "remote", "set-url", "origin", _git.RepositoryUrl).ConfigureAwait(false);
        }
        else
        {
            await RunAsync(root, cancellationToken, "init", "--quiet").ConfigureAwait(false);
            await RunAsync(root, cancellationToken, "remote", "add", "origin", _git.RepositoryUrl).ConfigureAwait(false);
        }

        string[] fetch = _git.Depth > 0
            ? ["fetch", "--force", "--depth", _git.Depth.ToString(CultureInfo.InvariantCulture), "origin", _git.Reference]
            : ["fetch", "--force", "origin", _git.Reference];

        await RunAsync(root, cancellationToken, fetch).ConfigureAwait(false);
        await RunAsync(root, cancellationToken, "checkout", "--force", "--detach", HeadRef).ConfigureAwait(false);

        var commit = (await RunAsync(root, cancellationToken, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
        Log.DocsCheckedOut(_logger, Redact(_git.RepositoryUrl), _git.Reference, commit);
        return commit;
    }

    private async Task<string> RunAsync(string workingDirectory, CancellationToken cancellationToken, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(_git.Executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // A credential prompt on a headless server would hang the startup forever.
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_git.Timeout);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start \"{_git.Executable}\"; is Git installed on the server?");

        var standardOutput = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var standardError = process.StandardError.ReadToEndAsync(deadline.Token);

        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Kill(process);
            throw new TimeoutException(
                $"\"git {Redact(string.Join(' ', arguments))}\" did not complete within {_git.Timeout}.");
        }

        var output = await standardOutput.ConfigureAwait(false);
        var error = await standardError.ConfigureAwait(false);

        return process.ExitCode == 0
            ? output
            : throw new InvalidOperationException(
                $"\"git {Redact(string.Join(' ', arguments))}\" failed with exit code {process.ExitCode}: {Redact(error).Trim()}");
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process exited between the deadline and the kill; nothing left to stop.
        }
    }

    /// <summary>
    /// Keeps a token embedded in the clone URL out of the logs and out of the exception messages,
    /// both of which end up in the platform's log collector.
    /// </summary>
    private string Redact(string text)
    {
        if (!Uri.TryCreate(_git.RepositoryUrl, UriKind.Absolute, out var uri) || uri.UserInfo.Length == 0)
        {
            return text;
        }

        var masked = new UriBuilder(uri) { UserName = "***", Password = string.Empty }.Uri.ToString();
        return text.Replace(_git.RepositoryUrl, masked, StringComparison.Ordinal);
    }
}
