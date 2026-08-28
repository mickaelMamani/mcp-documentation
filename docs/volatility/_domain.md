---
formatVersion: "1.0"
domain: Volatility
summary: "Volatility surfaces: retrieval, plugging, calibration."
keywords: >-
  volatility vol surface surfaces smile skew tenor tenors strike strikes
  plug plugs plugging bump bumps shift shifts calibrate calibrates calibration
  recalibrate recalibrates implied interpolate interpolation grid grids
  underlying underlyings quote quotes moneyness delta atm published locked
  surfaceId plugId businessDate assetClass model
  list lists listing search searches searching find finds finding lookup
  get gets retrieve retrieves query queries compute computes apply applies
  delete deletes remove removes
  volatilité nappe nappes lister rechercher récupérer calculer décaler calibrer
  interpoler
endpoints: [ListSurfaces, GetSurface, Plug, DeletePlug, Recalibrate, ComputeImpliedVol]
---

## Overview

This domain owns the implied volatility surfaces used for pricing and risk across FX,
equity and rates. Three entities: a `Surface` holds a tenor and strike grid of vol
points plus its calibration metadata (model `SVI` or `SABR`, `calibrationRmse`,
`status`), a plug is a manual shift in vol points applied on one grid point, and a
`Quote` is the raw market observation (straddle, risk reversal, butterfly) feeding
calibration. Invariants: a surface is identified by `underlying` + `businessDate` +
`version`; plugs are versioned overrides that never rewrite calibrated values, so
removing a plug restores them; a surface in status `locked` rejects every write.

## Use cases

| I want to… | Use | Notes |
|---|---|---|
| Find which volatility surfaces exist for EURUSD today | `ListSurfaces` | `underlying` is required; paginated; narrow further with `assetClass`. |
| Search the surfaces published on a past business date for an underlying | `ListSurfaces` | `underlying` is required; add `businessDate` and `status`; the calibration model is returned, not filterable. |
| Retrieve the full tenor and strike grid of one surface | `GetSurface` | Cheapest read once the surfaceId is known; the grid can be large. |
| Get the list of plugs currently applied to a surface | `GetSurface` | Plugs are returned inline; no extra call needed. |
| Look up the vol of a 3M 25-delta point | `ComputeImpliedVol` | Read only; interpolates on the published grid. |
| Compute an implied vol for a strike that falls between two grid points | `ComputeImpliedVol` | Returns 422 outside the grid unless `allowExtrapolation` is true. |
| Bump the 3M ATM point by 0.5 vol | `Plug` | Needs `volatility:write`; creates a new surface version. |
| Apply a trader override before pricing a deal | `Plug` | Always fill the reason; plugs are audited and may expire. |
| Remove an override and restore the calibrated values | `DeletePlug` | Needs the plugId; rejected on a locked surface. |
| Refit a surface from the latest market quotes | `Recalibrate` | Expensive; drops every plug unless `keepPlugs` is true. |

## Typical workflow

Pricing chain: 1. `ListSurfaces` → 2. `GetSurface` → 3. `Plug` → 4. `ComputeImpliedVol`.
Resolve the surfaceId, inspect the grid and its active plugs, bump the point you need,
then read the plugged value back.

Calibration chain: 1. `GetSurface` (list the active plugs) → 2. `Recalibrate` with
`keepPlugs` true → 3. `DeletePlug` for the overrides the refit made obsolete →
4. `ComputeImpliedVol`. Refit once the quote snapshot has landed, check the new
`calibrationRmse` and version, then validate a reference point. Called without
`keepPlugs`, `Recalibrate` drops every plug, so there is nothing left to delete.

## Authentication & scopes

Client credentials on the `markets` realm; send `Authorization: Bearer <access_token>`.

| Operation | Scope |
|---|---|
| `ListSurfaces` | `volatility:read` |
| `GetSurface` | `volatility:read` |
| `ComputeImpliedVol` | `volatility:read` |
| `Plug` | `volatility:write` |
| `DeletePlug` | `volatility:write` |
| `Recalibrate` | `volatility:write` |

Domain specific header: `X-Business-Date` (optional, ISO date such as 2026-08-28).
It selects the as-of business date for reads and defaults to today. Writes always
target the surface named in the payload or the path, so the header is ignored there.

## Common errors

| Code | Cause | Fix |
|---|---|---|
| 400 | `underlying` missing on `ListSurfaces` | The filter is mandatory; there is no wildcard and no unfiltered catalogue scan. |
| 404 | Unknown surfaceId, or no surface for that underlying and business date | Resolve the id with `ListSurfaces` before reading or writing. |
| 404 | Unknown plugId on `DeletePlug` | Read the active plugs from `GetSurface`; an expired plug is already gone. |
| 409 | Surface status is locked, or a calibration is running | Wait for the calibration to finish, then retry the write. |
| 422 | Requested tenor or strike lies outside the calibrated grid | Clamp to the published tenors and strikes, or set `allowExtrapolation` to true on `ComputeImpliedVol`. |
| 422 | Not enough quotes to fit the model | Wait for a fuller quote snapshot, or recalibrate on a shorter tenor set. |
| 403 | Token lacks `volatility:write` | Request the write scope for the client; read scopes cannot plug or recalibrate. |
| 429 | Above 600 requests per minute for the client | Honour the `Retry-After` header in seconds and batch grid reads. |
