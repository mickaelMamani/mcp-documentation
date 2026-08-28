# CLAUDE.md — ApiDocs MCP (POC)

## What this project is

A proof-of-concept **MCP server in .NET 8** that exposes the internal documentation of an API platform to coding agents. Everything runs **in-process** (no database, no search engine, no auth). Documentation is a folder of generated Markdown files; for the POC it is a **fake documentation** we write ourselves.

Two design documents are the source of truth. **Read both before writing any code**, and keep them updated when a decision changes:

- `docs-design/DOC-FORMAT.md` — structure and content of the documentation folder (contract generator ↔ server).
- `docs-design/ARCHITECTURE.md` — layers, technology choices, retrieval design, MCP surface (tool names, descriptions, output formats), index lifecycle, benchmark.

If something in this file contradicts those documents, the design documents win; flag the contradiction.

## POC scope

In scope:
- stdio transport only.
- Tools: `list_domains`, `search_endpoints`, `get_endpoint`, `search_docs`. Resources `endpoint://{operationId}` and `doc://…`.
- Lexical retrieval with Lucene.NET (BM25). No embeddings, no reranker.
- Fake documentation: 2 domains (`volatility`, `folio`), 4–6 endpoints each, plus `_domain.md` files and `_platform.md`, fully conforming to `DOC-FORMAT.md`.
- Retrieval benchmark (xUnit) on a versioned question set.

Out of scope (do not implement, do not scaffold): authentication/authorization, Streamable HTTP host, embeddings/vectors, reranking, `reindex` admin tool, persistence of the index, Aspire.

## Repository layout

```
ApiDocs.sln
docs/                         # fake documentation (DOC-FORMAT.md compliant) — the data
docs-design/                  # DOC-FORMAT.md, ARCHITECTURE.md
src/
  ApiDocs.Domain/             # entities, value objects — zero dependencies
  ApiDocs.Application/        # use cases + ports (interfaces)
  ApiDocs.Infrastructure/     # Markdown source, Markdig chunker, Lucene index, snapshot provider
  ApiDocs.Mcp.Stdio/          # host: Generic Host + ModelContextProtocol stdio, MCP tools/resources
tests/
  ApiDocs.Tests/              # unit + retrieval benchmark
    benchmark/questions.yaml
```

Dependency direction: `Mcp.Stdio → Application → Domain`, `Infrastructure → Application → Domain`. **Domain references nothing. Application references only Domain.** Any violation is a bug, even if it compiles.

## Technology and packages

- .NET 8 (`net8.0`), C# 12, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<ImplicitUsings>enable</ImplicitUsings>`.
- `ModelContextProtocol` (official C# SDK). Tools via `[McpServerToolType]` / `[McpServerTool]` / `[Description]`. Register with `AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()` (verify exact API against the SDK docs — use the Context7 MCP with library `/modelcontextprotocol/csharp-sdk` when unsure).
- `Lucene.Net`, `Lucene.Net.Analysis.Common` (4.8.x). `RAMDirectory`, `BM25Similarity`, per-field analyzers as specified in `ARCHITECTURE.md §4.2`. Queries are built with query objects (`BooleanQuery`, `TermQuery`, `PrefixQuery`, `FuzzyQuery`), **never** with a text query parser.
- `Markdig` (+ YAML front matter extension), `YamlDotNet`, `System.Text.Json`.
- `Microsoft.ML.Tokenizers` for token counting (`cl100k_base`).
- Tests: xUnit, FluentAssertions.
- Central package management (`Directory.Packages.props`) with pinned versions.

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

`tests/ApiDocs.Tests/benchmark/questions.yaml` — at least 30 questions across `kind: intent | parameter | example | crosscutting`, some in French. The retrieval test simulates the LLM's reformulation with a fixed FR→EN mapping table in the test project.

Thresholds (build fails below them): hit@3 ≥ 0.90 on `search_endpoints`; MRR reported; average returned tokens per `get_endpoint` ≤ 2 500.

Any change to analyzers, boosts, synonyms, tool descriptions, or doc format must be accompanied by the benchmark result in the commit message.

## Commands

```bash
dotnet build
dotnet test                                   # unit + validator + benchmark
dotnet run --project src/ApiDocs.Mcp.Stdio -- --docs ./docs
npx @modelcontextprotocol/inspector dotnet run --project src/ApiDocs.Mcp.Stdio -- --docs ./docs
```

Local Claude Code registration (`.mcp.json` at repo root, committed):

```json
{
  "mcpServers": {
    "api-docs": {
      "command": "dotnet",
      "args": ["run", "--project", "src/ApiDocs.Mcp.Stdio", "--", "--docs", "./docs"]
    }
  }
}
```

## How to work in this repo

1. Start from the plan in `ARCHITECTURE.md §10`. Deliver in this order: Domain model → validator + fake docs → chunker → Lucene index + snapshot → use cases → MCP tools → benchmark → resources.
2. For each step: write the tests first when the behaviour is specified in the design docs (validator rules, chunk kinds, query construction, token budget assembly).
3. Keep the design docs in sync: if you take a decision not covered there, add a row to the ADR table in `ARCHITECTURE.md §11`.
4. Do not add packages, projects, or abstractions not listed here without asking. Do not add a `Common`/`Shared`/`Utils` project.
5. When unsure about retrieval behaviour, run the benchmark rather than reasoning about it.
6. Answer in French or English, matching the user; code, comments, tool descriptions and commit messages in English.
