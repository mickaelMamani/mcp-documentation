---
formatVersion: "1.0"
domain: Folio
operationId: GetFolioById
method: GET
route: /folios/{folioId}
version: "2.3"
summary: Get one folio with its header and aggregated exposures when the id is known.
tags: [folio]
keywords: >-
  folio folios portfolio portfolios get gets getting fetch fetches fetching load
  loads loading read reads reading retrieve retrieves retrieving by id identifier
  header exposures exposure delta vega notional valuation asof folioId
  includeExposures asOf owner currency status marketValue positionCount
  folio folios portefeuille récupérer obtenir charger lire identifiant expositions
aliases: [fetch, load, read]
related: [SearchFolio, GetFolioPositions, CloseFolio]
deprecated: false
---

## Description
This is the cheapest read of the domain, a single lookup on the primary key, so
prefer it over `SearchFolio` whenever the id is already known. It returns the
folio header plus its aggregated exposures, but never the individual lines: call
`GetFolioPositions` to obtain the positions themselves. Exposures are computed at
`asOf` from the valuation cache and can lag the market by up to 15 minutes; treat
them as indicative rather than as an official risk figure. Set
`includeExposures=false` when only the header is needed, which skips the
valuation join. A folio owned by another desk answers 403, not 404, so the two
cases must be handled separately. Requires the `folio:read` scope.

## Parameters
| Name | In | Type | Required | Description |
|---|---|---|---|---|
| folioId | path | string | yes | Identifier of the folio, for example `FOL-000123`. |
| includeExposures | query | boolean | no | Include the aggregated `exposures` block. Default `true`. |
| asOf | query | string | no | Business date used to value the folio, for example `2026-08-28`. Defaults to today. |
| X-Business-Date | header | string | no | Business date of the query context when `asOf` is not supplied. Defaults to today. |

## Response
| Status | Meaning |
|---|---|
| 200 | The folio header, with `exposures` when requested. |
| 400 | Malformed `folioId` or unparsable `asOf` date. |
| 401 | Missing, malformed or expired access token. |
| 403 | Folio is owned by another desk, or the token lacks the `folio:read` scope. |
| 404 | No folio exists with this identifier. |
| 429 | Rate limit of 600 requests per minute exceeded; see `Retry-After`. |

```json
{
  "folioId": "FOL-000123",
  "name": "Vega Book EUR",
  "owner": "desk-fx-options",
  "currency": "EUR",
  "status": "open",
  "tags": ["fx", "desk-paris"],
  "description": "FX vega book of the Paris desk",
  "createdAt": "2026-02-14T08:30:00Z",
  "updatedAt": "2026-08-28T09:12:00Z",
  "closedAt": null,
  "positionCount": 42,
  "marketValue": 12850400.75,
  "exposures": { "delta": -1840500.0, "vega": 96250.5, "notional": 310000000.0 }
}
```

## Example C#
```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

var token = Environment.GetEnvironmentVariable("FEDERER_API_TOKEN");
var folioId = "FOL-000123";

using var http = new HttpClient { BaseAddress = new Uri("https://api.example.internal") };
http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

using var response = await http.GetAsync($"/folios/{folioId}?includeExposures=true&asOf=2026-08-28");

if (response.StatusCode == HttpStatusCode.NotFound)
{
    Console.WriteLine($"Folio {folioId} does not exist");
    return;
}

if (response.StatusCode == HttpStatusCode.Forbidden)
{
    Console.WriteLine($"Folio {folioId} exists but belongs to another desk, or the token lacks folio:read");
    return;
}

response.EnsureSuccessStatusCode();

var folio = await response.Content.ReadFromJsonAsync<JsonNode>();
Console.WriteLine($"{folio!["name"]} owned by {folio["owner"]} in {folio["currency"]}");
Console.WriteLine($"Status {folio["status"]}, {folio["positionCount"]} positions, MV {folio["marketValue"]}");

var exposures = folio["exposures"];
Console.WriteLine($"Delta {exposures!["delta"]} Vega {exposures["vega"]} Notional {exposures["notional"]}");
```

## Example Python
```python
import os

import requests

BASE_URL = "https://api.example.internal"
TOKEN = os.environ["FEDERER_API_TOKEN"]
FOLIO_ID = "FOL-000123"

headers = {
    "Authorization": f"Bearer {TOKEN}",
    "Accept": "application/json",
}

response = requests.get(
    f"{BASE_URL}/folios/{FOLIO_ID}",
    headers=headers,
    params={"includeExposures": "true", "asOf": "2026-08-28"},
    timeout=30,
)

if response.status_code == 404:
    raise SystemExit(f"Folio {FOLIO_ID} does not exist")

if response.status_code == 403:
    raise SystemExit(
        f"Folio {FOLIO_ID} exists but belongs to another desk, or the token lacks folio:read"
    )

response.raise_for_status()

folio = response.json()
print(folio["folioId"], folio["name"], folio["owner"], folio["currency"], folio["status"])
print("Positions:", folio["positionCount"], "Market value:", folio["marketValue"])

exposures = folio["exposures"]
print("Delta:", exposures["delta"])
print("Vega:", exposures["vega"])
print("Notional:", exposures["notional"])
```
