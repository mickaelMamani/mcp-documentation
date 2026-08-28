---
formatVersion: "1.0"
domain: Volatility
operationId: DeletePlug
method: DELETE
route: /volatility/plugs/{plugId}
version: "2.3"
summary: Delete a plug and restore the calibrated values of the surface.
tags: [surface, calibration]
keywords: >-
  delete deletes deleting deleted remove removes removing removed unplug unplugs
  unplugging revert reverts reverting rollback restore restores restoring cancel
  plug plugs bump bumps shift shifts surface surfaces volatility vol grid point
  calibrated calibration idempotent audit trail permanent recompute recomputation
  batch batched deferred pending stale outstanding
  plugId recompute correlation version
  supprimer suppression retirer annuler rétablir plug nappe surface volatilité
aliases: [unplug, revert, remove]
related: [Plug, GetSurface, Recalibrate]
deprecated: false
---

## Description
Use this to undo a manual bump once the market has moved back to the model, or when
a plug was applied to the wrong grid point. Removing a plug never rewrites calibrated
values: the plugged point returns to the value produced by the last calibration as
soon as the surface is recomputed. With the default `recompute=true` that happens
before the call returns and the surface version is incremented, so a consumer that
cached the surface must refetch it with `GetSurface`. The call is idempotent in
effect but not in status: deleting a plug that is already gone returns 404, which
makes a retry after a 5xx or a network timeout safe. The surface must not be locked
by a running calibration, otherwise the call is rejected with 409. Send
`recompute=false` on every delete of a batch except the last one, which keeps the
default, so the grid is recomputed once instead of once per plug.

## Parameters
| Name | In | Type | Required | Description |
|---|---|---|---|---|
| plugId | path | string | yes | Identifier of the plug to delete, e.g. `PLG-0004212`. |
| recompute | query | boolean | no | Recompute the surface points before returning. Default true. With false the plug is removed but no new version is produced: `GetSurface` keeps serving the current version, which still lists the plug and still carries its shift, until the next write recomputes the grid. |
| X-Correlation-Id | header | string | no | Client trace identifier echoed back in the response headers. |

## Response
A successful deletion returns 204 with an empty body; the JSON block below shows the
problem+json payload returned on 404.

| Status | Meaning |
|---|---|
| 204 | Plug deleted. The calibrated values are restored and the surface version incremented only when `recompute` is true; with false both happen at the next write. |
| 401 | Missing or expired access token. |
| 403 | Token lacks the volatility:write scope. |
| 404 | Plug not found, or already deleted. |
| 409 | Surface locked by a running calibration; retry once it completes. |
| 429 | Rate limit exceeded; see the Retry-After header, in seconds. |

```json
{
  "type": "https://api.example.internal/problems/plug-not-found",
  "title": "Plug not found",
  "status": 404,
  "detail": "Plug PLG-0004212 does not exist or has already been deleted.",
  "instance": "/volatility/plugs/PLG-0004212",
  "traceId": "6f1c2d84-9b3a-4a71-8f2e-0d5c7a91b402"
}
```

## Example C#
```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

var token = Environment.GetEnvironmentVariable("FEDERER_API_TOKEN")
            ?? throw new InvalidOperationException("FEDERER_API_TOKEN is not set.");

using var http = new HttpClient { BaseAddress = new Uri("https://api.example.internal") };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

const string plugId = "PLG-0004212";

using var request = new HttpRequestMessage(
    HttpMethod.Delete, $"/volatility/plugs/{plugId}?recompute=true");
request.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString());

using var response = await http.SendAsync(request);

if (response.StatusCode == HttpStatusCode.NoContent)
{
    Console.WriteLine($"Plug {plugId} deleted; surface points recomputed.");
}
else
{
    var problem = await response.Content.ReadFromJsonAsync<JsonNode>();
    Console.WriteLine($"status : {(int)response.StatusCode}");
    Console.WriteLine($"title  : {problem!["title"]}");
    Console.WriteLine($"detail : {problem["detail"]}");
}
```

## Example Python
```python
import os
import uuid

import requests

BASE_URL = "https://api.example.internal"
PLUG_ID = "PLG-0004212"
token = os.environ["FEDERER_API_TOKEN"]

headers = {
    "Authorization": f"Bearer {token}",
    "Accept": "application/json",
    "X-Correlation-Id": str(uuid.uuid4()),
}

response = requests.delete(
    f"{BASE_URL}/volatility/plugs/{PLUG_ID}",
    params={"recompute": "true"},
    headers=headers,
    timeout=30,
)

if response.status_code == 404:
    problem = response.json()
    print("status :", problem["status"])
    print("title  :", problem["title"])
    print("detail :", problem["detail"])
else:
    response.raise_for_status()
    print("status :", response.status_code)
    print("deleted:", PLUG_ID)
    print("body   :", response.text or "<empty>")
```

## Notes
Requires the volatility:write scope. Deletion is permanent, there is no restore
endpoint, but the audit trail keeps the record of the plug and of its removal with
the token subject that applied and deleted it. To list the plugIds active on a
surface, call `GetSurface` with includePlugs set to true: it answers for the last
recomputed version, so while a deferred delete is outstanding the removed plug is
still listed and its shift is still in the points a pricing job reads. The surface
version is the signal, because a deferred delete does not move it: after a batch,
confirm with `GetSurface` that the version has been incremented, and never leave a
published surface with a delete pending across a pricing run. Any later write on the
surface runs the pending recomputation, so an aborted batch is closed by the next
`Plug`, by another `DeletePlug` with recompute true, or by `Recalibrate`. A retried
last delete that returns 404 proves the plug is gone but not that the recomputation
ran; check the version before relying on the surface.
