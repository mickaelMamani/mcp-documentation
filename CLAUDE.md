# CLAUDE.md — ApiDocs MCP (POC)

## What this project is

A proof-of-concept **MCP server in .NET 8** that exposes the internal documentation of an API platform to coding agents. Everything runs **in-process** (no database, no search engine, no auth). Documentation is a folder of generated Markdown files; for the POC it is a **fake documentation** we write ourselves.

Two design documents are the source of truth. **Read both before writing any code**, and keep them updated when a decision changes:

- `specs/DOC-FORMAT.md` — structure and content of the documentation folder (contract generator ↔ server).
- `specs/ARCHITECTURE.md` — layers, technology choices, retrieval design, MCP surface (tool names, descriptions, output formats), index lifecycle, benchmark.

If something in this file contradicts those documents, the design documents win; flag the contradiction.

## POC scope

In scope:
- Two transports, one application: stdio (local, one server per developer machine) and Streamable HTTP (`/mcp`, shared production server).
- Tools: `list_domains`, `search_endpoints`, `get_endpoint`, `search_docs`. Resources `endpoint://{operationId}` and `doc://…`.
- Lexical retrieval with Lucene.NET (BM25). No embeddings, no reranker.
- Fake documentation: 2 domains (`volatility`, `folio`), 4–6 endpoints each, plus `_domain.md` files and `_platform.md`, fully conforming to `DOC-FORMAT.md`.
- Retrieval benchmark (xUnit) on a versioned question set.

Out of scope (do not implement, do not scaffold): authentication/authorization, embeddings/vectors, reranking, `reindex` admin tool, hot reload of the index, persistence of the index, Aspire.

Authentication and hot reload are out of scope **by decision, not by oversight** (`ARCHITECTURE.md` ADR #24 and #25): access is closed off upstream of the application (VPN, mTLS, gateway), and a documentation update is picked up by restarting the service. Both decisions have a single hook point should they be revisited — do not pre-build for them.

## Repository layout

```
ApiDocs.sln
docs/                         # fake documentation (DOC-FORMAT.md compliant) — the data
specs/                        # DOC-FORMAT.md, ARCHITECTURE.md
.mcp-server/                  # published copy of the stdio host that .mcp.json runs — gitignored
.mcp-server-http/             # published copy of the HTTP host, for local end-to-end tests — gitignored
src/
  ApiDocs.Domain/             # entities, value objects — zero dependencies
  ApiDocs.Application/        # use cases + ports (interfaces)
  ApiDocs.Infrastructure/     # Markdown source, Git checkout, Markdig chunker, Lucene index, snapshot provider
  ApiDocs.Mcp/                # MCP surface shared by both hosts: tools, resources, ServerInstructions
  ApiDocs.Mcp.Stdio/          # host: Generic Host + stdio transport — the developer loop
  ApiDocs.Mcp.Http/           # host: ASP.NET Core + Streamable HTTP on /mcp — the production server
tests/
  ApiDocs.Tests/              # unit + retrieval benchmark + tuning sweep
    benchmark/questions.yaml       # authored question set
    benchmark/heldout-terse.yaml   # held-out queries, generated — see ARCHITECTURE.md §9
```

Dependency direction: `Mcp.Stdio | Mcp.Http → Mcp → Application → Domain`, `Infrastructure → Application → Domain`. **Domain references nothing. Application references only Domain.** Any violation is a bug, even if it compiles. One deliberate exception: both hosts also reference `Infrastructure`, because a host is the composition root and is the only project allowed to know the implementations (`ARCHITECTURE.md` ADR #11). Do not "fix" it.

The two hosts contain **only** wiring: transport, configuration, startup, and for the HTTP one the `/health` endpoint. Anything a client can see — a tool, its description, a resource — belongs to `ApiDocs.Mcp` and is registered by `WithApiDocsSurface()`, so stdio and HTTP can never expose different surfaces (`ARCHITECTURE.md` ADR #22).

## Technology and packages

- .NET 8 (`net8.0`), C# 12, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<ImplicitUsings>enable</ImplicitUsings>`.
- `ModelContextProtocol` (official C# SDK). Tools via `[McpServerToolType]` / `[McpServerTool]` / `[Description]`. Register with `AddMcpServer().WithStdioServerTransport()`, and for the production host `ModelContextProtocol.AspNetCore` with `.WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless)` plus `app.MapMcp("/mcp")` (verify exact API against the SDK docs — use the Context7 MCP with library `/modelcontextprotocol/csharp-sdk` when unsure).
- `Lucene.Net`, `Lucene.Net.Analysis.Common` (4.8.x). `RAMDirectory`, `BM25Similarity`, per-field analyzers as specified in `ARCHITECTURE.md §4.2`. Queries are built with query objects (`BooleanQuery`, `TermQuery`, `PrefixQuery`, `FuzzyQuery`), **never** with a text query parser.
- `Markdig` (+ YAML front matter extension), `YamlDotNet`, `System.Text.Json`.
- `Microsoft.ML.Tokenizers` for token counting (`cl100k_base`), together with `Microsoft.ML.Tokenizers.Data.Cl100kBase`, which embeds the vocabulary: without it the tokenizer fetches it over the network at startup, which breaks the "no outbound call from the server" rule (`ARCHITECTURE.md` ADR #16).
- Tests: xUnit, FluentAssertions.
- Central package management (`Directory.Packages.props`) with pinned versions, plus a committed `NuGet.config` pinned to a single source — central package management fails with NU1507 as soon as a machine declares several (`ARCHITECTURE.md` ADR #17).

Before using any package API you are not certain about, look it up (Context7: `resolve-library-id` then `query-docs`). Do not guess signatures.

## Code conventions (mandatory)

- `internal sealed` by default. `public` only where the SDK or test project requires it (use `InternalsVisibleTo` for tests).
- Primary constructors. `readonly record struct` / `sealed record` for value objects; invariants enforced in factory methods.
- Logging with `LoggerMessage` source generators in a `Log` partial class per assembly (`Log.IndexRebuilt(logger, files, chunks, ms)`). **In stdio mode all logs go to stderr** — stdout is the JSON-RPC channel; writing anything else to stdout breaks the protocol.
- No `ConfigureAwait(false)` in the host project; use it in Infrastructure/Application (they are library code).
- No swallowed exceptions. Ingestion errors for a file → file rejected + validation report; the rest of the index still builds. A failed rebuild keeps the previous snapshot.
- Nullable reference types everywhere; no `!` null-forgiving except in tests with a comment.
- No `static` mutable state. The current index is an immutable snapshot published with `Interlocked.Exchange` (see `ARCHITECTURE.md §6`).
- Hot path is allocation-conscious: `FrozenDictionary` for lookups, arrays over lists in the snapshot, no LINQ in per-request scoring loops.
- Public MCP tool descriptions and `ServerInstructions` are **copied verbatim from `ARCHITECTURE.md §5`**. Changing them requires updating the document and re-running the benchmark.

## Fake documentation rules

- Lives in `docs/`, generated by hand for the POC. Must pass every validation rule in `DOC-FORMAT.md §7` — write the validator first, then the docs, and run the validator in a test.
- Use realistic finance-flavoured content (volatility surfaces, folios, positions) but **no real internal data, hostnames, or credentials**. Base URL `https://api.example.internal`.
- Every endpoint file has both `Example C#` and `Example Python` sections with complete, self-contained snippets.
- Each `_domain.md` has a filled `## Use cases` table with varied verbs (find / search / query / look up / retrieve). This table drives intent questions in the benchmark.
- `keywords` fields include French terms as specified in `DOC-FORMAT.md §6`.

## Benchmark

`tests/ApiDocs.Tests/benchmark/questions.yaml` — at least 30 questions across `kind: intent | parameter | example | crosscutting`, some in French, and some marked `terse: true`. Terse means a 1–3 token query: that is the shape the tool descriptions ask the model for, so a set made only of full sentences does not test what the server actually receives. The retrieval test simulates the LLM's reformulation with a fixed FR→EN mapping table in the test project.

`benchmark/heldout-terse.yaml` — held-out queries written from `docs/` alone by agents that never saw the retrieval code. It arbitrates tuning, never the build gate. Nothing may be fitted to it: do not hand-edit it to make a configuration look better, regenerate it.

Thresholds (build fails below them): hit@3 ≥ 0.90 on `search_endpoints`; MRR and hit@1 reported; average returned tokens per `get_endpoint` ≤ 2 500. **Watch hit@1**: hit@3 alone once hid a defect where the right endpoint was systematically second behind an endpoint of the other domain (`ARCHITECTURE.md` ADR #20).

Analyzer and boost values live in `LuceneOptions` and `RankingOptions`, not in constants, so they can be measured. Any change to analyzers, boosts, synonyms, tool descriptions, or doc format must be accompanied by the benchmark result in the commit message.

## Commands

```bash
dotnet build
dotnet test                                   # unit + validator + benchmark + tuning sweep
dotnet run --project src/ApiDocs.Mcp.Stdio -- --docs ./docs        # stdio dev loop
dotnet publish src/ApiDocs.Mcp.Stdio -c Release -o .mcp-server     # what .mcp.json runs
npx @modelcontextprotocol/inspector dotnet .mcp-server/ApiDocs.Mcp.Stdio.dll --docs ./docs

dotnet run --project src/ApiDocs.Mcp.Http                          # HTTP host, dev profile: local docs/ folder
curl http://localhost:5080/health                                  # snapshot served + documentation revision

dotnet publish src/ApiDocs.Mcp.Http -c Release -o .mcp-server-http  # copy to run while still building the solution
dotnet .mcp-server-http/ApiDocs.Mcp.Http.dll --urls http://localhost:5080 --Docs:Source=Folder --Docs:Path=./docs
```

Same trap as the stdio host, same fix: `dotnet run --project src/ApiDocs.Mcp.Http` holds `src/ApiDocs.Mcp.Http/bin/**` locked, so `dotnet build` and `dotnet test` fail for as long as it runs. Run the published copy instead when the server has to stay up while you keep working (`ARCHITECTURE.md` ADR #19). Note that `dotnet run` on a Web SDK project sets the working directory to the **project** folder, not the repository root — hence `Docs__Path=../../docs` in its launch profile.

Debugging with F5 (Visual Studio / VS Code) uses `src/ApiDocs.Mcp.Stdio/Properties/launchSettings.json`, which sets the working directory to the repository root and passes `--docs ./docs`. Without it the debugger starts in `bin/Debug/net8.0`, `--docs` resolves to `bin/Debug/net8.0/docs`, and the host exits with `No manifest.json found` (`ARCHITECTURE.md` ADR #21). The path is always resolved against the working directory of the process, never against the binaries.

Local Claude Code registration (`.mcp.json` at repo root, committed). It runs the **published copy**, not `dotnet run`: a live `dotnet run` holds `src/ApiDocs.Mcp.Stdio/bin/**` locked, so `dotnet build` and `dotnet test` fail for as long as a client is connected (`ARCHITECTURE.md` ADR #19). Re-run the `dotnet publish` above after changing code — `docs/` is read live from the repo root and only needs a server restart.

```json
{
  "mcpServers": {
    "api-docs": {
      "command": "dotnet",
      "args": [".mcp-server/ApiDocs.Mcp.Stdio.dll", "--docs", "./docs"]
    }
  }
}
```

## Production host (`ApiDocs.Mcp.Http`)

The documentation is centralised and changes on its own schedule, so the server fetches it: at startup it checks the configured Git reference out into a working copy, indexes it, and only then starts serving `/mcp`. A failure at that point exits with code 1 — a bad configuration fails the deployment instead of serving an empty corpus. `git` must be on the server's PATH, and its credentials come from the machine (SSH key, credential helper): `GIT_TERMINAL_PROMPT=0` is forced, so a missing credential fails fast instead of hanging a headless process.

Configuration binds the `Docs` section; every key has an environment variable form (`Docs__Git__Reference`).

| Key | Default | What it does |
|---|---|---|
| `Docs:Source` | `Git` | `Folder` (dev, stdio, tests) or `Git` (production) |
| `Docs:Path` | `docs` | Folder mode only; ignored in Git mode, where it is derived |
| `Docs:Git:RepositoryUrl` | — | **Required** in Git mode |
| `Docs:Git:Reference` | `main` | Branch or tag to serve |
| `Docs:Git:Subdirectory` | `docs` | Folder holding `manifest.json` inside the repository |
| `Docs:Git:WorkingCopy` | `docs-checkout` | Kept between restarts, so a restart only fetches the delta |
| `Docs:Git:Depth` | `1` | `0` fetches the whole history |
| `Docs:Git:Timeout` | `00:02:00` | Budget per git invocation |
| `ASPNETCORE_URLS` | `http://localhost:5000` | Do not hardcode it in `appsettings.json`: that would win over the environment |

Updating the documentation means restarting the service (ADR #25). `GET /health` reports `docsRevision`, the commit actually indexed — that is how you confirm the restart picked the new corpus up. Client registration then uses the HTTP transport:

```json
{
  "mcpServers": {
    "api-docs": {
      "type": "http",
      "url": "https://api-docs.example.internal/mcp"
    }
  }
}
```

## How to work in this repo

1. Start from the plan in `ARCHITECTURE.md §10`. Deliver in this order: Domain model → validator + fake docs → chunker → Lucene index + snapshot → use cases → MCP tools → benchmark → resources.
2. For each step: write the tests first when the behaviour is specified in the design docs (validator rules, chunk kinds, query construction, token budget assembly).
3. Keep the design docs in sync: if you take a decision not covered there, add a row to the ADR table in `ARCHITECTURE.md §11`.
4. Do not add packages, projects, or abstractions not listed here without asking. Do not add a `Common`/`Shared`/`Utils` project.
5. When unsure about retrieval behaviour, run the benchmark rather than reasoning about it — `RetrievalTuningSweepTests` scores a whole grid of configurations in one run and names the queries each one still gets wrong.
6. Answer in French or English, matching the user; code, comments, tool descriptions and commit messages in English.
