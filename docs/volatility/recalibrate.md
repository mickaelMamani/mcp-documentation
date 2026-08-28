---
formatVersion: "1.0"
domain: Volatility
operationId: Recalibrate
method: POST
route: /volatility/surfaces/{surfaceId}/recalibrate
version: "2.3"
summary: Update a volatility surface by recalibrating it from the latest market quotes.
tags: [calibration]
keywords: >-
  recalibrate recalibrates recalibrating recalibration calibrate calibrates
  calibrating calibration refit refits refitting rebuild rebuilds rebuilding
  fit fitting solver surface surfaces volatility vol quote quotes market
  snapshot model SVI SABR rmse iterations lock locked plugs version versions
  idempotency key replay retry retries timeout timeouts quota audit
  surfaceId quoteSource asOf keepPlugs maxIterations
  recalibrer recalibrage calibrer calibrage ajuster nappe volatilité cotations
aliases: [refit, calibrate, rebuild]
related: [GetSurface, DeletePlug, ComputeImpliedVol, Plug]
deprecated: false
---

## Description

Use this when new market quotes are available and the fitted parameters of the surface are stale, or after an
incident that corrupted a fit. The call is synchronous but slow: expect 2 to 10 seconds, so set the client
timeout to at least 30 seconds. For the duration of the fit the surface status becomes `locked`; concurrent
`Plug` calls and a second recalibration on the same surface are rejected with 409. Existing plugs are dropped
unless `keepPlugs` is true, so inspect them with `GetSurface` before calling. A successful fit increments the
surface version; earlier versions are kept for audit only and are not readable through the API, so a bad fit is
corrected by another calibration on a better quote snapshot, never by restoring the previous version. Requires
the `volatility:write` scope. A `calibrationRmse` above 0.35 indicates a poor fit and should be reviewed before
the surface is published.

## Parameters

| Name | In | Type | Required | Description |
|---|---|---|---|---|
| surfaceId | path | string | yes | Identifier of the surface to recalibrate, e.g. `VOL-EURUSD-2026-08-28`; resolve it with `ListSurfaces`. |
| quoteSource | body | string | no | Quote provider used for the fit. Defaults to `BROKER_A`. |
| asOf | body | string | no | ISO 8601 UTC instant of the quote snapshot to fit. Defaults to now. |
| model | body | string | no | Calibration model, `SVI` or `SABR`. Defaults to the model stored on the surface. |
| keepPlugs | body | boolean | no | Reapply the existing plugs after the fit instead of dropping them. Defaults to false. |
| maxIterations | body | integer | no | Upper bound on solver iterations, between 10 and 2000. Defaults to 200. |
| Idempotency-Key | header | string | no | Client-generated key; a replay of the same key returns the result of the first calibration instead of starting a second one. |
| X-Correlation-Id | header | string | no | Optional client correlation id, echoed back in the response headers. |

## Response

| Status | Meaning |
|---|---|
| 200 | Surface recalibrated; returns the new version and the fit quality. |
| 400 | Malformed body, unknown model, or maxIterations out of range. |
| 401 | Missing or expired bearer token. |
| 403 | Token lacks the `volatility:write` scope. |
| 404 | Surface not found. |
| 409 | A calibration is already running on this surface. |
| 422 | Not enough quotes to fit: fewer than 5 per tenor. |
| 429 | Rate limit exceeded; retry after the `Retry-After` delay. |

```json
{
  "surfaceId": "VOL-EURUSD-2026-08-28",
  "version": 13,
  "model": "SVI",
  "calibratedAt": "2026-08-28T10:00:00Z",
  "calibrationRmse": 0.21,
  "quotesUsed": 148,
  "quotesDropped": 6
}
```

## Example C#

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

var token = Environment.GetEnvironmentVariable("FEDERER_API_TOKEN")
            ?? throw new InvalidOperationException("FEDERER_API_TOKEN is not set.");

var surfaceId = "VOL-EURUSD-2026-08-28";
var asOf = "2026-08-28T10:00:00Z";

using var http = new HttpClient { BaseAddress = new Uri("https://api.example.internal") };
http.Timeout = TimeSpan.FromSeconds(30);
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
http.DefaultRequestHeaders.Add("X-Correlation-Id", Guid.NewGuid().ToString());
http.DefaultRequestHeaders.Add("Idempotency-Key", $"recal-{surfaceId}-{asOf}");

var body = new
{
    quoteSource = "BROKER_A",
    asOf,
    model = "SVI",
    keepPlugs = false,
    maxIterations = 200
};

using var response = await http.PostAsJsonAsync($"/volatility/surfaces/{surfaceId}/recalibrate", body);
response.EnsureSuccessStatusCode();

var result = await response.Content.ReadFromJsonAsync<JsonNode>()
             ?? throw new InvalidOperationException("Empty response body.");

Console.WriteLine($"version   : {result["version"]} ({result["model"]})");
Console.WriteLine($"rmse      : {result["calibrationRmse"]}");
Console.WriteLine($"quotes    : {result["quotesUsed"]} used, {result["quotesDropped"]} dropped");
```

## Example Python

```python
import os
import uuid

import requests

BASE_URL = "https://api.example.internal"
SURFACE_ID = "VOL-EURUSD-2026-08-28"
AS_OF = "2026-08-28T10:00:00Z"

token = os.environ["FEDERER_API_TOKEN"]
headers = {
    "Authorization": f"Bearer {token}",
    "Accept": "application/json",
    "Content-Type": "application/json",
    "X-Correlation-Id": str(uuid.uuid4()),
    "Idempotency-Key": f"recal-{SURFACE_ID}-{AS_OF}",
}
payload = {
    "quoteSource": "BROKER_A",
    "asOf": AS_OF,
    "model": "SVI",
    "keepPlugs": False,
    "maxIterations": 200,
}

response = requests.post(
    f"{BASE_URL}/volatility/surfaces/{SURFACE_ID}/recalibrate",
    headers=headers,
    json=payload,
    timeout=30,
)
response.raise_for_status()

result = response.json()
print("version   :", result["version"], result["model"])
print("rmse      :", result["calibrationRmse"])
print("quotes    :", result["quotesUsed"], "used,", result["quotesDropped"], "dropped")
```

## Notes

Each successful call consumes one unit of the calibration quota, 30 per hour per client, counted independently
from the global limit of 600 requests per minute. Prefer the nightly batch and keep ad-hoc calls for intraday
incidents. If the call times out, do not retry it blind: re-read the surface with `GetSurface` and compare
`version` and `calibratedAt` with the values you held before the call; a higher version means the fit landed, and
a second call would burn another quota unit and drop the plugs again. A retry sent while the first fit is still
running answers 409, even with the same `Idempotency-Key`; once that fit has completed, replaying the key returns
its result instead of starting a new one. Error bodies follow RFC 7807 `application/problem+json`; a 409 carries
the running calibration id in `detail`, which is meant for logs and support, as no endpoint takes it as an input.
Recalibrating on `https://api.uat.example.internal` uses the UAT quote store and never touches production
surfaces.
