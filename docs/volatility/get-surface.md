---
formatVersion: "1.0"
domain: Volatility
operationId: GetSurface
method: GET
route: /volatility/surfaces/{surfaceId}
version: "2.3"
summary: Get one volatility surface with its tenor and strike grid.
tags: [surface]
keywords: >-
  get gets getting fetch fetches fetching read reads reading load loads loading
  retrieve retrieves retrieving surface surfaces grid grids point points curve
  volatility vol vols smile skew tenor tenors strike strikes strikeType moneyness
  plug plugs plugged includePlugs surfaceId underlying assetClass businessDate
  model currency status version calibratedAt calibrationRmse locked published
  nappe nappes volatilité récupérer lire charger échéance grille
aliases: [fetch, read, load]
related: [ListSurfaces, Plug, ComputeImpliedVol]
deprecated: false
---

## Description
Use this call once the `surfaceId` is known, typically from `ListSurfaces`. The
payload can be large: a full fx grid is 5 strikes by 12 tenors, so 60 points plus
the plug list, and requesting several surfaces in a loop is the usual cause of
slow clients. Pass `tenors` to restrict the grid to the maturities you actually
price; the strike axis is never filtered. Set `includePlugs=false` to obtain the
pure calibrated surface, without any manual adjustment. The call is read only and
has no side effect on the calibration. Requires the `volatility:read` scope. To
interpolate a single point instead of downloading the grid, prefer
`ComputeImpliedVol`.

## Parameters
| Name | In | Type | Required | Description |
|---|---|---|---|---|
| surfaceId | path | string | yes | Identifier of the surface to read, e.g. `VOL-EURUSD-2026-08-28`. |
| tenors | query | string | no | Comma separated ISO 8601 durations used to filter the grid, e.g. `P1M,P3M,P1Y`. Defaults to all tenors. |
| includePlugs | query | boolean | no | Whether applied plugs are returned and reflected in the points. Defaults to true. |
| X-Business-Date | header | string | no | As-of business date in ISO format, e.g. `2026-08-28`. Defaults to today. |

## Response
| Status | Meaning |
|---|---|
| 200 | Full surface: the `ListSurfaces` header fields, plus the tenor axis, strike axis, points and plugs. |
| 400 | Malformed `surfaceId`, unparsable `tenors` value or non boolean `includePlugs`. |
| 401 | Missing or expired access token. |
| 403 | Token does not carry the `volatility:read` scope. |
| 404 | Surface not found for this identifier and business date. |
| 429 | Rate limit exceeded; retry after `Retry-After` seconds. |

```json
{
  "surfaceId": "VOL-EURUSD-2026-08-28",
  "businessDate": "2026-08-28",
  "model": "SVI",
  "status": "published",
  "version": 12,
  "tenors": ["P1M", "P3M", "P1Y"],
  "strikes": [0.9, 1.0, 1.1],
  "points": [{ "tenor": "P3M", "strike": 1.0, "strikeType": "moneyness", "vol": 18.75 }],
  "plugs": [{ "plugId": "PLG-0004212", "tenor": "P3M", "strike": 1.0, "shift": 0.25 }]
}
```

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
http.DefaultRequestHeaders.Add("X-Business-Date", "2026-08-28");

var surfaceId = "VOL-EURUSD-2026-08-28";
var url = $"/volatility/surfaces/{surfaceId}?tenors=P1M,P3M,P1Y&includePlugs=true";
using var response = await http.GetAsync(url);
response.EnsureSuccessStatusCode();

var surface = await response.Content.ReadFromJsonAsync<JsonNode>();
var model = surface?["model"];
var plugCount = surface?["plugs"]?.AsArray().Count ?? 0;
Console.WriteLine($"{surfaceId} model={model} plugs={plugCount}");

foreach (var point in surface?["points"]?.AsArray() ?? new JsonArray())
{
    var tenor = point?["tenor"];
    var strike = point?["strike"];
    var vol = point?["vol"];
    Console.WriteLine($"{tenor} strike={strike} vol={vol}");
}
```

## Example Python
```python
import os

import requests

BASE_URL = "https://api.example.internal"
TOKEN = os.environ["FEDERER_API_TOKEN"]
SURFACE_ID = "VOL-EURUSD-2026-08-28"

headers = {
    "Authorization": f"Bearer {TOKEN}",
    "Accept": "application/json",
    "X-Business-Date": "2026-08-28",
}

params = {
    "tenors": "P1M,P3M,P1Y",
    "includePlugs": "true",
}

url = f"{BASE_URL}/volatility/surfaces/{SURFACE_ID}"
response = requests.get(url, headers=headers, params=params, timeout=30)
response.raise_for_status()

surface = response.json()
print(SURFACE_ID, "model=" + surface["model"], "plugs=" + str(len(surface["plugs"])))

for point in surface["points"]:
    print(point["tenor"], "strike=" + str(point["strike"]), "vol=" + str(point["vol"]))
```

## Notes
The values in `points` already include every applied plug, so they are the values
used for pricing; call with `includePlugs=false` to compare them against the raw
calibrated surface. A surface whose `status` is `locked` remains fully readable:
locking only blocks writes such as `Plug` and `Recalibrate`.
