# ApiDocs MCP — POC

Serveur **MCP en .NET 8** qui expose la documentation interne d'une plateforme API à un agent de
code (Claude Code, Gemini CLI, Cursor…). L'agent ne lit plus un dossier de Markdown : il pose une
question, le serveur filtre, classe et renvoie **peu et bien**, dans un budget de tokens.

Tout tourne **dans le process** : pas de base de données, pas de moteur de recherche externe, pas
d'appel réseau sortant. La documentation est un dossier de Markdown ; pour ce POC c'est un corpus
fictif écrit à la main (plateforme « Federer API », domaines `volatility` et `folio`).

| | |
|---|---|
| Recherche | Lucene.NET 4.8 (BM25), lexical seul — pas d'embeddings, pas de reranker |
| Transports | stdio (poste développeur) et Streamable HTTP `/mcp` (serveur partagé) |
| Corpus | 2 domaines · 12 endpoints · 16 fichiers · 89 chunks |
| Qualité | hit@3 **100 %**, hit@1 **96 %**, MRR **0.972** sur 47 questions · 1 445 tokens en moyenne par `get_endpoint` |
| Tests | 53 tests, dont le validateur de format, le benchmark de retrieval et le balayage de réglages |

Les deux documents de conception font foi : [`specs/ARCHITECTURE.md`](specs/ARCHITECTURE.md)
(couches, retrieval, surface MCP, ADR) et [`specs/DOC-FORMAT.md`](specs/DOC-FORMAT.md) (contrat du
corpus). [`CLAUDE.md`](CLAUDE.md) donne les règles de travail dans le dépôt.

---

## Démarrage rapide

Prérequis : **.NET 8 SDK**. Le mode `Git` de l'hôte HTTP exige en plus `git` sur le `PATH`.

```bash
dotnet build
dotnet test                                                    # 53 tests, benchmark inclus
dotnet run --project src/ApiDocs.Mcp.Stdio -- --docs ./docs     # boucle de dev en stdio
```

Inspecter la surface à la main :

```bash
dotnet publish src/ApiDocs.Mcp.Stdio -c Release -o .mcp-server
npx @modelcontextprotocol/inspector dotnet .mcp-server/ApiDocs.Mcp.Stdio.dll --docs ./docs
```

### Brancher un client MCP

[`.mcp.json`](.mcp.json) est versionné et déclare les deux transports. Il lance la **copie publiée**,
jamais `dotnet run` : un `dotnet run` verrouille `src/ApiDocs.Mcp.Stdio/bin/**` tant qu'un client est
connecté, et `dotnet build` / `dotnet test` échouent alors sur un verrou de fichier (ADR #19).

```json
{
  "mcpServers": {
    "api-docs":      { "command": "dotnet", "args": [".mcp-server/ApiDocs.Mcp.Stdio.dll", "--docs", "./docs"] },
    "api-docs-http": { "type": "http", "url": "http://localhost:5080/mcp" }
  }
}
```

`docs/` est lu **en direct** depuis la racine du dépôt : faire évoluer le corpus ne demande qu'un
redémarrage du serveur, seule une évolution du **code** impose un nouveau `dotnet publish`.

---

## Surface MCP

Quatre tools, décrits en anglais et **partagés par les deux transports** (projet `ApiDocs.Mcp`,
ADR #22) : une description ne peut pas diverger entre stdio et HTTP. Les textes sont copiés
verbatim de `ARCHITECTURE.md §5` — ce sont du code de prompt, versionné et mesuré par le benchmark.

| Tool | Rôle | Budget |
|---|---|---|
| `list_domains` | Domaines, résumé, nombre d'endpoints, version de la plateforme | 600 tokens |
| `search_endpoints` | Candidats pour une intention → `operationId`, méthode, route, résumé | 400 tokens |
| `get_endpoint` | Documentation complète d'un endpoint, sections classées par `query`, exemple C# ou Python | 2 500 tokens |
| `search_docs` | Recherche plein texte transverse (auth, pagination, erreurs, conventions) | 1 500 tokens |

Resources : `endpoint://{operationId}` (document brut, sans budget) et `doc://{domain}/_domain`,
`doc://_platform`. `resources/list` énumère chaque endpoint avec son `summary` — une table des
matières gratuite pour les clients qui la lisent.

Les `ServerInstructions` envoyées à l'initialisation cadrent l'usage : questions posées en français,
documentation en anglais, **c'est au LLM de reformuler** en vocabulaire API avant d'appeler un tool
(ADR #5). Le pont FR→EN côté serveur passe par les `keywords` bilingues du corpus.

### Ce que voit l'agent

```
> search_endpoints(query: "folio search by criteria")
1. SearchFolio — POST /folios/search — Search folios by name, owner, currency or date criteria. [domain: folio]
2. GetFolioById — GET /folios/{folioId} — Get one folio with its header and aggregated exposures… [domain: folio]
Next: call get_endpoint(operationId) for details.
```

Un `operationId` inconnu ne renvoie pas une erreur sèche mais les trois plus proches
(`Unknown operationId "GetFolio". Did you mean: GetFolioById, GetFolioPositions?`), en classant
d'abord les candidats dont le nom est un préfixe : le modèle devine un nom court, il ne fait pas de
faute de frappe (ADR #18).

---

## Architecture

```
ApiDocs.Mcp.Stdio | ApiDocs.Mcp.Http  →  ApiDocs.Mcp  →  ApiDocs.Application  →  ApiDocs.Domain
                                          ApiDocs.Infrastructure  →  ApiDocs.Application  →  ApiDocs.Domain
```

`Domain` ne référence rien, `Application` ne référence que `Domain`. Seule exception assumée : les
deux hôtes référencent aussi `Infrastructure`, parce qu'un hôte est la racine de composition et le
seul endroit autorisé à connaître les implémentations (ADR #11). Les hôtes ne contiennent **que** du
câblage : transport, configuration, démarrage, et `/health` côté HTTP.

```
src/
  ApiDocs.Domain/          DocumentChunk · EndpointDescriptor · SectionKind · TokenBudget · Breadcrumb
  ApiDocs.Application/     use cases + ports (IDocumentSource, IChunker, ILexicalIndex, ITokenCounter)
  ApiDocs.Infrastructure/  source Markdown, checkout Git, chunker Markdig, index Lucene, snapshot
  ApiDocs.Mcp/             tools, resources, ServerInstructions — WithApiDocsSurface()
  ApiDocs.Mcp.Stdio/       Generic Host + transport stdio
  ApiDocs.Mcp.Http/        ASP.NET Core + Streamable HTTP sur /mcp + /health
docs/                      le corpus fictif (conforme à DOC-FORMAT.md)
specs/                     ARCHITECTURE.md · DOC-FORMAT.md
tests/ApiDocs.Tests/       unitaires + validateur + benchmark + balayage de réglages
```

### Ingestion et index

```
dossier docs/ → validation manifest + front matter → sections ## → DocumentChunk
             → duplication Summary/Keywords sur chaque chunk → réécriture des liens endpoint://
             → index Lucene (RAMDirectory) → snapshot immuable → Interlocked.Exchange
```

L'index est construit **au démarrage, avant que le transport ne serve** : un client ne voit jamais
un serveur à moitié construit, et un corpus invalide fait sortir le process en code 1 plutôt que de
servir une documentation vide. Les requêtes lisent le snapshot courant sans verrou. Une évolution
de la documentation est prise en compte par un redémarrage (ADR #25).

### Retrieval

Requêtes construites par **objets** (`BooleanQuery`, `TermQuery`, `PrefixQuery`, `FuzzyQuery`),
jamais par un parseur de texte. Analyzer par champ, `Title` ×4, `Summary` et `Keywords` ×3,
`Breadcrumb` ×2, `Body` ×1, et boost ×10 quand un token correspond exactement à un `operationId`
connu. Deux facteurs multiplicatifs s'appliquent **après** BM25 sur `search_endpoints` — couverture
des tokens de la requête et affinité de domaine — parce que BM25 note chaque terme indépendamment
et ignore de quel domaine parle la question (ADR #20).

L'assemblage de la sortie n'ajoute que des chunks **entiers** tant que le budget le permet : un bloc
de code tronqué est pire qu'absent. Le pied de réponse nomme les sections écartées.

Les valeurs d'analyzer et de boost vivent dans `LuceneOptions` et `RankingOptions`, pas dans des
constantes : elles se mesurent.

---

## Le corpus (`docs/`)

Documentation fictive, en anglais, écrite à la main pour le POC et conforme à chaque règle de
validation de `DOC-FORMAT.md §7` — le validateur a été écrit avant les fichiers et tourne dans les
tests. Contenu à saveur finance (surfaces de volatilité, folios, positions), **aucune donnée
interne réelle** : URL de base `https://api.example.internal`.

| Domaine | Endpoints |
|---|---|
| `volatility` | `ListSurfaces` · `GetSurface` · `Plug` · `DeletePlug` · `Recalibrate` · `ComputeImpliedVol` |
| `folio` | `SearchFolio` · `GetFolioById` · `GetFolioPositions` · `CreateFolio` · `CloseFolio` · `ListFolios` *(déprécié)* |

Chaque fichier endpoint porte ses sections `Description`, `Parameters`, `Response`, `Example C#`,
`Example Python`, `Notes`. Chaque `_domain.md` porte une table `## Use cases` aux verbes variés
(find / search / query / look up / retrieve) : c'est elle qui répond aux questions d'intention.

---

## Benchmark

Le benchmark est un **critère de build**, pas un rapport. Il vit dans
`tests/ApiDocs.Tests/benchmark/` :

- `questions.yaml` — 55 questions versionnées, `kind: intent | parameter | example | crosscutting`,
  dont des questions en français et 15 requêtes **courtes** (1 à 3 tokens). La reformulation du LLM
  est simulée par une table FR→EN fixe dans le projet de test.
- `heldout-terse.yaml` — 50 requêtes courtes écrites à partir de `docs/` seul par des agents qui
  n'ont jamais vu le code de retrieval. Elle **arbitre les réglages**, jamais le build : rien ne doit
  y être ajusté, elle se régénère.

Mesures actuelles (`dotnet test`) :

```
search_endpoints sur 47 questions : hit@3 = 100,0 %, MRR = 0,972
  dont requêtes courtes (1-3 tokens) : hit@3 = 100,0 % sur 15 questions
get_endpoint sur 24 appels : 1 445 tokens en moyenne, 1 737 au maximum
```

Seuils qui font échouer le build : hit@3 ≥ 0,90 sur `search_endpoints`, moyenne ≤ 2 500 tokens par
`get_endpoint`. **hit@1 est surveillé de près** : hit@3 seul a une fois masqué un défaut où le bon
endpoint arrivait systématiquement deuxième derrière un endpoint de l'autre domaine (ADR #20). Le
réglage retenu porte hit@1 de 89 % à 96 % sur le jeu rédigé, de 80 % à 93 % sur les requêtes courtes
et de 80 % à 92 % sur le jeu held-out.

`RetrievalTuningSweepTests` note une grille entière de configurations en un seul passage et nomme
les requêtes que chacune rate encore : en cas de doute sur le comportement du retrieval, lancer le
balayage plutôt que raisonner.

> Toute modification d'analyzer, de boost, de synonyme, de description de tool ou de format de doc
> doit être accompagnée du résultat du benchmark dans le message de commit.

---

## Hôte de production (`ApiDocs.Mcp.Http`)

La documentation est centralisée et évolue à son propre rythme : le serveur va donc la chercher.
Au démarrage il fait un `fetch` de la référence configurée, la checkout dans une copie de travail
conservée entre les redémarrages (un redémarrage ne télécharge que le delta), l'indexe, **puis**
seulement sert `/mcp`. Un échec sort en code 1 : une mauvaise configuration fait échouer le
déploiement au lieu de servir un corpus vide.

```bash
dotnet run --project src/ApiDocs.Mcp.Http          # profil de dev : lit le dossier docs/ local
curl http://localhost:5080/health                  # snapshot servi + révision de la doc

dotnet publish src/ApiDocs.Mcp.Http -c Release -o .mcp-server-http
dotnet .mcp-server-http/ApiDocs.Mcp.Http.dll --urls http://localhost:5080 \
       --Docs:Source=Folder --Docs:Path=./docs
```

Même piège qu'en stdio : `dotnet run --project` verrouille `bin/**`, donc lancer la copie publiée
quand le serveur doit rester debout pendant qu'on continue à travailler.

Configuration — section `Docs`, chaque clé ayant sa forme variable d'environnement
(`Docs__Git__Reference`) :

| Clé | Défaut | Rôle |
|---|---|---|
| `Docs:Source` | `Git` | `Folder` (dev, stdio, tests) ou `Git` (production) |
| `Docs:Path` | `docs` | Mode `Folder` uniquement ; dérivé en mode `Git` |
| `Docs:Git:RepositoryUrl` | — | **Requis** en mode `Git` |
| `Docs:Git:Reference` | `main` | Branche ou tag servi |
| `Docs:Git:Subdirectory` | `docs` | Dossier contenant `manifest.json` dans le dépôt |
| `Docs:Git:WorkingCopy` | `docs-checkout` | Conservée entre les redémarrages |
| `Docs:Git:Depth` | `1` | `0` récupère tout l'historique |
| `Docs:Git:Timeout` | `00:02:00` | Budget par invocation de `git` |
| `ASPNETCORE_URLS` | `http://localhost:5000` | À ne pas figer dans `appsettings.json` |

Les identifiants viennent de la machine (clé SSH, credential helper) ; `GIT_TERMINAL_PROMPT=0` est
forcé, donc un identifiant manquant échoue vite au lieu de bloquer un process sans TTY. Un jeton
porté par l'URL de clone est masqué dans les logs **et** dans les messages d'exception.

`GET /health` expose `docsRevision`, le commit réellement indexé — c'est le seul moyen de vérifier
qu'un redémarrage a bien pris la nouvelle documentation :

```json
{ "status": "ok", "platform": "Federer API", "platformVersion": "2.3.0",
  "docsRevision": "09a8616…", "files": 16, "chunks": 89, "endpoints": 12,
  "indexBuildMs": 412, "validationIssues": 0 }
```

---

## Hors périmètre — par décision

Ne pas implémenter, ne pas préparer le terrain :

- **Authentification** (ADR #24) : l'accès est fermé en amont (VPN, mTLS, passerelle). Conséquence
  assumée : qui atteint le port lit toute la documentation, et le serveur ne peut ni tracer ni
  révoquer. Le jour où l'exposition change, le point d'accroche est unique — `app.MapMcp` +
  `RequireAuthorization`.
- **Rechargement à chaud** (ADR #25) : l'index se reconstruit au démarrage, point. `RebuildAsync`
  est déjà idempotent et publie par `Interlocked.Exchange` ; ajouter un déclencheur plus tard n'est
  qu'un `BackgroundService` ou un endpoint.
- Embeddings, reranking, tool `reindex`, persistance de l'index, Aspire.

Les deux premières sont des décisions coûtées, pas des oublis. Chacune a un seul point d'accroche —
ne pas pré-construire pour elles.

---

## Convention de travail

Ordre de livraison, tests d'abord quand le comportement est spécifié, et surtout : **si une décision
n'est pas couverte par les documents de conception, elle y est ajoutée** (table ADR de
`ARCHITECTURE.md §11`) avant d'être codée. Le détail est dans [`CLAUDE.md`](CLAUDE.md).
