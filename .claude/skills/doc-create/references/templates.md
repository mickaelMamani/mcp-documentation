# Templates — copy these, don't write from memory

Every template below already applies the traps documented in SKILL.md: quoted `summary`,
quoted `formatVersion`, `>-` block for `keywords`, exact section titles, one tagged code
block per example. Replace the placeholder content; keep the structure and the quoting.

## 1. Endpoint file — `<domain>/<operation-id>.md`

Filename = `operationId` in kebab-case (`GetFolioById` → `get-folio-by-id.md`).

````markdown
---
formatVersion: "1.0"
domain: Folio
operationId: GetFolioById
method: GET
route: /folio/{folioId}
version: "2.3"
summary: "Get one folio by id, including its positions."
tags: [folio, retrieval]
keywords: >-
  folio folios portfolio portfolios get retrieve fetch by id single detail details
  positions folioId
  folio folios portefeuille portefeuilles récupérer obtenir consulter détail
aliases: [fetch, lookup]
related: [SearchFolio, CloseFolio]
deprecated: false
---

## Description
When to use it, what it does, prerequisites, side effects, pitfalls. 3 to 10 lines.
Do not repeat the summary. Reference other endpoints as inline code: use `SearchFolio`
to find the id first.

## Parameters
| Name | In | Type | Required | Description |
|---|---|---|---|---|
| folioId | path | integer | yes | Identifier of the folio. |

## Response
| Status | Meaning |
|---|---|
| 200 | The folio with its positions. |
| 404 | Folio not found. |

```json
{ "folioId": 42, "name": "EU Equities", "positions": [] }
```

## Example C#
```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;

using var client = new HttpClient { BaseAddress = new Uri("https://api.example.internal") };
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

var folio = await client.GetFromJsonAsync<FolioDto>("/folio/42");
Console.WriteLine($"{folio!.Name}: {folio.Positions.Count} positions");

internal sealed record FolioDto(int FolioId, string Name, List<PositionDto> Positions);
internal sealed record PositionDto(int PositionId, string Instrument, decimal Quantity);
```

## Example Python
```python
import requests

BASE_URL = "https://api.example.internal"
headers = {"Authorization": f"Bearer {token}"}

response = requests.get(f"{BASE_URL}/folio/42", headers=headers, timeout=30)
response.raise_for_status()
folio = response.json()
print(f"{folio['name']}: {len(folio['positions'])} positions")
```

## Notes
Optional. Security remarks, limits, per-environment behavior. Delete the section if empty.
````

Checklist for this file kind:

- [ ] `summary` double-quoted, one sentence, ≤ 160 chars, starts with Get/List/Search/Create/Update/Delete/Apply/Compute
- [ ] `keywords`: inflected EN forms + parameter names + 5–10 FR terms, no punctuation
- [ ] `operationId`/`method`/`route` identical to the `manifest.json` entry
- [ ] Section titles exactly: Description, Parameters, Response, Example C#, Example Python, Notes — in order, no H1
- [ ] Parameters table header exactly `Name | In | Type | Required | Description`; `In` ∈ path/query/header/body
- [ ] Exactly one fenced block per Example section, language tagged, self-contained code
- [ ] `related` entries all exist as operationIds

## 2. Domain file — `<domain>/_domain.md`

````markdown
---
formatVersion: "1.0"
domain: Folio
summary: "Folios (portfolios of positions): search, retrieval, lifecycle."
keywords: >-
  folio folios portfolio portfolios search searching find finding query querying
  get retrieve list create close lifecycle owner
  folio folios portefeuille portefeuilles requêter rechercher récupérer lister créer
endpoints: [SearchFolio, GetFolioById, CreateFolio, CloseFolio]
---

## Overview
Role of the domain, data model in 5 lines, invariants. Strictly English prose.

## Use cases
| I want to… | Use | Notes |
|---|---|---|
| Find folios matching criteria (name, owner, date) | `SearchFolio` | Paginated; max 500 per page. |
| Fetch one folio when I already know its id | `GetFolioById` | Cheaper than search; includes positions. |
| Create a folio | `CreateFolio` | Requires `folio:write` scope. |
| Close a folio at end of life | `CloseFolio` | Irreversible. |

## Typical workflow
1. `SearchFolio` to find the id → 2. `GetFolioById` for details → 3. `CloseFolio`.

## Authentication & scopes
Required scopes and domain-specific headers.

## Common errors
| Code | Cause | Fix |
|---|---|---|
| 400 | Empty search criteria | Provide at least one filter. |
| 404 | Folio not found | Verify the id with `SearchFolio`. |
````

Checklist:

- [ ] Section titles exactly: Overview, Use cases, Typical workflow, Authentication & scopes, Common errors (`&`, not "and")
- [ ] Use cases: one row per endpoint minimum, varied verbs in user language, endpoint as inline code
- [ ] `endpoints:` lists every endpoint of the domain, all existing operationIds
- [ ] Same summary/keywords rules as endpoints (quoted, ≤ 160, no punctuation in keywords, FR terms)

## 3. Platform file — `_platform.md`

Section titles are free in this file only.

````markdown
---
formatVersion: "1.0"
platform: Federer API
platformVersion: "2.3.0"
summary: "Platform-wide conventions: authentication, pagination, errors, rate limits."
keywords: >-
  platform authentication token bearer keycloak pagination error errors rate limit
  headers base url environment
  plateforme authentification jeton pagination erreur erreurs limite environnement
---

## Base URLs
| Environment | URL |
|---|---|
| Production | https://api.example.internal |

## Authentication
How to obtain and pass the token.

## Pagination
Common paging parameters and response envelope.

## Errors
Global error format and status code conventions.
````

## 4. Manifest entry — `manifest.json`

Add the endpoint under its domain; create the domain block if new. Keep
`operationId`/`method`/`route`/`summary` byte-identical with the front matter
(`method` may differ in case only).

```json
{
  "formatVersion": "1.0",
  "platform": "Federer API",
  "platformVersion": "2.3.0",
  "generatedAt": "2026-08-31T10:00:00Z",
  "language": "en",
  "domains": [
    {
      "id": "folio",
      "name": "Folio",
      "summary": "Folios (portfolios of positions): search, retrieval, lifecycle.",
      "file": "folio/_domain.md",
      "endpoints": [
        {
          "operationId": "GetFolioById",
          "method": "GET",
          "route": "/folio/{folioId}",
          "summary": "Get one folio by id, including its positions.",
          "file": "folio/get-folio-by-id.md",
          "tags": ["folio", "retrieval"],
          "deprecated": false
        }
      ]
    }
  ]
}
```

Checklist:

- [ ] `id` kebab-case and equal to the folder name; `file` paths relative to `docs/`
- [ ] Every manifest endpoint has a file on disk and vice versa
- [ ] No duplicate `operationId` anywhere in the manifest
