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
`maui-windows` ; elle tourne en mode 1 (poste isolé) sur une base SQLite locale.

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
tests/             Domain, Application, Architecture, Persistence (3 providers), Api (bout en bout), UI (bUnit)
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

## Pas encore dans ce socle

- Base Catalog, Keycloak, comptes locaux multi-utilisateurs : phase 2 (l'API refuse de démarrer hors développement en mode 2).
- Registre local des dossiers, SQLCipher et coffre de l'OS (MAUI) : phase 2.
- Outbox, synchronisation, `Idempotency-Key`, `ETag` : POC synchronisation puis phase 4.
- Pipeline OpenTelemetry exporté (les conventions et le test « pas de données dans les journaux » sont là) : phase 2.
- Livrables non logiciels de la phase 0 : analyse de risques, AIPD initiale, cible ASVS, références réglementaires relues
  par l'expert-comptable référent.

## Licence

MIT, voir [LICENSE](LICENSE). Composants tiers : [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
