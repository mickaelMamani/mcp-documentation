---
name: doc-create
description: Create, update, or verify documentation for the ApiDocs MCP server — endpoint .md files, _domain.md, _platform.md, and manifest.json under a docs/ folder governed by specs/DOC-FORMAT.md. Use this skill EVERY time a task touches documentation content or format, even if the user does not name it — writing or regenerating an endpoint doc, adding a domain, editing front matter, summaries or keywords, fixing a validation or YAML ingestion error, or checking that a docs folder will pass the validator. It encodes every DOC-FORMAT rule, the exact behavior of DocumentValidator, and the YAML pitfalls (unquoted colons in summary) that crash ingestion.
---

# doc-create — write documentation that passes ingestion on the first try

`specs/DOC-FORMAT.md` is the contract between the documentation generator and the MCP
server, and `src/ApiDocs.Infrastructure/Ingestion/DocumentValidator.cs` is its executable
form. This skill condenses both so a doc file is written correctly the first time. If this
skill ever contradicts `specs/DOC-FORMAT.md`, the spec wins — and flag the contradiction so
the skill gets fixed.

Ready-to-copy templates for all four file kinds live in
[references/templates.md](references/templates.md). Start from a template rather than from
memory; every known trap is already handled in them.

## Workflow

1. **Identify what you are touching.** Four file kinds exist, each with its own rules:
   `manifest.json`, endpoint files (`[<group>/]<domain>/<operation-id>.md`), domain files
   (`[<group>/]<domain>/_domain.md`), and the platform file (`_platform.md`).
2. **Creating or updating** → copy the matching template from `references/templates.md`,
   fill it in, then run the full checklist below on the result.
3. **Checking existing docs** → run the checklist below file by file, then run the real
   validator (see "Verify before you finish").
4. **Never finish without running the validator.** The checklist catches what you can see;
   the test run catches what you can't.

## The #1 failure mode: YAML front matter

The front matter is parsed by YamlDotNet with camelCase keys. Most ingestion crashes ever
seen on this format are YAML syntax, not content. Apply these rules mechanically:

- **Always double-quote `summary`.** A summary almost always contains `: ` or ends with
  `.`, and an unquoted colon-space inside a plain scalar is invalid YAML. The parser fails
  with *"While scanning a plain scalar value, found invalid mapping"* pointing at the
  column of the colon. This has caused real production incidents; quoting costs nothing.

  ```yaml
  summary: "Folio (portfolio) management in Sophis: modifications and hierarchy."   # correct
  summary: Folio (portfolio) management in Sophis: modifications and hierarchy.     # CRASHES
  ```

- Quote any other scalar value containing `:`, `#`, leading/trailing spaces, or starting
  with `[`, `{`, `*`, `&`, `!`, `>`, `|`, `%`, `@`, `` ` ``, `"`, `'`.
- **`keywords` uses the `>-` block scalar**, indented two spaces, so it can span lines and
  needs no quoting. It must contain plain words separated by spaces — the validator
  rejects any of these characters in it: `,;:.!?()[]{}"'` `` ` `` `<>=`. In particular:
  no commas between words, no trailing period. A hyphen is not in the forbidden set, but
  prefer separate words (`code type` rather than `code-type`) — users query the parts,
  and the analyzer indexes what you write literally.
- Lists use flow style: `tags: [surface, calibration]`, `related: [GetSurface]`,
  `endpoints: [SearchFolio, GetFolioById]`. Unquoted entries are fine there (no colons in
  operationIds).
- The file must **start** with `---` on line 1 (no BOM, no blank line before it) and the
  block must be closed by a second `---`. A file without a closed front matter block is
  rejected (`front-matter.missing`).
- Keys are camelCase (`formatVersion`, `operationId`, `platformVersion`). Unknown keys are
  ignored silently — a typo in a key name becomes a "required field missing" error, not a
  parse error, so double-check spelling when the validator says a field is missing that
  you can see in the file.
- `formatVersion` is the string `"1.0"`, quoted — unquoted `1.0` parses as a number and
  fails the exact string comparison.
- No tabs anywhere in the YAML block. UTF-8 without BOM, LF line endings, for the whole
  file.

## Files and folders

- One folder per domain, named in **kebab-case**, matching the normalized `domain` field.
- Optionally, domain folders may sit under **one** grouping folder (kebab-case, macro
  feature group): `pricing/volatility/plug.md`. Grouped and root-level domains can mix.
  Purely organizational — the server only follows the manifest `file` paths (relative to
  the docs root, `/` separator), so the manifest must point at the real path; the group
  never appears in domain ids or the MCP surface (DOC-FORMAT §2, ARCHITECTURE ADR #31).
- One file per endpoint, named as the `operationId` in kebab-case:
  `GetFolioById` → `get-folio-by-id.md`. The front matter `operationId` is the source of
  truth, the filename is derived — keep them in sync anyway.
- Files prefixed `_` are context files (`_domain.md`, `_platform.md`), never endpoints.
- No subdirectories inside a domain. A domain beyond ~150 endpoints is split into several
  domains, not into subfolders.

## manifest.json

The catalogue the server trusts. Required fields: `formatVersion`, `platform`,
`platformVersion`, `language`, and for each domain `id`, `name`, `summary`, `file`, and for
each endpoint `operationId`, `method`, `route`, `summary`, `file`.

Consistency rules the ingestion enforces (`manifest.consistency`, `operationId.unique`,
`manifest.file`):

- Every endpoint present in the manifest must have its file on disk, and every endpoint
  file must have a manifest entry — an `operationId` on one side only fails ingestion.
- `operationId`, `method`, `route` must be **identical** between manifest and front matter
  (`operationId`/`route` compared case-sensitively, `method` case-insensitively).
- `operationId` is unique across the whole repository.
- When you add, rename, or remove an endpoint, touch **three places**: the endpoint file,
  the manifest entry, and the `endpoints:` list of the domain's `_domain.md`. Add a
  `related:` back-reference from workflow neighbours while you're there.

## Endpoint file body

H2 titles are a **closed set, matched character-for-character** (case, spacing, `&`, `#`):
`Description`, `Parameters`, `Response`, `Example C#`, `Example Python`, `Notes` — in that
order. A missing section is allowed; a renamed one (`## Remarks`, `## Examples`,
`## Example CSharp`) is an ingestion error (`sections.titles`). A body with **zero** `##`
sections is also an error.

- **No H1** (`#`) in the body — the title is the `operationId`.
- `## Parameters` must be a table whose header row is exactly
  `Name | In | Type | Required | Description` (case-insensitive on the column names, but
  all five, in that order). `In` values ∈ `path | query | header | body`.
- Each `Example *` section contains **exactly one** fenced code block, with the language
  declared (` ```csharp `, ` ```python `). Zero blocks, two blocks, or a bare ` ``` `
  all fail (`sections.example.codeblock`). Any explanatory prose goes outside the fence.
- Examples are complete and self-contained: imports/usings, client instantiation, the
  call, reading the result. No `// ...` placeholders — the LLM copies examples verbatim,
  a partial example ships broken code to users.
- Token budgets (target, per section): Description ≤ 250, Parameters ≤ 400,
  Response ≤ 300, each Example ≤ 400. Over budget, synthesize — the server truncates long
  chunks and truncation degrades answers.
- No relative Markdown links to other files. Reference other endpoints by `operationId`
  in inline code: `` `GetSurface` `` — the server rewrites those to `endpoint://` links.
- Description says when to use it, prerequisites, side effects, pitfalls, in 3–10 lines,
  and does **not** repeat the summary.

## Domain file body (`_domain.md`)

Allowed H2 titles, exact strings: `Overview`, `Use cases`, `Typical workflow`,
`Authentication & scopes`, `Common errors` (note the `&`, not "and").

The **Use cases table is the highest-value retrieval content in the repository** — it is
what answers intent questions ("how do I query folios?") when the user doesn't know any
endpoint name. Columns: `I want to… | Use | Notes`. Write the first column in user
language with varied verbs (find / search / query / look up / retrieve / create), one row
per endpoint at minimum, and put the endpoint in the `Use` column as inline code.

The `endpoints:` front matter list must only contain `operationId`s that exist (checked
against the manifest, rule `related.exists`).

## Platform file body (`_platform.md`)

Same front matter shape but requires `platform` and `platformVersion` instead of
`domain`, no `endpoints` list. Section titles are **free** in this file only
(ARCHITECTURE ADR #8). Content: base URLs per environment, authentication, common
headers, pagination, error format, versioning, rate limits, date/number conventions.

## Summaries and keywords (retrieval quality rules)

- `summary`: **one sentence, ≤ 160 characters** (hard validator limit), usage language —
  a developer who has never seen the API must understand the endpoint from the summary
  alone. Start with a standardized action verb: `Get`, `List`, `Search`, `Create`,
  `Update`, `Delete`, `Apply`, `Compute`.
- `keywords`, one space-separated line via `>-`, containing in order:
  1. The entity and action words, **with inflected forms** — singular and plural, verb
     and noun (`plug plugs plugging`, `search searching searches`). The server does no
     stemming on this field.
  2. **Parameter names** (`surfaceId`, `tenor`, `codeType`) — users search by them.
  3. **5 to 10 French terms**: the entity, the action, common forms
     (`folio portefeuille requêter rechercher récupérer`). This is the lexical safety net
     for French questions; the doc prose itself stays strictly English.
- Never mix French into section bodies — it degrades English stemming.
- `related:` filled in whenever the endpoint belongs to a sequence; every entry must be
  an existing `operationId`.
- Realistic finance-flavoured content only — **no real internal data, hostnames, or
  credentials**. Base URL is `https://api.example.internal`.

## Validator rules → what to fix

| Rule id | Trigger | Fix |
|---|---|---|
| `front-matter.missing` | File doesn't start with a closed `---` block | Add front matter at line 1 |
| `front-matter.required` | A required field empty/absent (or its key misspelled) | Add the field; check camelCase spelling |
| `front-matter.formatVersion` | Not exactly `"1.0"` | Quote it: `formatVersion: "1.0"` |
| `manifest.consistency` | operationId/method/route differ from manifest | Align file and manifest |
| `operationId.unique` | Same operationId twice in the manifest | Rename one |
| `sections.titles` | H2 outside the allowed set, or no H2 at all | Use the exact titles above |
| `sections.example.codeblock` | ≠ 1 fenced block, or no language tag | One tagged block per Example section |
| `sections.parameters.table` | Wrong/missing 5-column header | `Name \| In \| Type \| Required \| Description` |
| `summary.length` | > 160 characters | Shorten |
| `keywords.punctuation` | Any of `,;:.!?()[]{}"'` `` ` `` `<>=` in keywords | Plain words, spaces only |
| `related.exists` | `related`/`endpoints` names an unknown operationId | Fix the name or add the endpoint |

A YamlDotNet exception (e.g. *"found invalid mapping"* at some line/column) happens
**before** validation: it is raw YAML syntax, almost always an unquoted `: ` inside
`summary`. The reported line is relative to the YAML block (line 1 = the line after the
opening `---`), and the column points at the offending colon.

## Verify before you finish

Run the real validator — it executes against the shipped `docs/` corpus:

```bash
dotnet test --filter "FullyQualifiedName~ValidationTests"
```

Zero errors **and zero warnings** are required. Then, if you added or removed endpoints
or domains, know that `ValidationTests.Every_manifest_endpoint_is_indexed` asserts the
exact corpus size (currently 12 endpoints, 2 domains) — update those counts in the same
change.

Finally run the full suite: content changes move retrieval scores, and the benchmark is a
build gate (hit@3 ≥ 0.90 on `search_endpoints`, avg `get_endpoint` tokens ≤ 2500):

```bash
dotnet test
```

Any change to summaries, keywords, or the doc format must be accompanied by the benchmark
result in the commit message (see CLAUDE.md). New endpoints usually also deserve new rows
in `tests/ApiDocs.Tests/benchmark/questions.yaml` — intent, parameter, and example
questions, some French, some `terse: true`.
