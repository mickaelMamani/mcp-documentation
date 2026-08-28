---
formatVersion: "1.0"
domain: Folio
operationId: GetFolioPositions
method: GET
route: /folios/{folioId}/positions
version: "2.3"
summary: Get the positions of a folio, with paging and instrument filters.
tags: [folio, positions]
keywords: >-
  position positions holding holdings line lines constituent constituents
  get gets getting fetch fetches fetching retrieve retrieves retrieving list lists listing
  folio folios portfolio portfolios instrument instruments underlying maturity maturities
  option options forward forwards cash quantity notional delta vega marketValue
  folioId positionId instrumentId instrumentType maturityFrom maturityTo page pageSize
  businessDate business date asOf header headers
  paging pagination paged snapshot frozen
  position lignes portefeuille titres échéance récupérer lister obtenir consulter
aliases: [holdings, lines, constituents]
related: [GetFolioById, ListSurfaces, ComputeImpliedVol]
deprecated: false
---

## Description
Use this when you need the individual lines of a folio rather than the aggregated exposures returned by
`GetFolioById`. Requires the `folio:read` scope on a folio visible to the token subject. It is paginated by
design: a market-making folio holds tens of thousands of lines, so always loop until
`page * pageSize >= totalCount` instead of assuming a single page. Closed folios still return their frozen
positions, so this is the right call for historical reporting. `delta` is the per-unit option delta in [-1, 1],
positive on a call and negative on a put; `marketValue` and `vega` are amounts in the line `currency`, vega per
volatility point. `vega` is `null` on forward and cash lines, so summing that column blindly understates the
book. A line carries no strike: to revalue the option lines, resolve their `underlying` to a `surfaceId` with
`ListSurfaces`, then send each `maturityDate` as `points[].maturity` to `ComputeImpliedVol`, with the line
`delta` converted to a call delta (`1 + delta` on a put line) as `points[].strike` and `points[].strikeType`
set to `delta`.

## Parameters
| Name | In | Type | Required | Description |
|---|---|---|---|---|
| folioId | path | string | yes | Identifier of the folio, e.g. `FOL-000123`. |
| instrumentType | query | string | no | Filter on the instrument type: `option`, `forward` or `cash`. Omit for all types. |
| underlying | query | string | no | Filter on the underlying code, e.g. `EURUSD`. Exact match, case sensitive. |
| maturityFrom | query | string | no | Inclusive lower bound on `maturityDate`, as a business date (2026-09-01). |
| maturityTo | query | string | no | Inclusive upper bound on `maturityDate`, as a business date (2026-12-31). |
| page | query | integer | no | 1-based page number. Defaults to 1. |
| pageSize | query | integer | no | Number of positions per page. Defaults to 50, maximum 500. |
| X-Business-Date | header | string | no | As-of business date for the position snapshot. Defaults to today. |

## Response
| Status | Meaning |
|---|---|
| 200 | Paged envelope of positions; empty `items` when the folio holds no matching line. |
| 400 | Invalid paging or malformed `maturityFrom` / `maturityTo`. |
| 401 | Missing or expired access token. |
| 403 | The `folio:read` scope is missing or the folio belongs to another desk. |
| 404 | Folio not found for the requested business date. |
| 429 | Rate limit of 600 requests per minute exceeded; retry after `Retry-After` seconds. |

```json
{
  "items": [
    {
      "positionId": "POS-0009981",
      "instrumentId": "OPT-EURUSD-20261218-1.1000-C",
      "instrumentType": "option",
      "underlying": "EURUSD",
      "quantity": 250,
      "notional": 25000000,
      "currency": "EUR",
      "tradeDate": "2026-06-15",
      "maturityDate": "2026-12-18",
      "marketValue": 412350.75,
      "delta": 0.42,
      "vega": 18750.5
    }
  ],
  "page": 1,
  "pageSize": 50,
  "totalCount": 17
}
```

## Example C#
```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;

var token = Environment.GetEnvironmentVariable("FEDERER_API_TOKEN")
            ?? throw new InvalidOperationException("FEDERER_API_TOKEN is not set.");

using var http = new HttpClient { BaseAddress = new Uri("https://api.example.internal") };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
http.DefaultRequestHeaders.Add("X-Business-Date", "2026-08-28");

const string folioId = "FOL-000123";
var url = $"/folios/{folioId}/positions?instrumentType=option&underlying=EURUSD&page=1&pageSize=50";

using var response = await http.GetAsync(url);
response.EnsureSuccessStatusCode();

var payload = await response.Content.ReadFromJsonAsync<PositionPage>()
              ?? throw new InvalidOperationException("Empty response body.");

Console.WriteLine($"Page {payload.Page} of {payload.TotalCount} positions");
foreach (var p in payload.Items)
{
    // Delta is per unit: the delta-equivalent notional of the line is Notional * Delta.
    Console.WriteLine($"{p.PositionId} {p.Underlying} maturity={p.MaturityDate} delta={p.Delta} " +
                      $"vega={p.Vega} deltaNotional={p.Notional * p.Delta} {p.Currency}");
}

record PositionPage(Position[] Items, int Page, int PageSize, int TotalCount);
record Position(string PositionId, string InstrumentType, string Underlying, string MaturityDate,
                decimal Notional, string Currency, decimal Delta, decimal? Vega);
```

## Example Python
```python
import os

import requests

BASE_URL = "https://api.example.internal"
FOLIO_ID = "FOL-000123"

token = os.environ["FEDERER_API_TOKEN"]
headers = {
    "Authorization": f"Bearer {token}",
    "Accept": "application/json",
    "X-Business-Date": "2026-08-28",
}
params = {
    "instrumentType": "option",
    "underlying": "EURUSD",
    "maturityFrom": "2026-09-01",
    "page": 1,
    "pageSize": 50,
}

response = requests.get(
    f"{BASE_URL}/folios/{FOLIO_ID}/positions", headers=headers, params=params, timeout=30
)
response.raise_for_status()

payload = response.json()
print(f"Page {payload['page']} of {payload['totalCount']} positions")

for item in payload["items"]:
    # delta is per unit; the delta-equivalent notional of the line is notional * delta
    delta_notional = item["notional"] * item["delta"]
    print(
        f"{item['positionId']} {item['underlying']} maturity={item['maturityDate']} "
        f"delta={item['delta']} vega={item['vega']} "
        f"deltaNotional={delta_notional} {item['currency']}"
    )
```

## Notes
Positions are a snapshot taken at `X-Business-Date`, not a live view: intraday trades booked
today appear with a delay of up to 5 minutes. Send `X-Correlation-Id` when paging through a large
folio so that every page shares the same trace in the platform logs. The `delta` column is per unit and
carries no position direction, which lives in `quantity`: the delta-equivalent notional of a line is
`notional * delta`, expressed in the line `currency`. Do not sum that column to reconcile against the
aggregated `exposures.delta` of `GetFolioById`, which is a monetary amount in the folio `currency` and can
therefore use a different currency from the line.
