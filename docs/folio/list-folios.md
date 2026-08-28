---
formatVersion: "1.0"
domain: Folio
operationId: ListFolios
method: GET
route: /folios
version: "2.0"
summary: "List the folios visible to the caller. Deprecated: use SearchFolio."
tags: [folio, legacy]
keywords: >-
  list lists listing listed folio folios portfolio portfolios all every index catalogue
  legacy deprecated deprecation sunset removal migration enumerate enumerates enumerating
  browse browses paging pagination page pageSize owner correlation id header headers reporting job
  folio folios portefeuille lister liste tous obsolète déprécié énumérer parcourir
aliases: [all, index, legacy]
related: [SearchFolio, GetFolioById]
deprecated: true
---

## Description
This endpoint only exists for the legacy reporting jobs that were written before 2.2 and
that cannot send a request body; do not call it from new code. It offers no criteria
beyond `owner`, no sorting and no projection: the order of the items is unspecified and
may change between releases, so a paged read is not stable if folios are created while
you iterate. It returns folio headers of every status, open and closed alike, which is
why legacy reports still see archived folios. `SearchFolio` replaces it with the same
paged envelope, plus criteria on name, owner, currency and dates, plus explicit sorting.
The route answers 410 from the sunset date carried on the response header, and it is not
carried over to 3.0. Requires the `folio:read` scope.

## Parameters
| Name | In | Type | Required | Description |
|---|---|---|---|---|
| owner | query | string | no | Filter on the owner of the folio, exact match, e.g. `desk-fx-options`. |
| page | query | integer | no | Page number, 1-based. Defaults to 1. |
| pageSize | query | integer | no | Items per page. Defaults to 50, maximum 500. |
| X-Correlation-Id | header | string | no | Client correlation identifier, echoed back on the response. |

## Response
Every response carries a `Deprecation: true` header and the RFC 8594
`Sunset: Fri, 26 Mar 2027 00:00:00 GMT` header; from that date the route answers 410 and
only `SearchFolio` remains. The body is the standard paged envelope of folio headers,
without positions or exposures.

| Status | Meaning |
|---|---|
| 200 | Page of folio headers. |
| 400 | Invalid `page` or `pageSize`, or `pageSize` above 500. |
| 401 | Missing or expired access token. |
| 403 | Token lacks the `folio:read` scope. |
| 410 | Route switched off on the sunset date; call `SearchFolio` instead. |
| 429 | Rate limit exceeded; retry after `Retry-After` seconds. |

```json
{
  "items": [
    {
      "folioId": "FOL-000123",
      "name": "Vega Book EUR",
      "owner": "desk-fx-options",
      "currency": "EUR",
      "status": "open",
      "positionCount": 42
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

using var http = new HttpClient { BaseAddress = new Uri("https://api.example.internal/") };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
http.DefaultRequestHeaders.Add("X-Correlation-Id", Guid.NewGuid().ToString());

using var response = await http.GetAsync("folios?owner=desk-fx-options&page=1&pageSize=50");
response.EnsureSuccessStatusCode();

if (response.Headers.TryGetValues("Sunset", out var sunset))
{
    Console.Error.WriteLine($"ListFolios is deprecated, sunset on {string.Join(" ", sunset)}.");
}

var envelope = await response.Content.ReadFromJsonAsync<JsonNode>()
               ?? throw new InvalidOperationException("Empty response body.");

Console.WriteLine($"Page {envelope["page"]} of {envelope["totalCount"]} folios.");
foreach (var item in envelope["items"]?.AsArray() ?? new JsonArray())
{
    Console.WriteLine($"{item?["folioId"]} {item?["name"]} {item?["status"]}");
}
```

## Example Python
```python
import os
import uuid

import requests

BASE_URL = "https://api.example.internal"

token = os.environ["FEDERER_API_TOKEN"]
headers = {
    "Authorization": f"Bearer {token}",
    "Accept": "application/json",
    "X-Correlation-Id": str(uuid.uuid4()),
}
params = {"owner": "desk-fx-options", "page": 1, "pageSize": 50}

response = requests.get(
    f"{BASE_URL}/folios",
    params=params,
    headers=headers,
    timeout=30,
)
response.raise_for_status()

sunset = response.headers.get("Sunset")
if sunset:
    print(f"ListFolios is deprecated, sunset on {sunset}.")

envelope = response.json()
print(f"Page {envelope['page']} of {envelope['totalCount']} folios.")
for folio in envelope["items"]:
    print(f"{folio['folioId']} {folio['name']} {folio['status']}")
```

## Notes
Deprecated since platform version 2.2, switched off on the sunset date and not carried
over to 3.0. Migrate to `SearchFolio`: it answers with the same paged envelope, takes the
criteria in a JSON body instead of the query string, and lets you sort the result. Keep
`GetFolioById` for single reads; calling this endpoint page by page to find one folio
wastes the 600 requests per minute quota.
