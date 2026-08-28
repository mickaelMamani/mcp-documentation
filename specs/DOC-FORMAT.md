# DOC-FORMAT — Structure de la documentation API

> Contrat entre le **générateur de documentation** et le **serveur MCP**.
> Toute modification de ce format est une modification d'interface : elle doit être versionnée (`formatVersion`) et rétrocompatible ou accompagnée d'une migration de l'ingestion.

**Version du format :** 1.0
**Langue de la documentation :** anglais (les questions utilisateur peuvent être en français — voir §6)

---

## 1. Objectifs du format

| Objectif | Conséquence sur le format |
|---|---|
| Le serveur MCP ne doit rien deviner | Toutes les métadonnées sont explicites (front matter YAML + manifest) |
| Le retrieval doit être précis | Une unité d'indexation = un endpoint ; sections à titres fixes ; mots-clés générés |
| Le LLM doit recevoir le minimum de tokens utiles | Sections courtes, tableaux plutôt que prose, un exemple par langage isolable |
| Les réindexations doivent être incrémentales | Un fichier par endpoint, hash de contenu par fichier |
| La doc reste lisible par un humain | Markdown standard, rendu correct dans Bitbucket / VS Code |

---

## 2. Arborescence

```
docs/
├── manifest.json                 # Catalogue machine : domaines, endpoints, version
├── _platform.md                  # Vue d'ensemble plateforme : auth, conventions, erreurs globales
├── volatility/
│   ├── _domain.md                # Vue d'ensemble du domaine + cas d'usage → endpoints
│   ├── plug.md                   # Un fichier par endpoint
│   ├── get-surface.md
│   └── ...
├── folio/
│   ├── _domain.md
│   ├── search-folio.md
│   ├── get-folio-by-id.md
│   └── ...
└── ...
```

Règles :

- **Un dossier par domaine.** Nom en `kebab-case`, identique au champ `domain` (normalisé) du manifest.
- **Un fichier par endpoint.** Nom = `operationId` en `kebab-case` (`GetFolioById` → `get-folio-by-id.md`). Le nom de fichier est dérivé, jamais source de vérité : l'`operationId` du front matter fait foi.
- **Fichiers préfixés `_`** = fichiers de contexte (jamais des endpoints). Le serveur les traite différemment (§4.3).
- Pas de sous-dossiers dans un domaine. Si un domaine dépasse ~150 endpoints, le découper en plusieurs domaines plutôt qu'en sous-dossiers.
- Encodage UTF-8 sans BOM, fins de ligne LF.

---

## 3. `manifest.json`

Généré en même temps que les fichiers. C'est la source du **catalogue d'endpoints** (tool `list_domains`, résolution `search_endpoints`). Les front matters des fichiers doivent être cohérents avec lui ; l'ingestion échoue si un `operationId` est présent d'un côté et pas de l'autre.

```json
{
  "formatVersion": "1.0",
  "platform": "Federer API",
  "platformVersion": "2.3.0",
  "generatedAt": "2026-08-28T10:00:00Z",
  "language": "en",
  "domains": [
    {
      "id": "volatility",
      "name": "Volatility",
      "summary": "Volatility surfaces: retrieval, plugging, calibration.",
      "file": "volatility/_domain.md",
      "endpoints": [
        {
          "operationId": "Plug",
          "method": "POST",
          "route": "/volatility/plug",
          "summary": "Apply a plug (manual bump) to an existing volatility surface.",
          "file": "volatility/plug.md",
          "tags": ["surface", "calibration"],
          "deprecated": false
        }
      ]
    }
  ]
}
```

Champs obligatoires : `formatVersion`, `platform`, `platformVersion`, `language`, `domains[].id`, `domains[].name`, `domains[].summary`, `domains[].file`, `endpoints[].operationId`, `endpoints[].method`, `endpoints[].route`, `endpoints[].summary`, `endpoints[].file`.

---

## 4. Contenu des fichiers

### 4.1 Fichier endpoint (`<domain>/<operation-id>.md`)

#### Front matter (obligatoire)

```yaml
---
formatVersion: "1.0"
domain: Volatility
operationId: Plug
method: POST
route: /volatility/plug
version: "2.3"                  # version de l'API où l'endpoint est disponible
summary: Apply a plug (manual bump) to an existing volatility surface.
tags: [surface, calibration]
keywords: >-                     # voir §6 — généré, bilingue, une ligne
  plug plugs bump shift surface volatility vol calibrate calibration
  surfaceId tenor strike
  plug bumper décaler surface volatilité calibrer nappe
aliases: [bump, shift]           # synonymes métier de l'action, EN uniquement
related: [GetSurface, Recalibrate]   # endpoints liés (workflow), operationId
deprecated: false
---
```

Contraintes :
- `summary` : **une phrase, ≤ 160 caractères**, en langage d'usage ("Apply a plug to a surface"), pas en jargon interne. C'est ce qui est renvoyé par `search_endpoints` et ce qui est le plus lourdement indexé.
- `keywords` : liste de mots séparés par des espaces, sans ponctuation. Voir §6 pour la règle de génération.
- `related` : sert au tool `get_endpoint` pour proposer les endpoints du même workflow sans appel supplémentaire.

#### Corps — sections fixes, dans cet ordre

Les titres de niveau 2 sont **exactement** ceux-ci (le chunker découpe dessus et type chaque chunk par son titre). Une section absente est autorisée ; une section renommée est une erreur d'ingestion.

```markdown
## Description
Quand l'utiliser, ce qu'il fait, prérequis, effets de bord, pièges. 3 à 10 lignes.
Ne pas répéter le summary.

## Parameters
| Name | In | Type | Required | Description |
|---|---|---|---|---|
| surfaceId | body | string | yes | Identifier of the target surface. |
| tenor | body | string | yes | Tenor in ISO 8601 duration (e.g. `P3M`). |
| shift | body | number | yes | Absolute vol shift, in points. |

## Response
Status codes, schéma de réponse (tableau ou bloc JSON court), erreurs métier.

| Status | Meaning |
|---|---|
| 200 | Surface updated; returns the new surface id. |
| 404 | Surface not found. |
| 409 | Surface locked by another calibration. |

```json
{ "surfaceId": "VOL-EURUSD-2026-08-28", "version": 12 }
```

## Example C#
```csharp
// Exemple complet et exécutable : client, appel, lecture du résultat.
```

## Example Python
```python
# Idem, Python.
```

## Notes
Optionnel. Remarques de sécurité, limites, comportement par environnement.
```

Contraintes :
- **Pas de titre `#` (H1)** dans le corps : le titre est `operationId` (front matter). Évite un chunk redondant.
- `## Parameters` en **tableau** obligatoirement. Colonne `In` ∈ `path | query | header | body`.
- **Un seul bloc de code par section `Example *`**, avec le langage déclaré (` ```csharp `, ` ```python `). Le chunker isole chaque exemple comme un chunk `example` tagué `language`. L'exemple doit être autonome (imports, instanciation du client, appel, gestion du résultat) — le LLM le renverra tel quel.
- Longueur cible par section : Description ≤ 250 tokens, Parameters ≤ 400, Response ≤ 300, chaque Example ≤ 400. Au-delà, le générateur doit synthétiser. Un chunk trop long est tronqué par le serveur au budget, ce qui dégrade la réponse.
- Pas de liens relatifs vers d'autres fichiers ; référencer les endpoints par `operationId` en code inline (`` `GetSurface` ``). Le serveur les résout en `endpoint://` (voir ARCHITECTURE §5.3).

### 4.2 Fichier domaine (`<domain>/_domain.md`)

C'est le chunk qui répond aux questions **d'intention** ("comment requêter les folios ?") quand l'utilisateur ne connaît pas encore le nom de l'endpoint. Le générateur doit y investir.

```yaml
---
formatVersion: "1.0"
domain: Folio
summary: Folios (portfolios of positions): search, retrieval, lifecycle.
keywords: >-
  folio folios portfolio portfolios search query find get retrieve by id list
  folio folios portefeuille requêter rechercher récupérer lister
endpoints: [SearchFolio, GetFolioById, CreateFolio, CloseFolio]
---

## Overview
Rôle du domaine, modèle de données en 5 lignes, invariants.

## Use cases
| I want to… | Use | Notes |
|---|---|---|
| Find folios matching criteria (name, owner, date) | `SearchFolio` | Paginated; max 500 per page. |
| Fetch one folio when I already know its id | `GetFolioById` | Cheaper than search; includes positions. |
| Create a folio | `CreateFolio` | Requires `folio:write` scope. |

## Typical workflow
1. `SearchFolio` to find the id → 2. `GetFolioById` for details → 3. `CloseFolio`.

## Authentication & scopes
Scopes requis, en-têtes spécifiques au domaine.

## Common errors
| Code | Cause | Fix |
|---|---|---|
```

Le tableau **Use cases** est la section la plus importante de tout le dépôt pour la qualité du retrieval : la colonne "I want to…" doit être rédigée en langage utilisateur, avec des verbes variés (find / search / query / look up).

### 4.3 Fichier plateforme (`_platform.md`)

Même structure que `_domain.md` sans `endpoints`. Contenu : URL de base par environnement, authentification (Keycloak, obtention du token), en-têtes communs, pagination, format des erreurs, versioning, limites de débit, conventions de dates/nombres. Chunké par `##` comme les autres.

---

## 5. Règles de génération pour le retrieval

1. **Summary en langage d'usage.** Test : un développeur qui ne connaît pas l'API doit comprendre à quoi sert l'endpoint en lisant uniquement le summary.
2. **Noms de paramètres dans `keywords`.** Les utilisateurs cherchent souvent par nom de paramètre (`surfaceId`, `tenor`).
3. **Formes fléchies dans `keywords`.** Singulier et pluriel, verbe et nom (`plug plugs plugging`, `search searching`). Pas de stemming côté serveur sur ce champ.
4. **Verbes d'action standardisés** dans les summaries : `Get`, `List`, `Search`, `Create`, `Update`, `Delete`, `Apply`, `Compute`. Ça aligne le vocabulaire des summaries sur celui des `operationId` et des requêtes LLM.
5. **Un exemple par langage, complet.** Pas de `// ...`. Le LLM copie l'exemple ; un exemple partiel produit du code faux.
6. **`related` renseigné** dès qu'un endpoint s'inscrit dans une séquence.

---

## 6. Bilinguisme (doc EN, questions FR)

Le serveur ne traduit pas. La stratégie est :

1. Le LLM client reformule la question en anglais (instruction dans les descriptions de tools — voir ARCHITECTURE §5).
2. **Filet de sécurité lexical** : le champ `keywords` contient, après les termes anglais, **5 à 10 termes français** par endpoint et par domaine : l'entité, l'action, et leurs formes courantes (`folio folios requêter rechercher récupérer`). Généré par le générateur (prompt LLM ou dictionnaire métier).
3. Les corps de sections restent **exclusivement en anglais** ; ne pas mélanger les langues dans la prose (ça dégrade le stemming anglais).

---

## 7. Validation (exécutée par l'ingestion, échec = fichier rejeté)

- Front matter présent, `formatVersion` supporté, champs obligatoires présents.
- `operationId` unique dans tout le dépôt ; présent dans `manifest.json` avec `method`/`route` identiques.
- Titres `##` ∈ {Description, Parameters, Response, Example C#, Example Python, Notes} pour un endpoint ; ∈ {Overview, Use cases, Typical workflow, Authentication & scopes, Common errors} pour un domaine.
- Chaque section `Example *` contient exactement un bloc de code avec langage déclaré.
- `## Parameters` est un tableau avec les 5 colonnes attendues.
- `summary` ≤ 160 caractères ; `keywords` sans ponctuation.
- `related[]` référence des `operationId` existants.

Le rapport de validation (fichier, règle, ligne) est journalisé et exposé par le tool d'administration `reindex` pour que le générateur puisse être corrigé.

---

## 8. Évolutions prévues

- `formatVersion 1.1` : section `## Request example` (payload JSON brut) séparée des exemples clients ; champ `since`/`until` pour les versions d'API.
- Support d'un fichier `_glossary.md` (termes métier → définitions), chunké par entrée, pour les questions "qu'est-ce qu'un plug ?".
