---
formatVersion: "1.0"
domain: Volatility
operationId: ListSurfaces
method: GET
route: /volatility/surfaces
version: "2.3"
summary: List the volatility surfaces available for an underlying and a business date.
tags: [surface, catalog]
keywords: >-
  list lists listing listed surface surfaces catalog catalogs inventory
  available availability browse discover find search searches searching lookup
  volatility vol vols underlying assetClass businessDate status version model
  currency calibratedAt calibrationRmse page pageSize paging paged fx equity rates
  draft published locked header headers
  nappe nappes volatilité lister liste rechercher catalogue disponibles
aliases: [surfaces, catalog, inventory]
related: [GetSurface, Recalibrate]
deprecated: false
---

## Description
Start here when you do not know the `surfaceId` yet: this is the discovery entry
point of the volatility domain and its cheapest call, because it returns surface
headers only, never the tenor and strike grid. Feed the returned `surfaceId` into
`GetSurface` or `Recalibrate`. Results are ordered by `businessDate` descending,
then by `surfaceId` ascending. A surface can exist in several versions after
successive calibrations; only the latest version of each surface is returned, so
the response never contains two entries with the same `surfaceId`. Requires the
`volatility:read` scope. The `underlying` filter is mandatory: an unfiltered
catalogue scan is not supported and there is no wildcard value.

## Parameters
| Name | In | Type | Required | Description |
|---|---|---|---|---|
| underlying | query | string | yes | Underlying code to list surfaces for, e.g. `EURUSD`. Exact match, case sensitive. |
| assetClass | query | string | no | Restricts the result to one asset class: `fx`, `equity` or `rates`. Defaults to all. |
| businessDate | query | string | no | Business date in ISO format, e.g. `2026-08-28`. Defaults to the `X-Business-Date` header, then to today. |
| status | query | string | no | Lifecycle filter: `draft`, `published` or `locked`. Defaults to all statuses. |
| page | query | integer | no | 1-based page number. Defaults to 1. |
| pageSize | query | integer | no | Number of surfaces per page. Defaults to 50, maximum 500. |
| X-Correlation-Id | header | string | no | Client correlation identifier, echoed back in the response headers. |

## Response
| Status | Meaning |
|---|---|
| 200 | Paged list of surface headers; `items` is empty when nothing matches. |
| 400 | Missing `underlying`, unknown `assetClass`, unknown `status` or `pageSize` above 500. |
| 401 | Missing or expired access token. |
| 403 | Token does not carry the `volatility:read` scope. |
| 429 | Rate limit of 600 requests per minute exceeded; retry after `Retry-After` seconds. |

```json
{
  "items": [
    {
      "surfaceId": "VOL-EURUSD-2026-08-28",
      "underlying": "EURUSD",
      "assetClass": "fx",
      "businessDate": "2026-08-28",
      "model": "SVI",
      "currency": "EUR",
      "status": "published",
      "version": 12,
      "calibratedAt": "2026-08-28T06:15:00Z",
      "calibrationRmse": 0.18
    }
  ],
  "page": 1,
  "pageSize": 50,
  "totalCount": 1
}
```

`calibrationRmse` is the fit residual in volatility points, the same unit as the
vols themselves, so `0.18` is 0.18 vol point; `Recalibrate` treats a value above
0.35 as a poor fit to review before the surface is published.

## Example C#
```csharp
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

var token = Environment.GetEnvironmentVariable("FEDERER_API_TOKEN")
            ?? throw new InvalidOperationException("FEDERER_API_TOKEN is not set.");

using var http = new HttpClient { BaseAddress = new Uri("https://api.example.internal") };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
http.DefaultRequestHeaders.Add("X-Correlation-Id", Guid.NewGuid().ToString());

var url = "/volatility/surfaces?underlying=EURUSD&assetClass=fx&status=published&page=1&pageSize=50";
using var response = await http.GetAsync(url);
response.EnsureSuccessStatusCode();

var payload = await response.Content.ReadFromJsonAsync<JsonNode>();
var items = payload?["items"]?.AsArray() ?? new JsonArray();
var totalCount = payload?["totalCount"];
Console.WriteLine($"Surfaces found: {totalCount}");

foreach (var item in items)
{
    var surfaceId = item?["surfaceId"];
    var businessDate = item?["businessDate"];
    var model = item?["model"];
    var rmse = item?["calibrationRmse"];
    Console.WriteLine($"{surfaceId} {businessDate} model={model} rmse={rmse} vol points");
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
    "X-Correlation-Id": str(uuid.uuid4()),
}

params = {
    "underlying": "EURUSD",
    "assetClass": "fx",
    "status": "published",
    "page": 1,
    "pageSize": 50,
}

url = f"{BASE_URL}/volatility/surfaces"
response = requests.get(url, headers=headers, params=params, timeout=30)
response.raise_for_status()

payload = response.json()
print(f"Surfaces found: {payload['totalCount']}")

for item in payload["items"]:
    line = "{} {} model={} rmse={} vol points".format(
        item["surfaceId"], item["businessDate"], item["model"], item["calibrationRmse"]
    )
    print(line)
```
