# Qualité du code et couverture des tests

Ce document fait l'état des lieux du code C# de Lettra au regard des bonnes pratiques
(DRY, KISS, SOLID, Clean Architecture) et de la couverture des tests. Il est tenu à jour à
chaque PR du chantier « Qualité du code » : la colonne « État » du plan dit ce qui est fait.

Référence des conventions : `krovomi/clean-architecture` et `krovomi/CleanArchitecture-FullExample`
(médiateur maison `ICommandDispatcher` / `IQueryDispatcher` + `IPipelineBehavior`, valeurs
`Result` / `Result<T>` avec codes d'erreur, entités riches, mappers écrits à la main,
`TimeProvider`, xUnit + FluentAssertions 7.2, bUnit pour l'interface).

## Verdict

Le code est **globalement sain** : avertissements traités en erreurs avec les analyseurs .NET en
mode `Recommended`, aucune utilisation de `DateTime.Now` côté serveur, aucun `.Result` / `.Wait()`,
exceptions typées et journalisées, règles de dépendance entre couches vérifiées par
`tests/Lettra.Architecture.Tests`. La couverture est déjà élevée (**88 % des lignes, 77 % des
branches**).

Il n'est pas pour autant exempt de dettes. Les principales :

1. **Duplication (DRY)** : le passage `Result` → réponse HTTP est écrit environ 55 fois dans
   les endpoints ; l'enchaînement « écrire puis relire, 412 si la version a changé » existe en
   4 exemplaires ; « aujourd'hui à Paris » est calculé de 4 façons différentes (dont une qui
   ne se comporte pas comme les autres) ; les éditeurs de devis et de facture Blazor partagent
   une dizaine de méthodes copiées.
2. **Responsabilité unique (SRP)** : quelques méthodes « à tout faire » de plus de 100 lignes
   (`RecordPaymentHandler`, `SyncCoordinator.RunInDossierAsync`, `ComplianceChecker.Check`,
   `AccountingAssistant.AskAsync`).
3. **Domaine anémique par endroits** : des règles métier vivent dans les handlers (acompte,
   suppression des seules factures en brouillon, rôles personnalisés) au lieu des entités.
4. **Un vrai défaut** : une modification concurrente d'une alerte de supervision lève
   `DbUpdateConcurrencyException`, que le pipeline ne traduit pas ; l'utilisateur reçoit une
   erreur 500 au lieu du conflit de version attendu.
5. **Une fragilité réglementaire** : la vérification des mentions légales repère certaines
   mentions en cherchant un mot dans le texte imprimé (« débits », « CGI », « 40 » et
   « recouvrement »). Reformuler une mention casserait la vérification sans qu'aucun test ne
   le signale.

## Couverture mesurée

Mesure du 19 octobre 2026 sur `main` (464190d), 14 projets de tests, 3 290 tests, dont les
tests d'intégration sur SQLite, PostgreSQL et SQL Server (Testcontainers) et l'import du realm
Keycloak. Sont exclus du calcul : le code généré (migrations EF, `*.Designer.cs`, instantanés
de modèle) et les bibliothèques tierces.

| Assembly | Lignes | Branches |
|---|---:|---:|
| **Total** | **88,1 %** (30 584 / 34 727) | **77,2 %** (11 961 / 15 490) |
| Lettra.Calculations | 98,6 % | 98,0 % |
| Lettra.Contracts | 97,7 % | 94,2 % |
| Lettra.Exports.Application | 92,7 % | 81,7 % |
| Lettra.Infrastructure.Persistence | 92,5 % | 84,8 % |
| Lettra.Supervision.Domain | 90,4 % | 82,9 % |
| Lettra.Supervision.Application | 90,1 % | 78,8 % |
| Lettra.Application | 89,9 % | 76,8 % |
| Lettra.Domain | 89,6 % | 85,0 % |
| Lettra.Exports.Infrastructure | 89,2 % | 75,9 % |
| Lettra.Api | 87,0 % | 75,1 % |
| Lettra.UI.Shared (Blazor) | 85,8 % | 71,7 % |
| Lettra.Sync.Client | 83,7 % | 70,1 % |
| Lettra.Sync.Server | 83,6 % | 68,3 % |
| Lettra.Supervision.Infrastructure | 77,2 % | 42,5 % |
| Lettra.Infrastructure.Speech.OnDevice | 59,6 % | 47,1 % |
| Lettra.UI.Gateway.Http | 51,1 % | 58,8 % |
| Lettra.UI.Gateway.InProcess | 50,9 % | 30,8 % |

Les autres assemblies (IA, voix, identité, impression, données de démonstration, observabilité)
sont entre 90 et 100 %.

Les trous les plus importants :

- **Passerelles HTTP** (`Lettra.UI.Gateway.Http`) : `HttpAdministrationGateway`,
  `HttpBankingGateway`, `HttpTaxGateway`, `HttpAssistantGateway` et `HttpAlertsGateway` ne sont
  jamais exécutées par les tests ; `HttpBillingGateways` et `HttpPartiesAndCatalogGateways` le
  sont très peu.
- **Passerelles en processus** (`Lettra.UI.Gateway.InProcess`, utilisées par l'application
  installée hors ligne) : la moitié n'est pas exécutée.
- **Supervision** : `EfAlertStore` (branches de concurrence) et les tâches de fond.
- **Tâche de fond de la vue cabinet** (`CabinetOverviewJob`) : non testée.
- **Éditeurs Blazor** : `InvoiceEditor.razor` (137 lignes non couvertes), `QuoteEditor.razor`,
  `ReconciliationPage.razor`, `DocumentLinesGrid.razor`, `TeamPanel.razor`, `VoiceHost.razor`.
- **Moteur de dictée embarqué** (`WhisperNetSpeechToText`) : dépend du modèle Whisper, testé
  seulement par le workflow `voix.yml`.
- **Hôte Worker** (`Lettra.Worker`) : aucun projet de test ne le référence, il n'apparaît donc
  pas dans la mesure. Ses tâches vivent dans les modules et y sont testées ; un projet de test de
  l'hôte demande une modification de la solution (fichier tenu par le fil Socle).

### Mesurer soi-même

Aucun paquet supplémentaire n'est nécessaire : le collecteur « Code Coverage » est fourni avec
`Microsoft.NET.Test.Sdk`.

```bash
dotnet build Lettra.Ci.slnf -c Release
dotnet test Lettra.Ci.slnf -c Release --no-build \
  --collect "Code Coverage;Format=cobertura" --results-directory TestResults/couverture
python3 build/couverture/fusionner.py TestResults/couverture 30
```

Le script fusionne les rapports des 14 projets de tests (une ligne est couverte si au moins un
projet l'exécute), écarte le code généré et affiche la couverture par assembly, puis les
fichiers qui ont le plus de lignes non couvertes.

Attention : sous instrumentation de couverture, le test d'architecture
`Les_domaines_n_utilisent_aucun_framework` échoue (l'instrumentation ajoute une référence
`Microsoft.CodeCoverage` dans `Lettra.Domain`). Il passe normalement sans `--collect`. Une étape
de couverture en CI doit donc soit exclure `Lettra.Architecture.Tests`, soit le lancer à part.

## Écarts relevés

Chaque écart a été vérifié dans le code. Les chemins sont relatifs à `src/`.

### Défauts

| # | Écart | Où | Correction |
|---|---|---|---|
| D1 | Un conflit de version sur une alerte lève `DbUpdateConcurrencyException`, non traduit par `ConcurrencyBehavior` (qui n'attrape que `ConcurrencyConflictException`) : erreur 500 au lieu d'un 412. | `Modules/Supervision/Lettra.Supervision.Infrastructure/EfAlertStore.cs:108`, `Core/Lettra.Application/Behaviors/ConcurrencyBehavior.cs:21` | Lever `ConcurrencyConflictException`, comme `DocumentNumbering`. |
| D2 | Les mentions légales sont repérées par recherche de mots dans le texte généré. | `Core/Lettra.Calculations/Compliance/ComplianceChecker.cs:168, 217, 245`, `Core/Lettra.Domain/Billing/Invoice.cs:172, 621` | Textes et prédicats de reconnaissance au même endroit (`LegalMentionTexts`) : le texte imprimé et sa vérification ne peuvent plus diverger. Des codes structurés demanderaient de changer le format des pièces enregistrées, pour un gain faible. |
| D3 | `SyncCenter.OnSourceChanged` est un `async void` qui avale toute exception sans la journaliser. | `Modules/Sync/Lettra.Sync.Client/SyncCenter.cs:178-188` | Journaliser l'exception. |
| D4 | Un délai d'attente HTTP devient une exception à l'écran pour la plupart des passerelles, mais « serveur injoignable » pour d'autres. | `Presentation/Lettra.UI.Gateway.Http/HttpPartiesAndCatalogGateways.cs:25`, `HttpBillingGateways.cs:37` contre `HttpCabinetGateway.cs:50` | Un seul filtre d'exception partagé. |
| D5 | Plusieurs écrans prennent la date du poste (`DateTime.Today`, `DateTimeOffset.Now`) au lieu de `TimeProvider`. | `Presentation/Lettra.UI.Shared` : `DossierReviewPage.razor:101`, `CabinetPage.razor:285`, `FecExport.razor:96`, `GrantAccessForm.razor:141`, `PlatformTransmissionPanel.razor:108`, `Administration.razor:83` | Une horloge d'interface injectée. |
| D6 | Supprimer un devis ou une facture avec une version périmée : en direct, refus « conflit de version » ; par HTTP, une `JsonException` remonte jusqu'à l'écran, car la pièce jointe au 412 est relue comme un `ProblemResponse`. Trouvé par les tests de parité des passerelles. | `Presentation/Lettra.UI.Gateway.Http/HttpBillingGateways.cs:33`, `HttpCustomersGateway.cs:96` | Pièce jointe illisible ignorée, le refus garde son code (corrigé, PR 2). |

### Duplication (DRY)

| # | Écart | Où |
|---|---|---|
| R1 | `result.IsSuccess ? TypedResults.Ok(...) : result.Errors.ToProblem()` écrit environ 55 fois, plus 4 aides privées équivalentes. | `Hosts/Lettra.Api/Endpoints/*` (`BillingEndpoints.cs:350`, `BankingEndpoints.cs:180`, `ExportEndpoints.cs:159`, `AdministrationEndpoints.cs:121`) |
| R2 | « Écrire, relire, joindre la version courante en cas de conflit » en 4 exemplaires, dont deux identiques ligne à ligne. | `Presentation/Lettra.UI.Gateway.InProcess/InProcessPartiesAndCatalogGateways.cs:53` et `InProcessBillingGateways.cs:24`, `Hosts/Lettra.Api/Infrastructure/ConcurrencyResults.cs:71` |
| R3 | « Aujourd'hui à Paris » calculé 4 fois, avec deux politiques de repli différentes ; « Europe/Paris » écrit dans 10 fichiers. | `Core/Lettra.Application/Features/Billing/BillingSupport.cs:10`, `Modules/Exports/Lettra.Exports.Application/Banking/BankingFeatures.cs:106`, `Tax/TaxQueries.cs:200`, `EInvoicing/Platform/Receivables.cs:82` |
| R4 | Validation d'un règlement écrite dans le handler et dans l'entité. | `Modules/Exports/Lettra.Exports.Application/Banking/Payments.cs:83-133` et `Core/Lettra.Domain/Banking/Payment.cs:142-157, 243-271` |
| R5 | Préambule « charger, introuvable, vérifier la version, brouillon ? » copié dans une dizaine de handlers ; `IVersionedCommand` n'est utilisé par rien. | `Core/Lettra.Application/Features/Billing/QuoteFeatures.cs:195, 230, 319, 344`, `InvoiceFeatures.cs:229, 274, 322, 404` |
| R6 | 83 réemballages `Result<T>.Failure(x.Errors)` faute de conversion et de `Bind` sur `Result`. | `Core/Lettra.Domain/Common/Result.cs:58-108` |
| R7 | Validateurs de liste paginée identiques ; `MaxPageSize = 200` redéclaré 7 fois. | `QuoteFeatures.cs:97`, `InvoiceFeatures.cs:123`, `SupplierQueries.cs:35`, `CatalogQueries.cs:45` |
| R8 | Fin de validation d'une facture dupliquée entre validation en ligne et import hors ligne. | `Features/Billing/ValidateInvoice.cs:133-143, 195-200`, `Features/Sync/ImportOfflineInvoice.cs:107-142` |
| R9 | Éditeurs de devis et de facture : une dizaine de méthodes copiées (prix, brouillon local, PDF, focus). | `Presentation/Lettra.UI.Shared/Pages/Billing/InvoiceEditor.razor` (836 lignes), `QuoteEditor.razor` (724 lignes) |
| R10 | Squelette de tâche périodique recopié 6 fois, sans `TimeProvider` pour `PeriodicTimer`. | `IntegrityCheckJob`, `FiscalDeadlineAlertJob`, `CabinetOverviewJob`, `OutboxDispatchJob`, `DossierMigrationJob`, `EscalationJob` |
| R11 | Liste des États membres de l'UE en deux exemplaires ; « EUR » défini de 4 façons. | `Calculations/Compliance/ComplianceChecker.cs:102`, `Exports.Application/Tax/DesCalculator.cs:26` |
| R12 | Normalisation de la recherche écrite 10 fois, et pas partout pareil (les devis ne retirent pas les espaces). | endpoints et passerelles |

### Responsabilité unique, ouverture / fermeture

| # | Écart | Où |
|---|---|---|
| S1 | `RecordPaymentHandler` : 16 dépendances, une méthode de 100 lignes qui fait six choses, et une commande imbriquée. | `Exports.Application/Banking/Payments.cs:33-271` |
| S2 | `SyncCoordinator.RunInDossierAsync` : 150 lignes, compteurs mutables capturés dans des closures. | `Modules/Sync/Lettra.Sync.Client/SyncCoordinator.cs:302` |
| S3 | `ComplianceChecker.Check` : 150 lignes, à découper par rubrique (vendeur, client, pièce, lignes, paiement) avec D2. | `Calculations/Compliance/ComplianceChecker.cs:106` |
| S4 | `AccountingAssistant.AskAsync` : 112 lignes (politique, pseudonymisation, contexte, boucle d'outils). | `Ai/Lettra.Ai.Orchestration/AccountingAssistant.cs:28` |
| S5 | `SyncOutboxBehavior` : `switch` sur 9 types de commandes ; une nouvelle commande synchronisable est ignorée sans erreur. | `Modules/Sync/Lettra.Sync.Client/SyncOutboxBehavior.cs:46, 64-150` |
| S6 | `BusinessMetrics` repère les commandes par leur nom de type écrit en chaîne. | `Core/Lettra.Application/Diagnostics/BusinessMetrics.cs:18-73` |
| S7 | Passerelle HTTP clients : le fichier contient aussi les gestionnaires HTTP et l'injection de dépendances de 20 passerelles. | `Presentation/Lettra.UI.Gateway.Http/HttpCustomersGateway.cs` |

### Domaine anémique, frontières de couches

| # | Écart | Où |
|---|---|---|
| A1 | Calcul de l'acompte, « seuls les brouillons se suppriment », détachement de la facture finale du devis : règles écrites dans les handlers. | `QuoteFeatures.cs:242, 395-400`, `InvoiceFeatures.cs:286-296, 374-379` |
| A2 | Approbation d'un avoir : le handler renvoie une erreur, l'entité lève une exception pour le même cas. | `InvoiceFeatures.cs:416`, `Domain/Billing/Invoice.cs:534-537` |
| A3 | Rôles et affectations : simples records, toutes les règles dans les handlers. | `Application/Abstractions/Identity/Authorization.cs:64, 70`, `Features/Rights/RightsCommands.cs:160-215`, `Features/Administration/AdministrationCommands.cs:166-226` |
| A4 | Les endpoints de synchronisation et de démonstration contournent le médiateur (permissions refaites à la main, second conteneur de services à chaque appel). | `Hosts/Lettra.Api/Endpoints/SyncEndpoints.cs:136-153`, `DemoEndpoints.cs:38-54` |
| A5 | Interface Blazor : `IServiceProvider.GetService(...)` (localisateur de services) dans 23 composants. | `Presentation/Lettra.UI.Shared` |
| A6 | État statique modifiable `ExplorerSettings.SearchDelay`, modifié par les tests. | `Presentation/Lettra.UI.Shared/Components/Explorer/RecordExplorerPage.cs:19` |
| A7 | Les modules Exports (64 handlers) et Supervision (10) n'ont aucun `IValidator` : la validation des requêtes est écrite dans les handlers. | `Exports.Application/Banking/Payments.cs:83`, `BankStatements.cs:32-50`, `Tax/TaxDeclarations.cs:107` |
| A8 | Le test de parité des codes d'erreur ne couvre pas `Supervision.Domain`. | `tests/Lettra.Architecture.Tests/ErrorCodeAndResourceTests.cs:29` |

Écarts constatés par les tests de parité, identiques sur les deux transports, à revoir plus tard :
un conflit de version sur les paramètres fiscaux répond 409 sans les paramètres actuels (les
autres pièces joignent la version actuelle) ; un relevé bancaire de format inconnu est refusé avec
`BankStatement.CsvColumnsInvalid` plutôt que `FormatUnknown`.

A2 (approbation d'avoir) et R6 (réemballages de `Result`) sont de faible valeur : la double
vérification d'A2 est une défense en profondeur, et un `Bind` sur `Result` rendrait les handlers
moins lisibles qu'aujourd'hui (KISS). Ils restent listés mais ne seront traités que s'ils gênent.

Ce qui est propre : `Lettra.Calculations`, le module Supervision (petit et cohérent ; son
`AlertCommandHandlers.ApplyAsync` est le bon modèle pour la facturation), l'hôte Worker, la
composition par mode de déploiement, l'identité, la voix, l'impression, les trois fournisseurs
EF (fins, duplication légère).

## Plan

Une PR par couche, petite, verte en CI et assignée à Leub. Chaque refactorisation commence par
des tests qui figent le comportement actuel, puis change le code sans changer le comportement.
Aucun test existant n'est retiré, désactivé ni affaibli.

| PR | Contenu | État |
|---|---|---|
| 1 | Ce document et le script `build/couverture/fusionner.py` ; API : `HttpResults` remplace les ternaires `Result` → HTTP recopiés (R1) ; défauts D1 et D3 corrigés ; parité des codes d'erreur étendue à `Supervision.Domain` (A8). | En revue (PR #119) |
| 2 | Doublons transverses : calendrier légal unique `LegalCalendar` (R3), liste UE et devise uniques (R11), boucle unique des tâches de fond `PeriodicPass` sur l'horloge injectée (R10), textes des mentions légales partagés (D2), « écrire puis relire » unique dans les passerelles en processus (R2), défaut D6, tests de parité de toutes les passerelles (68 cas, HTTP et en direct). | En cours |
| 3 | Domaine et application : règles d'acompte et de suppression dans les entités (A1), rôles (A3), chargement versionné (R5), validateurs paginés (R7), fin de validation de facture (R8), règlement (R4, S1), `IValidator` dans Exports (A7), tests. | À faire |
| 4 | Tests d'infrastructure : tâche de la vue cabinet et ses sources, serveur de synchronisation, webhook de supervision, annuaire d'autorisations. | En cours |
| 5 | API : recherche normalisée (R12), tests. Après le fil « Tris et filtres sur les listes ». | À faire |
| 6 | Passerelles HTTP : gestion d'erreurs partagée (D4), tests de toutes les passerelles (objectif > 90 %). Après la PR du kit de contrôle fiscal. | À faire |
| 7 | Conformité : découpage de `ComplianceChecker.Check` (S3). | À faire |
| 8 | Interface Blazor : horloge injectée (D5), fin du localisateur de services (A5, A6), base commune des éditeurs (R9), tests bUnit. | À faire |

Seuil de couverture proposé pour la CI, une fois ces PR fusionnées : 85 % des lignes et 75 % des
branches au global, sans régression d'une PR à l'autre.
