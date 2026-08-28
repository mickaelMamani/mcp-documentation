---
formatVersion: "1.0"
domain: Folio
operationId: SearchFolio
method: POST
route: /folios/search
version: "2.3"
summary: Search folios by name, owner, currency or date criteria.
tags: [folio, search]
keywords: >-
  folio folios portfolio portfolios search searches searching find finds finding
  list lists listing query queries querying lookup filter filters filtering
  criteria criterion match matching paged paging pagination name owner currency
  status open closed tags createdFrom createdTo sort page pageSize folioId
  marketValue positionCount
  portefeuille portefeuilles rechercher recherche requêter trouver filtrer lister
  propriétaire devise
aliases: [query, find, lookup]
related: [GetFolioById, CreateFolio, GetFolioPositions]
deprecated: false
---

## Description
This is the entry point into the domain when the folio id is unknown: it turns
business criteria into a page of folio headers. An empty JSON body is valid and
returns every folio the caller is allowed to read. All supplied criteria are
combined with AND, so adding a field can only narrow the result set. The call
scans the folio store and is markedly heavier than `GetFolioById`; when the id is
already known, read the folio directly instead of searching for it. Results are
always paged, so drive your loop from `totalCount` rather than assuming a single
page. Requires the `folio:read` scope.

## Parameters
| Name | In | Type | Required | Description |
|---|---|---|---|---|
| name | body | string | no | Case-insensitive "contains" match on the folio name. At least 2 characters. |
| owner | body | string | no | Exact owning desk identifier, for example `desk-fx-options`. No wildcard. |
| currency | body | string | no | ISO 4217 currency code of the folio, for example `EUR`. |
| status | body | string | no | Folio status, `open` or `closed`. Omit to return both. |
| tags | body | array of string | no | Matches folios carrying any of the listed tags. |
| createdFrom | body | string | no | Inclusive lower bound on the creation business date, for example `2026-01-01`. |
| createdTo | body | string | no | Inclusive upper bound on the creation business date, for example `2026-08-28`. |
| sort | body | string | no | `name`, `createdAt` or `marketValue`, with an optional `-` prefix for descending. Default `-createdAt`. |
| page | query | integer | no | 1-based page number. Default 1. |
| pageSize | query | integer | no | Number of folios per page. Default 50, maximum 500. |
| X-Correlation-Id | header | string | no | Client correlation id, echoed back on the response. |

## Response
| Status | Meaning |
|---|---|
| 200 | Paged envelope of folio headers matching the criteria. |
| 400 | Malformed body, unknown `sort` field or unparsable date. |
| 401 | Missing, malformed or expired access token. |
| 403 | Token does not carry the `folio:read` scope. |
| 422 | `name` is shorter than 2 characters. |
| 429 | Rate limit of 600 requests per minute exceeded; see `Retry-After`. |

```json
{
  "items": [
    {
      "folioId": "FOL-000123",
      "name": "Vega Book EUR",
      "owner": "desk-fx-options",
      "currency": "EUR",
      "status": "open",
      "tags": ["fx", "desk-paris"],
      "createdAt": "2026-02-14T08:30:00Z",
      "positionCount": 42,
      "marketValue": 12850400.75
    }
  ],
  "page": 1,
  "pageSize": 50,
  "totalCount": 137
}
```

## Example C#
```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

var token = Environment.GetEnvironmentVariable("FEDERER_API_TOKEN")
            ?? throw new InvalidOperationException("FEDERER_API_TOKEN is not set.");

using var http = new HttpClient { BaseAddress = new Uri("https://api.example.internal") };
http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
http.DefaultRequestHeaders.Add("X-Correlation-Id", Guid.NewGuid().ToString());

var criteria = new
{
    name = "vega",
    owner = "desk-fx-options",
    currency = "EUR",
    status = "open",
    tags = new[] { "fx", "desk-paris" },
    createdFrom = "2026-01-01",
    createdTo = "2026-08-28",
    sort = "-createdAt"
};

using var response = await http.PostAsJsonAsync("/folios/search?page=1&pageSize=50", criteria);
response.EnsureSuccessStatusCode();

var payload = await response.Content.ReadFromJsonAsync<JsonNode>();
Console.WriteLine($"Total folios: {payload!["totalCount"]}");

foreach (var item in payload["items"]!.AsArray())
{
    Console.WriteLine($"{item!["folioId"]} {item["name"]} {item["owner"]} {item["marketValue"]}");
}
```

## Example Python
```python
import os
import uuid

import requests

BASE_URL = "https://api.example.internal"
TOKEN = os.environ["FEDERER_API_TOKEN"]

headers = {
    "Authorization": f"Bearer {TOKEN}",
    "Accept": "application/json",
    "Content-Type": "application/json",
    "X-Correlation-Id": str(uuid.uuid4()),
}

criteria = {
    "name": "vega",
    "owner": "desk-fx-options",
    "currency": "EUR",
    "status": "open",
    "tags": ["fx", "desk-paris"],
    "createdFrom": "2026-01-01",
    "createdTo": "2026-08-28",
    "sort": "-createdAt",
}

response = requests.post(
    f"{BASE_URL}/folios/search",
    headers=headers,
    params={"page": 1, "pageSize": 50},
    json=criteria,
    timeout=30,
)
response.raise_for_status()

payload = response.json()
print(f"Total folios: {payload['totalCount']}")
for folio in payload["items"]:
    print(folio["folioId"], folio["name"], folio["owner"], folio["marketValue"])
```

## Notes
The search index is eventually consistent: a folio created less than 2 seconds
ago may be missing from the results. Right after `CreateFolio`, read the folio
back with `GetFolioById` using the returned id instead of searching for it.
