# Lettra

Logiciel de devis et factures multi-tenant pour cabinets d'experts-comptables : .NET 10, Blazor WebAssembly
et Blazor Hybrid (.NET MAUI), une base par dossier, cinq modes de déploiement, français par défaut et anglais.

Le dossier d'architecture (annexes A à G, ADR 0001 à 0014) fait foi ; ce dépôt en est l'implémentation.
Ce socle correspond à la **phase 0** de l'annexe C : une tranche verticale « créer un client » qui traverse
toutes les couches, en WASM (gateway HTTP) et en MAUI Windows (gateway in-process).

## Démarrer

Prérequis : SDK .NET 10 (voir `global.json`). Docker pour les tests PostgreSQL et SQL Server.

```bash
dotnet run --project src/Hosts/Lettra.Api      # API + application WASM sur https://localhost:7180
dotnet test Lettra.Ci.slnf                     # tous les tests (hors MAUI)
```

L'API démarre en mode 2 (réseau local) sur un dossier SQLite de démonstration (`appsettings.Development.json`).
L'application MAUI (`src/Hosts/Lettra.App.Maui`) se construit sous Windows avec la charge de travail
`maui-windows` ; elle tourne en mode 1 (poste isolé) : au premier lancement, elle crée le cabinet local et un premier dossier chiffré.

`Lettra.slnx` contient tout ; `Lettra.Ci.slnf` exclut le projet MAUI pour les machines Linux et la CI.

## Structure

```
src/
  Core/            Calculations (arrondi unique), Contracts (API publique), Domain (0 dépendance), Application (cas d'usage, ports)
  Infrastructure/  Persistence (EF Core) + un projet de migrations par provider (Sqlite, PostgreSql, SqlServer),
                   Catalog (registre des dossiers), Identity.Local, Identity.Oidc, External (SIRENE, VIES), Observability
  Modules/         Supervision (Domain, Application, Infrastructure) : alertes et centre d'alertes (annexe G)
  Composition/     Bootstrap : AddLettra(), le seul endroit qui connaît le mode de déploiement
  Presentation/    UI.Abstractions (ports), UI.Shared (toute l'UI, RCL), UI.Gateway.Http, UI.Gateway.InProcess
  Hosts/           Api (Minimal APIs, sert le WASM), Web (WASM), App.Maui (Blazor Hybrid), Worker (jobs SaaS)
  Ai/              Couche IA (annexe B) : Skills (Paperasse épinglé), Privacy (pseudonymisation), Tools, Orchestration,
                   Claude et Local (moteurs derrière le port IAiEngine)
tests/             Domain, Application, Architecture, Persistence (3 providers), Api (bout en bout), UI (bUnit), Ai
```

## Ce que le socle garantit (et teste)

| Règle | Où | Test |
|---|---|---|
| Dépendances entre couches (matrice autorisée) | annexe A §1 | `LayerDependencyTests` |
| Le domaine et l'UI ne voient jamais EF ; l'UI ne voit jamais le domaine | ADR-0001, ADR-0005 | `LayerDependencyTests` |
| Chaque commande et requête déclare sa permission | annexe F §4 | `UseCaseRulesTests`, `AuthorizationBehavior` |
| Une requête ne touche que la base du dossier ouvert | ADR-0003 | `TenantGuardBehavior`, `Une_base_par_dossier` (3 providers) |
| Le serveur ne renvoie que des codes ; chacun est traduit en FR et EN | annexe A §4.3 | `ErrorCodeAndResourceTests`, `CustomersPageTests` |
| Même résultat par HTTP et en in-process | ADR-0005 | `GatewayParityTests` |
| Aucune donnée de dossier dans les journaux, à tous les niveaux | annexe G | `NoDossierDataInLogsTests` |
| Une seule règle d'arrondi, TVA par taux | ADR-0005 | `RoundingTests` |
| Une base n'est ouverte que si son estampille est celle du dossier | ADR-0003 | `Une_base_d_un_autre_dossier_est_refusee` (3 providers) |
| Numérotation sans trou ni doublon sous concurrence | ADR-0007 | `Numerotation_continue_sous_concurrence` (3 providers) |
| Base de dossier SQLite chiffrée par sa DEK, DEK enveloppée par la clé du cabinet | ADR-0011 | `LocalDossierTests`, `DossierKeyringTests` |
| Création de dossier, reprise et migration par vagues avec bail | ADR-0009 | `Creation_de_dossier_et_vagues_de_migration_par_le_catalogue` (3 providers) |

## Assistant IA (annexe B, ADR-0006, ADR-0012)

Désactivé par défaut. `POST /api/v1/assistant` (permission `IaUtiliser`) renvoie un flux NDJSON d'événements
(`text`, `rule`, `tool`, `citation`, `warning`, `completed`…) ; `GET /api/v1/assistant/journal` lit le journal du dossier.
L'IA explique et rédige ; elle ne valide, n'émet ni ne supprime rien, et ne voit que des données pseudonymisées.

```json
"Lettra": { "Ai": {
  "Enabled": true, "Engine": "ClaudeEu", "Features": [ "RegulatoryQuestions" ], "OfficialWebLookup": false,
  "Claude": { "BaseUrl": null, "Model": "claude-opus-5-5" },
  "Local": { "Endpoint": null, "Model": "mistral-small" }
} }
```

La clé d'API n'est jamais dans un fichier versionné : `Lettra__Ai__Claude__ApiKey` ou `ANTHROPIC_API_KEY`. Sans clé (ou
sans point d'accès local), le moteur n'existe pas et l'assistant répond `Ai.EngineUnavailable`. Le manifeste du skill pack
se régénère avec `node src/Ai/tools/update-skillpack-manifest.js`.

## Conventions

- Les échecs métier sont des valeurs (`Result`, `Result<T>`), jamais des exceptions ; le domaine ne renvoie que des codes.
- Médiateur maison sans dépendance (`ICommandDispatcher`, `IQueryDispatcher`, `IPipelineBehavior`) ; pipeline :
  journalisation → dossier → autorisation → validation.
- Avertissements bloquants, analyseurs .NET en mode `Recommended`, gestion centralisée des paquets (`Directory.Packages.props`).
- Identifiants en Guid v7, montants en `decimal(18,4)`, dates en `DateTimeOffset` UTC.
- Tests nommés en français (`Une_entreprise_francaise_sans_siren_est_refusee`).

## CI (`.github/workflows/ci.yml`)

Build et tests Linux (dont PostgreSQL et SQL Server via Testcontainers), build MAUI Windows, audit des dépendances
(NuGetAudit bloquant), détection de secrets (gitleaks), SBOM CycloneDX, CodeQL (dépôt public ou GitHub Advanced
Security), vérification des avis de tiers (notice MIT de Paperasse), image de l'API construite, publiée sur GHCR et
signée (cosign, sans clé) sur `main`.

## Persistance et dossiers

| Mode | Registre des dossiers | Bases | Secrets et clés |
|---|---|---|---|
| 1. Poste isolé (MAUI) | registre local (`{id}.lettra` par dossier) | SQLite chiffré (SQLite3 Multiple Ciphers) | KEK du cabinet dans le coffre de l'OS (MAUI `SecureStorage`) |
| 2. Serveur du cabinet | base Catalog (SQLite, PostgreSQL ou SQL Server) | SQLite, PostgreSQL ou SQL Server | `config:` (bases déclarées), `cluster:` (bases créées) |
| 3 et 5. PostgreSQL dédié, SaaS | base Catalog PostgreSQL | une base PostgreSQL par dossier | coffre de la cellule injecté en configuration |

- Les migrations portent le même identifiant dans les trois assemblies de providers (`20261014000001_…`) : après
  `dotnet ef migrations add`, renommer la migration pour aligner son horodatage. Le catalogue a ses propres migrations
  (`CatalogMigrations/`, `--context CatalogDbContext`).
- `Lettra.Worker` : migration du catalogue, reprise des créations de dossiers interrompues, puis migration des bases par
  vagues (canari de 25 bases, arrêt au-delà de 5 % d'échecs, bail par dossier pour plusieurs Workers).
  `Lettra:Worker:RunOnce=true` pour un job de déploiement. Hors `RunOnce`, il lance aussi la passe d'escalade de la
  Supervision chaque minute (`Lettra:Worker:Escalation`).
- Registre des droits (modes 2, 3 et 5) : comptes, groupes, placements des dossiers, attributions et rôles du cabinet
  dans la base Catalog ; `Lettra:Authorization` y est importé au démarrage, sans rien retirer.
- Création d'un dossier : `POST /api/v1/cabinets/{cabinetId}/dossiers` (permission `DossiersCreer` sur le cabinet).
- En SaaS, l'API ne migre jamais une base : elle vérifie l'estampille et laisse la migration au Worker.

## Pas encore dans ce socle

- Cas d'usage d'administration des droits (inviter, rattacher à un groupe, attribuer un rôle) et liste des dossiers
  accessibles à l'utilisateur.
- Identité système du Worker pour exécuter des cas d'usage protégés (compte de service Keycloak, principal interne en mode 1).
- Registre d'alertes persistant : en attendant, chaque processus a le sien, et le Worker n'escalade que ses propres alertes.
- Bases SQLite chiffrées côté serveur (mode 2) et chiffrement de champs en PostgreSQL : la DEK existe déjà par dossier.
- Outbox, synchronisation, `Idempotency-Key`, `ETag` : POC synchronisation puis phase 4.
- Pipeline OpenTelemetry exporté (les conventions et le test « pas de données dans les journaux » sont là) : phase 2.
- Livrables non logiciels de la phase 0 : analyse de risques, AIPD initiale, cible ASVS, références réglementaires relues
  par l'expert-comptable référent.

## Licence

MIT, voir [LICENSE](LICENSE). Composants tiers : [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
