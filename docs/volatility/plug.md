---
formatVersion: "1.0"
domain: Volatility
operationId: Plug
method: POST
route: /volatility/plug
version: "2.3"
summary: Apply a plug (manual bump) to an existing volatility surface.
tags: [surface, calibration]
keywords: >-
  plug plugs plugging plugged bump bumps bumping bumped shift shifts shifting
  override overrides overriding apply applies applying manual trader adjustment
  surface surfaces volatility vol point points grid tenor strike calibration
  calibrated audit audited idempotency expiry moneyness delta absolute
  surfaceId tenor strike strikeType shift reason expiresAt plugId version
  plug bumper décaler surface volatilité nappe appliquer ajustement décalage
aliases: [bump, shift, override]
related: [GetSurface, DeletePlug, Recalibrate]
deprecated: false
---

## Description
Use this when a trader disagrees with the calibrated value of one grid point and
wants to override it without running a full calibration. The target surface must
exist and must not be locked by a running calibration, and both the tenor and the
strike must belong to the surface grid, otherwise the call is rejected with 422:
take them from the `tenors` and `strikes` axes returned by `GetSurface`, where a
strike is a number such as `1.0`, never a label such as `25D`. The plug increments
the surface version and is visible on the next read, so any consumer holding an
older version must refetch it with `GetSurface`. A plug does not survive a
calibration: `Recalibrate` drops every plug unless it is called with keepPlugs set
to true. Plugs are audited, appliedBy is taken from the access token subject and
never from the payload, and a plug can be reverted with `DeletePlug`. Replaying the
same Idempotency-Key returns the original plug instead of creating a second one.

## Parameters
| Name | In | Type | Required | Description |
|---|---|---|---|---|
| surfaceId | body | string | yes | Identifier of the target surface, e.g. `VOL-EURUSD-2026-08-28`. |
| tenor | body | string | yes | Grid tenor as an ISO 8601 duration, e.g. `P1M`, `P3M`, `P1Y`. |
| strike | body | number | yes | Grid strike, taken from the `strikes` axis returned by `GetSurface`, e.g. `1.0`. Interpreted according to `strikeType`. |
| strikeType | body | string | no | How `strike` is expressed: `absolute`, `delta` or `moneyness`. Defaults to `absolute`; send the `strikeType` carried by the surface points. |
| shift | body | number | yes | Absolute shift applied to the calibrated value, in volatility points. Can be negative. |
| reason | body | string | no | Free text justification kept in the audit trail. Max 500 characters. |
| expiresAt | body | string | no | ISO 8601 UTC instant after which the plug is ignored. Omitted means no expiry. |
| Idempotency-Key | header | string | no | Client-generated key that makes a retried creation return the original plug. |

## Response
| Status | Meaning |
|---|---|
| 200 | Plug applied; returns the plug id and the new surface version. |
| 400 | Malformed body, unknown field, unknown strikeType, or shift is not a number. |
| 401 | Missing or expired access token. |
| 403 | Token lacks the volatility:write scope. |
| 404 | Surface not found for the given surfaceId. |
| 409 | Surface locked by a running calibration; retry once it completes. |
| 422 | Tenor or strike outside the surface grid. |
| 429 | Rate limit exceeded; see the Retry-After header, in seconds. |

```json
{
  "plugId": "PLG-0004212",
  "surfaceId": "VOL-EURUSD-2026-08-28",
  "tenor": "P3M",
  "strike": 1.0,
  "strikeType": "moneyness",
  "shift": 0.25,
  "version": 12,
  "appliedBy": "svc-vol-desk",
  "appliedAt": "2026-08-28T10:00:00Z"
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

var body = new
{
    surfaceId = "VOL-EURUSD-2026-08-28",
    tenor = "P3M",
    strike = 1.0,
    strikeType = "moneyness",
    shift = 0.25,
    reason = "Broker straddle quoted above model mid",
    expiresAt = "2026-08-29T18:00:00Z"
};

using var request = new HttpRequestMessage(HttpMethod.Post, "/volatility/plug")
{
    Content = JsonContent.Create(body)
};
request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

using var response = await http.SendAsync(request);
response.EnsureSuccessStatusCode();

var plug = await response.Content.ReadFromJsonAsync<JsonNode>();
Console.WriteLine($"plugId    : {plug!["plugId"]}");
Console.WriteLine($"surfaceId : {plug["surfaceId"]}");
Console.WriteLine($"strike    : {plug["strike"]} ({plug["strikeType"]})");
Console.WriteLine($"version   : {plug["version"]}");
Console.WriteLine($"appliedAt : {plug["appliedAt"]}");
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
    "Content-Type": "application/json",
    "Idempotency-Key": str(uuid.uuid4()),
}

payload = {
    "surfaceId": "VOL-EURUSD-2026-08-28",
    "tenor": "P3M",
    "strike": 1.0,
    "strikeType": "moneyness",
    "shift": 0.25,
    "reason": "Broker straddle quoted above model mid",
    "expiresAt": "2026-08-29T18:00:00Z",
}

response = requests.post(
    f"{BASE_URL}/volatility/plug", json=payload, headers=headers, timeout=30
)
response.raise_for_status()

plug = response.json()
print("plugId    :", plug["plugId"])
print("surfaceId :", plug["surfaceId"])
print("strike    :", plug["strike"], f"({plug['strikeType']})")
print("version   :", plug["version"])
print("appliedAt :", plug["appliedAt"])
```

## Notes
Requires the volatility:write scope; a read-only token gets 403. A plug applied to
a published surface is visible immediately to every consumer, including downstream
pricing jobs, so prefer a draft surface for exploratory bumps. Shifts are expressed
in volatility points: the 0.25 above moves the calibrated 18.50 of the 3M 1.0 point
to the 18.75 that `GetSurface` then reports for it, plugs included.
