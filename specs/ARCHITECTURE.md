# ARCHITECTURE — Serveur MCP de documentation API

> Design doc. Aucun code d'implémentation ici : les contrats, les décisions et leurs justifications.
> Format de la documentation source : voir `DOC-FORMAT.md`.

**Cible :** .NET 8 (LTS) · in-process · stdio d'abord, Streamable HTTP ensuite
**Statut :** proposé — v1 = lexical seul ; v2 = + vecteurs + reranker

---

## 1. Contexte et objectifs

Un agent de code (Claude Code, Gemini CLI, Cursor…) doit pouvoir répondre de façon fiable à des questions sur les endpoints d'une plateforme API interne : quel endpoint, quels paramètres, quel retour, un exemple d'appel en C# ou Python.

Contraintes :
- Aucune dépendance à un moteur externe (pas de Meilisearch, Elasticsearch, base vectorielle). Tout tourne dans le process du serveur.
- Documentation générée, en anglais, structurée selon `DOC-FORMAT.md`. Questions utilisateur en français ou anglais.
- Pas de modèle d'embedding disponible en v1.
- Priorité : **qualité des résultats renvoyés au LLM**, puis simplicité.

Objectifs mesurables (voir §9) :
- Le bon `operationId` dans les 3 premiers résultats de `search_endpoints` sur ≥ 90 % du jeu de questions.
- ≤ 2 appels de tools par question en moyenne.
- ≤ 3 000 tokens de documentation renvoyés par question en moyenne.
- Démarrage (index construit) < 2 s pour 500 fichiers.

Inspiration : Context7 (Upstash). Leçon principale retenue : **le filtrage et le classement se font côté serveur**, le LLM reçoit peu et bien ; les tools sont peu nombreux, très décrits, et le `query` est propagé partout.

---

## 2. Vue d'ensemble

```
┌──────────────────────────── Client MCP (Claude Code, Gemini CLI, …) ───────────────┐
│  LLM  ── lit tools/list + ServerInstructions ── reformule en anglais ── appelle    │
└───────────────┬──────────────────────────────────────────────────────▲──────────────┘
                │ MCP (stdio | Streamable HTTP)                        │ texte Markdown compact
┌───────────────▼──────────────────────────────────────────────────────┴──────────────┐
│  Presentation      tools MCP (list_domains, search_endpoints, get_endpoint,        │
│                    search_docs) · resources (endpoint://, doc://) · prompts         │
├────────────────────────────────────────────────────────────────────────────────────┤
│  Application       use cases : SearchEndpoints · GetEndpoint · SearchDocs ·        │
│                    ListDomains · RebuildIndex                                       │
│                    ports : IDocumentSource · IChunker · ILexicalIndex ·            │
│                            IVectorIndex (v2) · IReranker (v2) · IIndexSnapshot     │
├────────────────────────────────────────────────────────────────────────────────────┤
│  Domain            DocumentChunk · EndpointDescriptor · DomainDescriptor ·         │
│                    ChunkId · OperationId · SectionKind · TokenBudget · Breadcrumb   │
├────────────────────────────────────────────────────────────────────────────────────┤
│  Infrastructure    MarkdownRepositorySource (dossier/git) · MarkdigChunker ·       │
│                    LuceneLexicalIndex · InMemoryVectorIndex (v2) · OnnxReranker    │
│                    (v2) · IndexSnapshotProvider (swap atomique) · FileWatcher       │
└────────────────────────────────────────────────────────────────────────────────────┘
```

Tout l'index vit dans un **snapshot immuable** en mémoire, construit par l'ingestion et publié atomiquement. Les requêtes lisent le snapshot courant sans verrou.

---

## 3. Choix technologiques

| Besoin | Choix | Alternatives écartées | Justification |
|---|---|---|---|
| Serveur MCP | `ModelContextProtocol` (SDK C# officiel) + `ModelContextProtocol.AspNetCore` pour HTTP | Implémentation JSON-RPC maison | Tools par attributs, transports stdio/HTTP fournis, `ServerInstructions`, resources et prompts supportés |
| Parsing Markdown | `Markdig` (+ extension YAML front matter) | Regex | Arbre de syntaxe fiable pour titres, tableaux, blocs de code |
| Front matter / manifest | `YamlDotNet`, `System.Text.Json` | — | Standard |
| Recherche lexicale | **`Lucene.NET` 4.8** | SQLite FTS5 | Voir §4.2. Analyzer par champ, synonymes, requêtes par objets, boosts : leviers nécessaires à l'itération qualité. **Point de vigilance :** package longtemps publié en `beta` sur NuGet — à faire valider par la gestion des dépendances ; repli documenté = SQLite FTS5 (§4.3) |
| Comptage de tokens | `Microsoft.ML.Tokenizers` (tiktoken `cl100k_base`) | Heuristique chars/4 | Budget de sortie fiable |
| Embeddings (v2) | `Microsoft.ML.OnnxRuntime` + modèle multilingue (`bge-m3` ou `multilingual-e5-small`) via `Microsoft.Extensions.AI.IEmbeddingGenerator` | Service distant | In-process, multilingue FR/EN |
| Reranker (v2) | `Microsoft.ML.OnnxRuntime` + cross-encoder (`bge-reranker-v2-m3`) | — | Plus grand gain qualité/coût ; indépendant du moteur lexical |
| Logs | `Microsoft.Extensions.Logging` + `LoggerMessage` source generator (`Log.` prefix), Serilog sink | — | Convention interne. **En stdio, les logs vont sur stderr, jamais stdout** (stdout = canal JSON-RPC) |
| Tests | xUnit, `Testcontainers` inutile (in-process) ; jeu de questions en YAML | — | §9 |

Conventions de code : `internal sealed` par défaut, constructeurs primaires, NRT activés, pas de `ConfigureAwait(false)` dans le hôte ASP.NET Core (oui dans une éventuelle lib partagée), aucune exception avalée.

---

## 4. Retrieval

### 4.1 Modèle de données indexé

**`EndpointDescriptor`** (catalogue, depuis `manifest.json` + front matter) : `OperationId`, `Domain`, `Method`, `Route`, `Version`, `Summary`, `Tags`, `Aliases`, `Keywords`, `Related`, `Deprecated`, `FilePath`, `ContentHash`.

**`DocumentChunk`** (une section `##`) :

| Champ | Type | Indexé | Rôle |
|---|---|---|---|
| `ChunkId` | hash(FilePath + Breadcrumb) | stocké | Identifiant stable entre réindexations |
| `Kind` | `Description` · `Parameters` · `Response` · `Example` · `Notes` · `DomainOverview` · `DomainUseCases` · `DomainWorkflow` · `DomainAuth` · `DomainErrors` · `Platform` | stocké | Filtrage et pondération |
| `OperationId` | string? | stocké | Lien vers l'endpoint (null pour domaine/plateforme) |
| `Domain` | string | stocké | Filtre |
| `Language` | `csharp` · `python` · null | stocké | Sélection d'exemple |
| `Title` | string | oui (boost ×4) | `operationId` ou titre de section |
| `Breadcrumb` | `Domain › OperationId › Section` | oui (boost ×2) | Contexte pour le LLM et le scoring |
| `Summary` | string | oui (boost ×3) | Copie du summary de l'endpoint/domaine sur chaque chunk |
| `Keywords` | string | oui (boost ×3, analyzer neutre) | Pont FR/EN, noms de paramètres |
| `Body` | string | oui (×1, analyzer anglais) | Contenu de la section |
| `TokenCount` | int | stocké | Budget |

Le `Summary` et les `Keywords` sont **dupliqués sur chaque chunk** de l'endpoint : un chunk `Example Python` est ainsi retrouvable par une requête "plug surface python" même si le code ne contient pas ces mots.

### 4.2 Chaîne d'analyse Lucene

| Champ | Analyzer | Détail |
|---|---|---|
| `Title`, `Breadcrumb` | Custom | `WhitespaceTokenizer` → `WordDelimiterFilter` (camelCase, `/`, `_`, `-`, `{}`) → `LowerCaseFilter` → `ASCIIFoldingFilter` → `EnglishMinimalStemFilter`. Conserve aussi le token original (`Plug` et `plug`). Le dernier filtre ne retire que la marque du pluriel : sans lui, le token de requête `folios` ne peut atteindre aucun `Title` (ADR #20) |
| `Summary`, `Body` | `EnglishAnalyzer` + `SynonymFilter` | Stemming anglais ; synonymes métier chargés depuis un fichier `synonyms.txt` versionné (`search, query, find, lookup` / `plug, bump, shift` / `folio, portfolio`) |
| `Keywords` | Custom neutre | `WhitespaceTokenizer` → `LowerCaseFilter` → `ASCIIFoldingFilter` → `EnglishMinimalStemFilter`. **Pas de stemming**, hormis la marque du pluriel, commune à l'anglais et au français, donc sans risque pour un champ bilingue (ADR #20) |

Construction de la requête (`ILexicalIndex.Search(query, filters, k)`) :

1. Tokeniser la requête avec l'analyzer neutre (lowercase + folding).
2. `BooleanQuery` :
   - pour chaque token : `Should` sur chaque champ avec son boost ; `PrefixQuery` ajoutée si token ≥ 4 caractères ; `FuzzyQuery(maxEdits=1)` sur `Title`/`Keywords` si token ≥ 6 caractères ;
   - si un token correspond exactement à un `operationId` connu : `TermQuery` boost ×10 (l'utilisateur ou le LLM a nommé l'endpoint) ;
   - `MinimumNumberShouldMatch` = 1 (tolérant ; c'est BM25 qui classe).
3. Filtres (`FilterClause` / `TermQuery` en `Must`) : `Domain`, `Kind`, `Language`, `Deprecated=false` par défaut.
4. Boost par document : `Kind ∈ {DomainUseCases, Description}` ×1.5 ; `Example` ×0.8 pour `search_docs` (les exemples remontent via `get_endpoint`, pas par la recherche libre).
5. **Re-pondération post-BM25** (`search_endpoints` uniquement, ADR #20). BM25 note chaque terme indépendamment et ignore de quel domaine parle la requête ; deux facteurs multiplicatifs rétablissent ce qu'il ne sait pas exprimer :
   - **couverture** ×(1 + 2 × part des tokens de la requête que l'endpoint prend en charge), calculée sur `operationId + summary + keywords` ;
   - **affinité de domaine** ×(1 + 2 × recouvrement de la requête avec `id + name + keywords` du domaine).

   Sans elles, un verbe générique rare porté par un `Title` (×4, IDF élevé) l'emporte sur l'endpoint qui répond réellement à la requête : `list folios` classait `ListSurfaces` premier. Les deux poids sont mesurés, pas choisis (§9).
6. `k` = 30 candidats en v1 (top-N direct), 50 en v2 (avant reranking).

Pourquoi ces choix : les termes rares et exacts (`operationId`, noms de paramètres) sont ce qui distingue les endpoints entre eux — d'où les boosts élevés sur `Title`/`Keywords` ; le corps sert à départager, pas à trouver.

### 4.3 Repli : SQLite FTS5

Si Lucene.NET est refusé : table FTS5 `chunks(title, breadcrumb, summary, keywords, body, …UNINDEXED)`, tokenizer `unicode61 remove_diacritics 2`, `ORDER BY bm25(chunks, 4, 2, 3, 3, 1)`, requête reconstruite en `OR` de tokens avec préfixe `*`. Le découpage camelCase et les synonymes sont alors **entièrement portés par le champ `keywords` généré** (`DOC-FORMAT §5-6`). Même port `ILexicalIndex`, même snapshot. Perte : fuzzy, synonymes côté index, analyzer par champ.

### 4.4 v2 — hybride et reranking

- **Vecteurs** : un `float[]` contigu par chunk (dim 384–1024), stocké dans le snapshot ; recherche brute-force cosinus (≤ 10 ms pour 10 000 chunks) via `TensorPrimitives.CosineSimilarity`. Texte embarqué = `Breadcrumb + Summary + Body` (tronqué au max du modèle). Embeddings calculés à l'ingestion en batch, mis en cache sur disque par `ContentHash`.
- **Fusion** : Reciprocal Rank Fusion des listes lexicale et vectorielle (`k=60`), 50 candidats.
- **Reranking** : cross-encoder ONNX sur (query, chunk) pour les 50 candidats ; garder le top 5–10. Latence attendue ~100–300 ms CPU.
- Le multilinguisme est la raison principale de la v2 : un modèle multilingue rapproche « comment requêter les folios » de « Search folios by criteria » sans traduction.

Déclencheur de passage en v2 : le benchmark (§9) montre un rang moyen > 3 sur les questions d'intention en français.

### 4.5 Budget de sortie

Chaque tool a un budget de tokens (`TokenBudget`). L'assemblage :
1. Trie les chunks par score.
2. Ajoute des chunks entiers tant que le budget le permet — **jamais de coupe à l'intérieur d'un chunk** (un bloc de code tronqué est pire qu'absent).
3. Ajoute un pied : `N more relevant sections not included: <breadcrumbs>. Refine `query` or call get_endpoint with one of: <operationIds>.`

Budgets par défaut : `search_endpoints` 400 · `get_endpoint` 2 500 · `search_docs` 1 500 · `list_domains` 600.

---

## 5. Surface MCP

### 5.1 `ServerInstructions` (envoyées à l'initialisation)

```
This server exposes the internal documentation of the <Platform> API (version {platformVersion}).
Documentation is in English. Users may ask in French: translate their intent into English API
vocabulary before calling any tool (entity, action, parameter names).
Workflow: (1) list_domains if you don't know which domain applies; (2) search_endpoints to find
candidate operationIds — several may match one intent, present them or ask; (3) get_endpoint with
the exact operationId to get parameters, response and a code example.
Always cite the operationId, HTTP method and route in your answer. Never invent endpoints or
parameters that are not in the returned documentation.
```

Tout ce qui est critique est **dupliqué** dans les descriptions de tools : certains clients ignorent `ServerInstructions`.

### 5.2 Tools

Descriptions en anglais, courtes (< 150 tokens chacune : elles sont rechargées dans le contexte à chaque tour). Les textes ci-dessous sont le contrat ; ils sont versionnés et testés (§9).

#### `list_domains`

- **Description** : `List the API domains with their summary, endpoint count and the platform version. Call once at the start of a session when you don't know which domain a question belongs to. Cheap; result rarely changes.`
- **Entrées** : aucune.
- **Sortie** (Markdown) :
  ```
  Platform: Federer API v2.3.0 (docs generated 2026-08-28)
  - volatility — Volatility surfaces: retrieval, plugging, calibration. (14 endpoints)
  - folio — Folios: search, retrieval, lifecycle. (9 endpoints)
  ```

#### `search_endpoints`

- **Description** : `Find candidate endpoints for a task. Call this before get_endpoint. Several endpoints may match one intent (e.g. search-by-criteria vs get-by-id): present them or ask the user which one. Returns operationId, method, route and a one-line summary. Do not call more than 2 times per question; refine the query instead.`
- **Paramètres** :
  - `query` (string, requis) — `English search terms using API vocabulary: entity, action, parameter names. Translate the user's request; never pass it verbatim. Good: "folio search by criteria", "volatility surface plug shift". Bad: "comment requêter les folios", "help".`
  - `domain` (string, optionnel) — `Restrict to one domain id from list_domains.`
  - `limit` (int, défaut 8, max 20).
- **Sortie** :
  ```
  1. SearchFolio — POST /folios/search — Search folios by name, owner or date criteria. Paginated. [domain: folio]
  2. GetFolioById — GET /folios/{id} — Get one folio with its positions when the id is known. [domain: folio]
  ...
  Next: call get_endpoint(operationId) for details.
  ```
  Résultat vide :
  ```
  No endpoint matched "<query>". The index is in English — retry with English API terms
  (entity + action, e.g. "folio search"). Domains: volatility, folio, pricing.
  ```
- **Implémentation** : recherche lexicale restreinte aux chunks `Kind ∈ {Description, DomainUseCases}` + champs `Title/Summary/Keywords` ; agrégation par `OperationId` (score max) ; les lignes de `DomainUseCases` matchées sont converties en leurs endpoints cibles.

#### `get_endpoint`

- **Description** : `Get the full documentation of one endpoint: description, parameters, response, and a code example. Requires the exact operationId from search_endpoints. Pass the user's question as query so the most relevant sections are returned first; pass language to get only the C# or Python example.`
- **Paramètres** :
  - `operationId` (string, requis) — `Exact operationId, case-sensitive, e.g. "GetFolioById".`
  - `query` (string, optionnel) — `The user's question in English; used to rank sections. Omit to get the standard layout.`
  - `language` (`csharp` | `python`, optionnel) — `Return only this language's example. Omit for both.`
- **Sortie** : en-tête fixe (`# GetFolioById — GET /folios/{id}` + summary + version + deprecated), puis sections dans l'ordre `Description, Parameters, Response, Example(s), Notes` en mode standard ; en mode `query`, sections classées par pertinence lexicale avec `Parameters` toujours incluse. Pied : `Related: SearchFolio, CloseFolio` (depuis `related`). Budget 2 500 tokens.
- `operationId` inconnu : retourne les 3 `operationId` les plus proches (fuzzy sur `Title`) — `Unknown operationId "GetFolio". Did you mean: GetFolioById, GetFolioPositions?`

#### `search_docs`

- **Description** : `Full-text search across all documentation sections (domain overviews, workflows, authentication, errors, endpoint descriptions). Use for cross-cutting questions (auth, pagination, conventions, "how do I…") when no single endpoint is the answer. Returns ranked sections with their breadcrumb.`
- **Paramètres** : `query` (même description que ci-dessus), `domain?`, `limit` (défaut 5, max 10).
- **Sortie** : pour chaque chunk `### <Breadcrumb>` + contenu, budget 1 500 tokens, pied avec les sections non incluses.

#### `reindex` (administration, désactivé par défaut en stdio, protégé en HTTP)

- Reconstruit le snapshot depuis la source ; retourne le rapport de validation (`DOC-FORMAT §7`) et les statistiques (fichiers, chunks, durée).

### 5.3 Resources

- `endpoint://{operationId}` — document endpoint complet (Markdown brut, sans budget). `resources/list` expose tous les endpoints avec `summary` en description : table des matières gratuite pour les clients qui la lisent.
- `doc://{domain}/_domain` et `doc://_platform` — fichiers de contexte complets.
- Les références `` `GetSurface` `` dans les corps sont réécrites en liens `endpoint://GetSurface` à l'ingestion.

### 5.4 Prompts

- `explain-endpoint(operationId, language?)` — enchaîne `get_endpoint` et demande une réponse structurée : quand l'utiliser, appel minimal, erreurs à gérer.
- `find-endpoint(task)` — enchaîne `list_domains` → `search_endpoints` et demande de proposer les candidats avec leurs différences.

Optionnels ; utiles pour standardiser l'usage dans les équipes.

---

## 6. Ingestion et cycle de vie de l'index

```
Source (dossier docs/ — clone git ou partage)
  → Validation manifest + front matters (échec = fichier rejeté, rapport)
  → Parsing Markdig → sections ## → DocumentChunk (Kind, Language, Breadcrumb, TokenCount)
  → Duplication Summary/Keywords sur les chunks de l'endpoint
  → Réécriture des références `OperationId` en endpoint://
  → Construction Lucene (RAMDirectory) [+ embeddings v2, cache disque par ContentHash]
  → Snapshot immuable { Catalogue (FrozenDictionary), Chunks[], IndexSearcher, Vectors[] }
  → Interlocked.Exchange(ref _current, snapshot) ; l'ancien snapshot est disposé après grâce
```

- **Atomicité** : aucune requête ne voit un index partiel. Si la construction échoue, l'ancien snapshot reste servi et l'erreur est journalisée.
- **Déclencheurs** : au démarrage ; `FileSystemWatcher` avec debounce 2 s (mode local) ; tool `reindex` / endpoint HTTP `POST /admin/reindex` (appelé par Jenkins après push sur le dépôt de doc) ; polling git optionnel.
- **Incrémental** : `ContentHash` par fichier ; seuls les fichiers modifiés sont re-parsés (et ré-embarqués en v2). L'index Lucene est reconstruit entièrement (quelques centaines de ms) : plus simple et sûr que la mise à jour in-place.
- **Cache de démarrage** : si la construction dépasse l'objectif (2 s), sérialiser le snapshot (chunks + vecteurs) sur disque, clé = hash du manifest.
- **Threading** : `IndexSearcher` Lucene est thread-safe en lecture ; un `IndexSearcher` par snapshot, jamais partagé entre snapshots.

---

## 7. Transport, hébergement, sécurité

| Phase | Transport | Hôte | Auth | Usage |
|---|---|---|---|---|
| v1 | **stdio** | `Host.CreateApplicationBuilder` (Generic Host), lancé par le client MCP | Aucune (process local de l'utilisateur) | Un index par poste ; `docs/` = clone git local |
| v1.5 | **Streamable HTTP** | ASP.NET Core sur .NET 8 (`MapMcp`), service Windows ou conteneur | Keycloak (Bearer JWT) via `AddAuthorizationFilters` + `[Authorize]` ; obligatoire avant tout déploiement non local | Index partagé, mis à jour par Jenkins |

- Le code Domain/Application/Infrastructure est identique dans les deux cas ; seul le projet hôte change.
- En stdio : **stdout est réservé au protocole**. Logs sur stderr ou fichier.
- Le serveur ne journalise pas le contenu des requêtes `query` par défaut (peuvent contenir des informations métier) — uniquement des métriques agrégées (§8). Activable en debug.
- Aucune donnée sortante : pas d'appel réseau depuis le serveur (les modèles v2 sont des fichiers locaux).

---

## 8. Observabilité

- Métriques (`System.Diagnostics.Metrics`, exportables OTLP quand la plateforme OpenTelemetry sera en place) : appels par tool, latence par tool, tokens renvoyés par appel, résultats vides, taille de l'index, durée d'ingestion, échecs de validation.
- Logs structurés `LoggerMessage` : `IndexRebuilt(files, chunks, ms)`, `ValidationFailed(file, rule)`, `ToolInvoked(tool, hits, tokens, ms)`.
- Un compteur clé : **taux de résultats vides de `search_endpoints`** — c'est l'indicateur direct d'un problème de vocabulaire (doc ou descriptions de tools).

---

## 9. Qualité : jeu de questions et benchmark

Fichier `benchmark/questions.yaml`, versionné avec le serveur :

```yaml
- id: q001
  question: "Comment requêter les folios ?"
  expected: [SearchFolio, GetFolioById]        # tous acceptés dans le top 3
  kind: intent
- id: q002
  question: "quel paramètre pour le tenor sur Plug"
  expected: [Plug]
  expected_chunk: Plug/Parameters
  kind: parameter
```

Deux niveaux de test :

1. **Retrieval pur** (test xUnit, rapide, à chaque commit) : la question est reformulée par une table de correspondance FR→EN fixe (simule le LLM), puis `search_endpoints` est appelé directement. Mesures : rang du premier `expected`, MRR, hit@3, **hit@1**, tokens renvoyés. Seuils = objectifs §1 ; régression = échec de build.

   Deux leçons tirées de l'ADR #20, à conserver :
   - **hit@3 seul ne suffit pas.** Il cachait un défaut où le bon endpoint était systématiquement deuxième derrière un endpoint de l'autre domaine. C'est hit@1 qui l'a révélé. Le seuil de build reste hit@3 ≥ 0,90 ; hit@1 est rapporté et surveillé.
   - **Le jeu de questions doit contenir des requêtes courtes.** Les descriptions de tools demandent au modèle d'envoyer « entité + action » ; un jeu composé uniquement de phrases complètes ne teste donc pas ce que le serveur reçoit réellement. Les questions marquées `terse: true` (1 à 3 tokens) sont mesurées séparément.

   S'y ajoute `benchmark/heldout-terse.yaml`, un jeu **held-out** généré par des agents n'ayant lu que `docs/` et ignorant tout du réglage, puis filtré de façon adversariale. Il n'est pas un seuil de build : il arbitre les réglages (`RetrievalTuningSweepTests`), précisément parce qu'aucun réglage n'a pu être ajusté sur lui.
2. **Bout en bout** (manuel ou nightly) : la question est posée à un agent réel avec le MCP branché. Mesures : nombre d'appels, tokens de doc consommés, et score 1–5 par un LLM juge sur la réponse finale (couvre la question ? cite le bon endpoint ? exemple correct ?). C'est la méthode de Context7.

Toute modification des **descriptions de tools**, des **boosts**, des **synonymes** ou du **format de doc** doit être accompagnée du résultat du benchmark.

---

## 10. Plan de livraison

| Étape | Contenu | Critère de sortie |
|---|---|---|
| 0 | Générateur produit `docs/` conforme à `DOC-FORMAT.md` sur 2 domaines | Validation §7 passe |
| 1 | Ingestion + snapshot + Lucene + `list_domains`, `search_endpoints`, `get_endpoint` en stdio | Benchmark retrieval ≥ seuils sur 30 questions |
| 2 | `search_docs`, resources, prompts, `reindex`, `FileSystemWatcher` | Utilisable au quotidien par 2–3 développeurs |
| 3 | Itération descriptions/boosts/synonymes sur retours réels | Taux de résultats vides < 5 % |
| 4 (v1.5) | Hôte HTTP + Keycloak + Jenkins | Déploiement partagé |
| 5 (v2) | Embeddings multilingues + RRF + reranker ONNX | Gain mesuré sur les questions `kind: intent` en français |

---

## 11. Décisions et alternatives (résumé ADR)

| # | Décision | Alternatives | Raison |
|---|---|---|---|
| 1 | In-process, snapshot immuable | Base externe | Aucune infra ; corpus petit ; simplicité ; atomicité |
| 2 | Lucene.NET pour le lexical | SQLite FTS5 (repli), BM25 maison | Leviers d'itération qualité (analyzer par champ, synonymes, requêtes par objets) ; statut du package à valider |
| 3 | 4 tools + resources, `query` partout | 1 tool générique ; 10 tools spécialisés | Pattern resolve→get de Context7 ; peu de tools = moins d'appels erronés |
| 4 | Filtrage/classement côté serveur, budget de tokens | Renvoyer tout et laisser le LLM trier | Leçon Context7 : −65 % tokens, −30 % appels |
| 5 | Traduction déléguée au LLM + `keywords` bilingues | Traduction côté serveur | Pas de modèle disponible ; le LLM le fait bien si instruit |
| 6 | stdio d'abord, HTTP ensuite | HTTP direct | Zéro déploiement pour valider la valeur ; même code applicatif |
| 7 | Benchmark versionné comme critère de tout changement | Jugement manuel | Les descriptions et boosts sont du code de prompt : ils se testent |
| 8 | Titres de sections libres dans `_platform.md` | Réutiliser les 5 titres de `_domain.md` ; fermer la liste dans `DOC-FORMAT §7` | `DOC-FORMAT §7` ne contraint les titres que pour les fichiers endpoint et domaine ; le contenu imposé par `§4.3` (URLs par environnement, pagination, versioning, limites de débit, conventions) n'entre pas dans les 5 titres de domaine. Liste retenue pour le corpus POC : Overview, Environments and base URLs, Authentication, Common headers, Pagination, Errors, Rate limits, Versioning and deprecation, Conventions. À figer dans `§7` le jour où le générateur produit ce fichier |
| 9 | Verbe standardisé porté par le `summary`, pas par l'`operationId` (`Recalibrate` a pour summary « Update a volatility surface by recalibrating it… ») | Renommer l'`operationId` ; élargir la liste de verbes de `DOC-FORMAT §5.4` | `§5.4` impose le verbe standardisé dans le summary ; garder le verbe métier dans `operationId`, `aliases` et `keywords` préserve la recherche par nom d'opération (boost ×10 sur match exact d'`operationId`, `§4.2`) |
| 10 | Paramètres de chemin nommés d'après la ressource (`/folios/{folioId}`, `/volatility/surfaces/{surfaceId}`) | `/folios/{id}` comme dans l'exemple de sortie de `§5.2` | Cohérence du corpus et du filet lexical : les utilisateurs cherchent par nom de paramètre (`folioId`), qui doit exister dans `keywords` (`DOC-FORMAT §5.2`). L'exemple de `§5.2` illustre le format de sortie du tool, pas le contrat de route |
| 11 | Le projet hôte `ApiDocs.Mcp.Stdio` référence `ApiDocs.Infrastructure` en plus d'`ApiDocs.Application` | Enregistrement des implémentations par réflexion ; projet `Bootstrap` dédié | L'hôte est la racine de composition : c'est le seul endroit qui a le droit de connaître les implémentations. Le sens des dépendances de `§2` reste vrai pour le code métier (`Infrastructure → Application → Domain`, `Domain` ne référence rien) |
| 12 | `search_endpoints` interroge **tous** les champs indexés (`Title`, `Breadcrumb`, `Summary`, `Keywords`, `Body`) en restreignant les chunks à `Kind ∈ {Description, DomainUseCases}` | Restreindre aussi les champs à `Title/Summary/Keywords` comme le suggère la lettre de `§5.2` | Le tableau `Use cases` est porté par le `Body` du chunk `DomainUseCases` : l'exclure vide de sa substance la section la plus utile aux questions d'intention (`DOC-FORMAT §4.2`). Le classement reste dominé par les boosts de `§4.1` |
| 13 | Les boosts par `Kind` de `§4.2` (×1.5 intention, ×0.8 exemples dans `search_docs`) sont appliqués **après** le scoring BM25, dans le use case | Boost par document à l'indexation | Lucene 4.8 n'expose plus de boost par document, seulement par champ ; appliquer le facteur après coup garde un seul index partagé par les quatre tools et rend le réglage testable sans réindexation |
| 14 | `search_endpoints` élimine les candidats dont le score est sous 25 % du meilleur | Renvoyer `limit` résultats quoi qu'il arrive | `MinimumNumberShouldMatch = 1` fait remonter tout endpoint partageant un seul token : la queue de liste est du bruit qui coûte des tokens et invite le modèle à choisir le mauvais endpoint |
| 15 | `resources/list` est servi par des handlers bas niveau (`WithListResourcesHandler`, `WithReadResourceHandler`) plutôt que par les attributs `[McpServerResource]` | Ressource template par attribut uniquement | Les attributs n'exposent qu'un *template* ; `§5.3` demande que `resources/list` énumère chaque endpoint avec son `summary` (table des matières gratuite) |
| 16 | Ajout du package `Microsoft.ML.Tokenizers.Data.Cl100kBase` | `Microsoft.ML.Tokenizers` seul | Sans lui, `TiktokenTokenizer.CreateForEncoding` télécharge le vocabulaire au démarrage, ce qui viole « aucun appel réseau depuis le serveur » (`§7`). Le package embarque le vocabulaire |
| 17 | `NuGet.config` versionné avec une source unique | Laisser la configuration NuGet de chaque poste | La gestion centralisée des versions (`Directory.Packages.props`) échoue en NU1507 dès qu'un poste déclare plusieurs sources ; la source unique rend la restauration reproductible |
| 18 | « Did you mean » : un candidat préfixe (ou préfixé par) l'`operationId` demandé est classé avant un candidat à distance d'édition équivalente | Distance de Levenshtein seule | `GetFolio` est `GetFolioById` tronqué, pas une faute de frappe de `CreateFolio` ; c'est le cas d'usage réel (le modèle devine un nom court) |
| 19 | `.mcp.json` pointe sur une copie publiee (`dotnet publish -c Release -o .mcp-server`, dossier ignore par git) plutot que sur `dotnet run --project` | `dotnet run` comme indique dans `CLAUDE.md` ; binaire auto-contenu avec RID | `dotnet run` verrouille `src/ApiDocs.Mcp.Stdio/bin/**` pendant toute la vie du serveur : tant qu'un client MCP est connecte, `dotnet build` et `dotnet test` echouent sur un verrou de fichier, ce qui rend developpement et usage mutuellement exclusifs. La copie publiee est un jeu de binaires distinct, donc les deux coexistent (verifie : rebuild + 48 tests pendant que le serveur tournait). Le dossier `docs/` reste lu **en direct** depuis la racine du depot, donc une evolution du corpus ne demande qu'un redemarrage du serveur ; seule une evolution du **code** impose un `dotnet publish`. Framework-dependent et sans RID pour rester portable |
| 20 | Deux facteurs multiplicatifs post-BM25 sur `search_endpoints` (couverture ×2, affinité de domaine ×2) et désingularisation légère sur `Title`/`Breadcrumb`/`Keywords` | Ne rien changer ; abaisser le `b` de BM25 ; enrichir les `keywords` du corpus | Défaut constaté en testant le serveur publié : `list folios` renvoyait `ListSurfaces` en premier, `folio list` et `list portfolios` aussi. Trois causes cumulées — le `Title` ×4 avec un IDF élevé sur un verbe générique, l'absence de désingularisation qui empêche `folios` d'atteindre le token `folio` d'un `Title`, et la normalisation par longueur de BM25 qui favorise le `summary` le plus court. Le corpus n'était pas en cause : `search-folio.md` portait déjà `folios` et `list` dans ses `keywords`. Les quatre leviers ont été croisés par `RetrievalTuningSweepTests` sur 48 configurations, jugées sur hit@1 (hit@3 masquait le défaut : le bon endpoint était deuxième) et sur un **jeu held-out** de 50 requêtes courtes rédigées par des agents n'ayant lu que `docs/`. Abaisser `b` n'apporte rien. Configuration retenue = la moins distordante atteignant le plateau mesuré. **Résultat : hit@1 89 % → 96 % (jeu rédigé), 80 % → 93 % (requêtes courtes), 80 % → 92 % (held-out) ; MRR 0.940 → 0.972 ; hit@3 reste à 100 %** |
