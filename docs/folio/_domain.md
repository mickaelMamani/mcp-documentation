---
formatVersion: "1.0"
domain: Folio
summary: "Folios (portfolios of positions): search, retrieval, lifecycle."
keywords: >-
  folio folios portfolio portfolios position positions holdings
  search searching searches query querying find finding lookup
  get retrieve list create close delete lifecycle owner currency tags
  exposure exposures delta vega folioId positionId
  folio folios portefeuille portefeuilles requêter rechercher récupérer lister créer clôturer supprimer
endpoints: [SearchFolio, GetFolioById, GetFolioPositions, CreateFolio, CloseFolio, ListFolios]
---

## Overview

The Folio domain holds the portfolios of positions managed by the trading desks.
A folio is a header — folioId, name, owner, currency, status, tags, description, createdAt, updatedAt, closedAt — plus aggregated figures: positionCount, marketValue and an exposures block carrying delta, vega and notional.
A position is one line of a folio: positionId, instrumentId, instrumentType, underlying, quantity, notional, currency, tradeDate, maturityDate, marketValue, delta and vega; the folio itself is the path segment of the call, not a field of the line.
Invariants: a folio is owned by exactly one desk and only that desk may read or write it; the currency is chosen at creation and is immutable afterwards; a closed folio is read-only and its positions are frozen; positions are written by the booking system, never by this API, so no position write endpoint exists here.

## Use cases

| I want to… | Use | Notes |
|---|---|---|
| Find folios matching criteria such as name, owner, currency or date | `SearchFolio` | Paginated; max 500 per page. |
| Search folios by tag or by desk to build a shortlist | `SearchFolio` | Criteria are combined with AND. |
| Fetch one folio when I already know its id | `GetFolioById` | Cheaper than search; header and exposures only. |
| Look up the owner, currency and status of a folio | `GetFolioById` | Returns the header without the position lines. |
| Get the aggregated delta and vega exposure of a portfolio | `GetFolioById` | Read the exposures block. |
| Retrieve the position lines of a folio | `GetFolioPositions` | Paginated; filter by instrumentType. |
| Query the holdings of a folio as of a past business date | `GetFolioPositions` | Send the X-Business-Date header. |
| Create a folio for a desk and a currency | `CreateFolio` | Needs folio:write; send Idempotency-Key. |
| Close a folio at the end of its life | `CloseFolio` | Needs folio:write; fails while live positions remain. |
| Delete a folio | `CloseFolio` | There is no delete: closing freezes the folio and is irreversible through the API. |
| List every folio my client can see | `SearchFolio` | Send an empty body; `ListFolios` is the deprecated legacy route. |

## Typical workflow

1. `SearchFolio` to find the id → 2. `GetFolioById` for the header → 3. `GetFolioPositions` for the lines → 4. `CloseFolio` at end of life.

Crosscutting revaluation chain: `GetFolioPositions` with `instrumentType=option` to collect the option lines → group them by `underlying` and resolve one surface per group → `ComputeImpliedVol`, one batch per surface, mapping the `maturityDate` of each line to `points[].maturity`. A position line carries no strike, so either take the absolute strike from your own trade data and send `points[].strikeType` `absolute`, or send `points[].strikeType` `delta` with the line `delta` converted to a call delta (`1 + delta` on a put line, whose `delta` is negative) as `points[].strike`. That field is a number in decimal form: a 42-delta call line stays 0.42, a 25-delta put line (`delta` -0.25) is sent as 0.75. Use the per-line `delta` returned by `GetFolioPositions`, not the aggregated `delta` of the exposures block, and not a `25D` style grid label.

## Authentication & scopes

Client credentials on the `markets` realm; send `Authorization: Bearer <access_token>` on every call.

| Scope | Grants |
|---|---|
| folio:read | `SearchFolio`, `GetFolioById`, `GetFolioPositions`, `ListFolios` |
| folio:write | `CreateFolio`, `CloseFolio` |

Domain headers: `X-Business-Date` (optional, ISO date such as 2026-08-28) sets the as-of business date on `GetFolioById`, when `asOf` is omitted, and on `GetFolioPositions`, and defaults to today; `SearchFolio` and `ListFolios` always read the current date. `Idempotency-Key` (optional) is honoured on `CreateFolio` and `CloseFolio`, where a replay of the same key returns the first response instead of creating a duplicate or answering 409.

## Common errors

| Code | Cause | Fix |
|---|---|---|
| 403 | The folio is owned by another desk | Ask the owning desk, or call with a token whose client is entitled to that desk. |
| 404 | No folio exists with this id, or it is not visible for the requested business date | Resolve the id again with `SearchFolio` before retrying. |
| 409 | A folio with the same name already exists for that owner | Pick another name, or reuse the existing folio returned in the problem detail. |
| 409 | The folio is already closed | Nothing to do; a closed folio stays read-only. |
| 422 | Live positions remain in the folio | Wait for the booking system to flatten or transfer the lines, then close again. |
| 429 | More than 600 requests per minute for this client | Back off for the number of seconds given in Retry-After. |
