---
formatVersion: "1.0"
domain: Volatility
operationId: ComputeImpliedVol
method: POST
route: /volatility/implied-vol
version: "2.3"
summary: Compute the implied volatility of a strike and maturity from a surface.
tags: [pricing, interpolation]
keywords: >-
  compute computes computing implied vol volatility interpolate interpolates
  interpolating interpolation extrapolate extrapolates extrapolation evaluate
  evaluates evaluating query queries querying point points strike strikes
  strikeType maturity maturities tenor tenors moneyness delta cubic linear
  surface surfaces pricing revalue marking batch
  surfaceId applyPlugs allowExtrapolation
  calculer calcul volatilité implicite interpoler nappe échéance évaluer
aliases: [interpolate, evaluate, query]
related: [GetSurface, GetFolioPositions]
deprecated: false
---

## Description

This is the read-only pricing helper of the domain: it never mutates the surface and only needs the
`volatility:read` scope. Send up to 500 points in a single call; a folio marking job typically lists the
option legs with `GetFolioPositions`, groups them by underlying, then sends one batch per surface here to
revalue them. Results are deterministic for a given surface version, so a replay produces identical vols.
Active plugs are included unless `applyPlugs` is false, which evaluates the raw calibrated surface. A point
outside the tenor or strike grid is rejected with 422 unless `allowExtrapolation` is true. Vols are returned in
volatility points, so 18.75 means 18.75 percent.

## Parameters

| Name | In | Type | Required | Description |
|---|---|---|---|---|
| surfaceId | body | string | yes | Identifier of the surface to evaluate, e.g. `VOL-EURUSD-2026-08-28`. |
| points | body | array | yes | Points to evaluate, 1 to 500 entries. Each entry is an object built from the three `points[].*` members below. Results keep the request order. |
| points[].maturity | body | string | yes | Member of each `points` entry, required there, not a top-level field. ISO 8601 date such as `2026-11-28`, or ISO 8601 duration such as `P3M`. |
| points[].strike | body | number | yes | Member of each `points` entry, required there, not a top-level field. Strike value, interpreted according to `points[].strikeType`. |
| points[].strikeType | body | string | no | Optional member of each `points` entry. One of `absolute`, `delta`, `moneyness`. Defaults to `absolute`; send the `strikeType` carried by the surface points. A `delta` strike is a call delta in [0, 1]. |
| interpolation | body | string | no | Strike interpolation scheme, `linear` or `cubic`. Defaults to `cubic`. |
| applyPlugs | body | boolean | no | Include the active plugs of the surface. Defaults to true. |
| allowExtrapolation | body | boolean | no | Allow points outside the grid instead of failing. Defaults to false. |
| X-Business-Date | header | string | no | Business date used to resolve the surface, ISO date. Defaults to today. |

## Response

| Status | Meaning |
|---|---|
| 200 | Vols computed; one result per requested point, in request order. |
| 400 | Malformed body, unknown strikeType, or more than 500 points. |
| 401 | Missing or expired bearer token. |
| 403 | Token lacks the `volatility:read` scope. |
| 404 | Surface not found for this id and business date. |
| 422 | A point falls outside the grid and allowExtrapolation is false. |
| 429 | Rate limit exceeded; retry after the `Retry-After` delay. |

```json
{
  "surfaceId": "VOL-EURUSD-2026-08-28",
  "results": [
    { "maturity": "P3M", "strike": 1.0, "strikeType": "moneyness", "vol": 18.75, "extrapolated": false },
    { "maturity": "P1Y", "strike": 1.05, "strikeType": "moneyness", "vol": 17.42, "extrapolated": false }
  ]
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
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
http.DefaultRequestHeaders.Add("X-Business-Date", "2026-08-28");

var body = new
{
    surfaceId = "VOL-EURUSD-2026-08-28",
    points = new[]
    {
        new { maturity = "P3M", strike = 1.0, strikeType = "moneyness" },
        new { maturity = "P1Y", strike = 1.05, strikeType = "moneyness" }
    },
    interpolation = "cubic",
    applyPlugs = true,
    allowExtrapolation = false
};

using var response = await http.PostAsJsonAsync("/volatility/implied-vol", body);
response.EnsureSuccessStatusCode();

var payload = await response.Content.ReadFromJsonAsync<JsonNode>()
              ?? throw new InvalidOperationException("Empty response body.");

foreach (var point in payload["results"]?.AsArray() ?? new JsonArray())
{
    Console.WriteLine($"{point?["maturity"]} @ {point?["strike"]} -> {point?["vol"]} vol points " +
                      $"(extrapolated={point?["extrapolated"]})");
}
```

## Example Python

```python
import os

import requests

BASE_URL = "https://api.example.internal"

token = os.environ["FEDERER_API_TOKEN"]
headers = {
    "Authorization": f"Bearer {token}",
    "Accept": "application/json",
    "Content-Type": "application/json",
    "X-Business-Date": "2026-08-28",
}
payload = {
    "surfaceId": "VOL-EURUSD-2026-08-28",
    "points": [
        {"maturity": "P3M", "strike": 1.0, "strikeType": "moneyness"},
        {"maturity": "P1Y", "strike": 1.05, "strikeType": "moneyness"},
    ],
    "interpolation": "cubic",
    "applyPlugs": True,
    "allowExtrapolation": False,
}

response = requests.post(
    f"{BASE_URL}/volatility/implied-vol",
    headers=headers,
    json=payload,
    timeout=15,
)
response.raise_for_status()

for point in response.json()["results"]:
    print(point["maturity"], point["strike"], point["vol"], "extrapolated:", point["extrapolated"])
```

## Notes

Interpolation is cubic in the strike dimension and linear in the total variance along the time dimension, which
keeps the term structure arbitrage free between two calibrated tenors. Extrapolation is flat beyond the last
tenor and beyond the outermost strikes, so an extrapolated result repeats the nearest grid vol and is flagged
with `extrapolated` set to true. Batch aggressively: one call with 500 points costs the same single unit of the
600 requests per minute budget as one call with a single point.
