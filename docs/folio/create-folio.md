---
formatVersion: "1.0"
domain: Folio
operationId: CreateFolio
method: POST
route: /folios
version: "2.3"
summary: Create a folio for a given owner and currency.
tags: [folio, lifecycle]
keywords: >-
  create creates creating creation new open opens opening register registers registering
  add adds adding declare declares declaring
  folio folios portfolio portfolios book books empty
  name owner currency tags description idempotency key scope
  folioId status unique uniqueness duplicate conflict immutable
  créer création nouveau ouvrir enregistrer portefeuille devise propriétaire
aliases: [new, open, register]
related: [SearchFolio, CloseFolio]
deprecated: false
---

## Description
Use this to declare a new container before any position exists: the folio is created empty and
with status `open`. Positions are never attached through this API; the booking system pushes them
asynchronously, so `positionCount` stays at 0 right after creation. The caller must hold the
`folio:write` scope, and `currency` is immutable once the folio exists, so a wrong currency means
creating another folio rather than patching this one. The name must be unique for the owner, which
is the most frequent cause of a 409. Send an `Idempotency-Key` when retrying over a flaky network:
a replay returns the original 201 instead of creating a duplicate. Derive that key from the
creation itself, owner plus name, and never reuse a constant, or the second folio you create
silently returns the first. Use `SearchFolio` first if you are not sure whether the desk already
owns a folio with that name.

## Parameters
| Name | In | Type | Required | Description |
|---|---|---|---|---|
| name | body | string | yes | Display name, 3 to 80 characters. Must be unique for the owner. |
| owner | body | string | no | Owning desk, exact identifier such as `desk-fx-options`. Defaults to the desk of the access token subject. |
| currency | body | string | yes | Reporting currency, ISO 4217 code such as `EUR`. Immutable after creation. |
| tags | body | array of string | no | Free classification labels, at most 10 entries. |
| description | body | string | no | Free text, at most 500 characters. |
| Idempotency-Key | header | string | no | Client-generated key derived from the creation, such as `create-<owner>-<name>`; a replay of the same key returns the original 201. |

## Response
| Status | Meaning |
|---|---|
| 201 | Folio created; the body is the new folio and `Location` points to its resource. |
| 400 | Malformed body or unknown field. |
| 401 | Missing or expired access token. |
| 403 | The `folio:write` scope is missing. |
| 409 | A folio with this name already exists for this owner. |
| 422 | Body well formed but invalid: unknown currency, name too short, more than 10 tags. |
| 429 | Rate limit of 600 requests per minute exceeded; retry after `Retry-After` seconds. |

```json
{
  "folioId": "FOL-000456",
  "name": "EURUSD Vol Book",
  "owner": "desk-fx-options",
  "currency": "EUR",
  "status": "open",
  "tags": ["fx", "options"],
  "description": "Market making book for EURUSD vanilla options.",
  "createdAt": "2026-08-28T10:00:00Z",
  "updatedAt": "2026-08-28T10:00:00Z",
  "closedAt": null,
  "positionCount": 0,
  "marketValue": 0,
  "exposures": { "delta": 0, "vega": 0, "notional": 0 }
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

var body = new
{
    name = "EURUSD Vol Book",
    owner = "desk-fx-options",
    currency = "EUR",
    tags = new[] { "fx", "options" },
    description = "Market making book for EURUSD vanilla options."
};

using var request = new HttpRequestMessage(HttpMethod.Post, "/folios")
{
    Content = JsonContent.Create(body)
};
request.Headers.Add("Idempotency-Key", $"create-{body.owner}-{body.name}");

using var response = await http.SendAsync(request);
response.EnsureSuccessStatusCode();

var folio = await response.Content.ReadFromJsonAsync<Folio>()
            ?? throw new InvalidOperationException("Empty response body.");

Console.WriteLine($"Location: {response.Headers.Location}");
Console.WriteLine($"Created {folio.FolioId} {folio.Currency} status={folio.Status}");

record Folio(string FolioId, string Name, string Owner, string Currency, string Status,
             int PositionCount, DateTimeOffset CreatedAt);
```

## Example Python
```python
import os

import requests

BASE_URL = "https://api.example.internal"

token = os.environ["FEDERER_API_TOKEN"]
body = {
    "name": "EURUSD Vol Book",
    "owner": "desk-fx-options",
    "currency": "EUR",
    "tags": ["fx", "options"],
    "description": "Market making book for EURUSD vanilla options.",
}
headers = {
    "Authorization": f"Bearer {token}",
    "Accept": "application/json",
    "Content-Type": "application/json",
    "Idempotency-Key": f"create-{body['owner']}-{body['name']}",
}

response = requests.post(f"{BASE_URL}/folios", headers=headers, json=body, timeout=30)
response.raise_for_status()

folio = response.json()
print("Location:", response.headers["Location"])
print(folio["folioId"], folio["currency"], folio["status"], folio["positionCount"])
```

## Notes
Name uniqueness is enforced per owner, not globally: two desks can both own a folio called
`Overnight Hedge`. There is no delete operation; a folio that must leave the active scope is
closed with `CloseFolio`, which freezes its positions and keeps it readable.
