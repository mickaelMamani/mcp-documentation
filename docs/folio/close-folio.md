---
formatVersion: "1.0"
domain: Folio
operationId: CloseFolio
method: POST
route: /folios/{folioId}/close
version: "2.3"
summary: Close a folio and freeze its positions.
tags: [folio, lifecycle]
keywords: >-
  close closes closing closed folio folios portfolio portfolios freeze freezes freezing frozen
  archive archives archiving terminate terminates terminating lifecycle status
  delete deletes deleting remove removes removing retire retires retiring
  positions readonly irreversible audit trail reason folioId closeDate closedAt closedBy
  idempotency key business date
  portefeuille clôturer clôture fermer fermeture archiver geler figer supprimer effacer
aliases: [archive, freeze, terminate]
related: [GetFolioPositions, GetFolioById, CreateFolio]
deprecated: false
---

## Description
Use this call at the end of a folio lifecycle, once every position has matured or has
been transferred to another folio; the platform refuses to close a folio that still holds
live positions. There is no delete endpoint: closing is how a folio is retired. Closing
freezes the folio: positions become read-only, no trade can be booked against it any more,
and exposures stop being revalued. A closed folio is still returned by `SearchFolio`,
which searches both statuses unless you pass `status`; downstream reports that filter on
`status: open` will drop it the day you close it, so check their criteria before closing.
The operation is irreversible through the API; reopening a folio is a support operation
handled outside the platform. Requires the `folio:write` scope. The call is idempotent
when you send an `Idempotency-Key`: a replay returns the original 200 response instead
of a 409.

## Parameters
| Name | In | Type | Required | Description |
|---|---|---|---|---|
| folioId | path | string | yes | Identifier of the folio to close, e.g. `FOL-000123`. |
| reason | body | string | yes | Business justification, 5 to 500 characters, kept in the audit trail. |
| closeDate | body | string | no | Business date of the closure (ISO date, e.g. `2026-08-28`). Defaults to today; cannot be in the future. |
| Idempotency-Key | header | string | no | Client-generated key that makes a retry of the same closure safe. |

## Response
A 200 returns the closure receipt shown below. Errors use the platform problem+json
document: the 422 carries `type` `https://api.example.internal/problems/folio-has-live-positions`
and a `detail` such as `Folio FOL-000123 still holds 42 live positions.`

| Status | Meaning |
|---|---|
| 200 | Folio closed; returns the closure receipt. |
| 400 | Malformed body, or `closeDate` in the future. |
| 401 | Missing or expired access token. |
| 403 | Token lacks the `folio:write` scope, or the folio is owned by another desk. |
| 404 | No folio exists with this identifier. |
| 409 | Folio is already closed. |
| 422 | Folio still holds live positions and cannot be closed. |
| 429 | Rate limit exceeded; retry after `Retry-After` seconds. |

```json
{
  "folioId": "FOL-000123",
  "status": "closed",
  "closedAt": "2026-08-28T10:00:00Z",
  "closedBy": "svc-folio-ops"
}
```

## Example C#
```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

var token = Environment.GetEnvironmentVariable("FEDERER_API_TOKEN")
            ?? throw new InvalidOperationException("FEDERER_API_TOKEN is not set.");

const string folioId = "FOL-000123";

using var http = new HttpClient { BaseAddress = new Uri("https://api.example.internal/") };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
http.DefaultRequestHeaders.Add("Idempotency-Key", $"close-{folioId}-2026-08-28");

var payload = new
{
    reason = "All positions matured, folio retired for 2026 reporting.",
    closeDate = "2026-08-28"
};

using var response = await http.PostAsJsonAsync($"folios/{folioId}/close", payload);
if (!response.IsSuccessStatusCode)
{
    var problem = await response.Content.ReadFromJsonAsync<JsonNode>();
    var detail = problem?["detail"]?.ToString() ?? "no detail";
    if ((int)response.StatusCode == 422)
    {
        throw new InvalidOperationException($"Folio still has live positions: {detail}");
    }
    throw new InvalidOperationException($"Close failed ({(int)response.StatusCode}): {detail}");
}

var receipt = await response.Content.ReadFromJsonAsync<JsonNode>()
              ?? throw new InvalidOperationException("Empty response body.");

Console.WriteLine($"Folio {receipt["folioId"]} is now {receipt["status"]}.");
Console.WriteLine($"Closed at {receipt["closedAt"]} by {receipt["closedBy"]}.");
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
    "Content-Type": "application/json",
    "Idempotency-Key": f"close-{FOLIO_ID}-2026-08-28",
}
payload = {
    "reason": "All positions matured, folio retired for 2026 reporting.",
    "closeDate": "2026-08-28",
}

response = requests.post(
    f"{BASE_URL}/folios/{FOLIO_ID}/close",
    json=payload,
    headers=headers,
    timeout=30,
)
if response.status_code == 422:
    problem = response.json()
    raise SystemExit(f"Folio still has live positions: {problem['detail']}")
response.raise_for_status()

receipt = response.json()
print(f"Folio {receipt['folioId']} is now {receipt['status']}.")
print(f"Closed at {receipt['closedAt']} by {receipt['closedBy']}.")
```

## Notes
Closing is irreversible through the API: plan the closure with the folio owner before
calling, because reopening requires a support ticket. Call `GetFolioPositions` first and
check that the returned page is empty, or that every position has matured, otherwise the
platform answers 422. A closed folio stays readable through `GetFolioById` and stays
visible to `SearchFolio`.
