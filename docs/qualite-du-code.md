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
branches** à cette mesure ; 92,2 % et 82,1 % après la PR #153, 92,3 % et 82,1 % après la PR #159).

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

Après la PR 2 (branche du 19 octobre, 3 589 tests) : **91,3 % des lignes** (31 693 / 34 731) et
**80,5 % des branches** (12 421 / 15 422). Passerelles en processus 84,7 % (50,9 % avant),
passerelles HTTP 94,4 % (51,1 %), supervision infrastructure 96,2 % (77,2 %), serveur de
synchronisation 99,6 % (83,6 %), API 90,8 %, Application 92,9 %, Domaine 90,9 %.

Après la PR 3 (#139, 3 858 tests) : **91,8 % des lignes** (35 496 / 38 667) et **81,6 % des
branches** (13 829 / 16 949).

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
Avec `--seuil-lignes 88 --seuil-branches 78`, il sort en erreur (code 1) si la couverture
totale passe sous ces seuils : c'est l'étape bloquante de la CI depuis la PR #136.

Attention : sous instrumentation de couverture, le test d'architecture
`Les_domaines_n_utilisent_aucun_framework` échoue (l'instrumentation ajoute une référence
`Microsoft.CodeCoverage` dans `Lettra.Domain`). Il passe normalement sans `--collect`. Une étape
de couverture en CI doit donc soit exclure `Lettra.Architecture.Tests`, soit le lancer à part : la CI
l'exclut de la mesure et le lance dans une étape à part (`.github/workflows/ci.yml`).

## Écarts relevés

Chaque écart a été vérifié dans le code. Les chemins sont relatifs à `src/`.

### Défauts

| # | Écart | Où | Correction |
|---|---|---|---|
| D1 | Un conflit de version sur une alerte lève `DbUpdateConcurrencyException`, non traduit par `ConcurrencyBehavior` (qui n'attrape que `ConcurrencyConflictException`) : erreur 500 au lieu d'un 412. | `Modules/Supervision/Lettra.Supervision.Infrastructure/EfAlertStore.cs:108`, `Core/Lettra.Application/Behaviors/ConcurrencyBehavior.cs:21` | Lever `ConcurrencyConflictException`, comme `DocumentNumbering`. |
| D2 | Les mentions légales sont repérées par recherche de mots dans le texte généré. | `Core/Lettra.Calculations/Compliance/ComplianceChecker.cs:168, 217, 245`, `Core/Lettra.Domain/Billing/Invoice.cs:172, 621` | Textes et prédicats de reconnaissance au même endroit (`LegalMentionTexts`) : le texte imprimé et sa vérification ne peuvent plus diverger. Des codes structurés demanderaient de changer le format des pièces enregistrées, pour un gain faible. |
| D3 | `SyncCenter.OnSourceChanged` est un `async void` qui avale toute exception sans la journaliser. | `Modules/Sync/Lettra.Sync.Client/SyncCenter.cs:178-188` | Journaliser l'exception. |
| D4 | Un délai d'attente HTTP devient une exception à l'écran pour la plupart des passerelles, mais « serveur injoignable » pour d'autres. | `Presentation/Lettra.UI.Gateway.Http/HttpPartiesAndCatalogGateways.cs:25`, `HttpBillingGateways.cs:37` contre `HttpCabinetGateway.cs:50` | Un seul filtre `HttpCalls.IsUnreachable` (erreur réseau ou délai dépassé), une annulation voulue par l'écran remonte (corrigé, PR 3). |
| D5 | Plusieurs écrans prennent la date du poste (`DateTime.Today`, `DateTimeOffset.Now`) au lieu de `TimeProvider`. | `Presentation/Lettra.UI.Shared` : `DossierReviewPage.razor:101`, `CabinetPage.razor:285`, `FecExport.razor:96`, `GrantAccessForm.razor:141`, `PlatformTransmissionPanel.razor:108`, `Administration.razor:83` | `TimeProvider` injecté dans 17 écrans (`UiClock.LocalToday`), enregistré par `AddLettraUi` ; la mesure de la grille, la date provisoire d'aperçu d'un brouillon et le panneau d'une personne suivent depuis la PR 7 bis (corrigé, PR 5 et 7 bis). |
| D6 | Supprimer un devis ou une facture avec une version périmée : en direct, refus « conflit de version » ; par HTTP, une `JsonException` remonte jusqu'à l'écran, car la pièce jointe au 412 est relue comme un `ProblemResponse`. Trouvé par les tests de parité des passerelles. | `Presentation/Lettra.UI.Gateway.Http/HttpBillingGateways.cs:33`, `HttpCustomersGateway.cs:96` | Pièce jointe illisible ignorée, le refus garde son code (corrigé, PR 2). |
| D8 | Des dates du jour lues en UTC alors que le calendrier légal est celui de Paris : entre 22 h et minuit UTC (minuit à 2 h à Paris), la veille. Le PDF d'un brouillon prenait l'horloge système (`DateTime.Today`), le badge d'ancienneté du skill pack de l'assistant le jour UTC, et le kit contrôle fiscal gardait dans la période une écriture enregistrée à 0 h 30 à Paris le lendemain de sa fin. | `Presentation/Lettra.UI.Shared/Billing/PrintableMapper.cs:26, 52`, `Ai/Lettra.Ai.Orchestration/AccountingAssistant.cs:51`, `Modules/Exports/Lettra.Exports.Application/AuditKit/AuditKit.cs:120` | Côté serveur, `LegalCalendar.Today(clock)` et `LegalCalendar.DateOf(instant)` ; côté écran (qui ne voit pas le domaine), `TimeProvider` injecté et `UiClock.LocalToday`, comme les autres écrans. Même règle pour l'horodatage de la mesure de la grille, le panneau d'une personne (`DateTimeOffset.Now`) et l'historique de saisie (`CreatedAt.Date` lu en UTC) (corrigé, PR 7 bis). |
| D9 | Le filtre des données de dossier (`DossierDataScrubber`) passait toute valeur dans une expression régulière bornée à 100 ms, qui masque la valeur entière si le délai expire. L'expression est linéaire (environ 4 ms pour 35 Ko), mais instrumentée par la mesure de couverture sur un runner chargé, elle dépassait le délai : un long texte sans donnée ressortait « *** » et le test `Un_long_texte_sans_donnee_reste_lisible_et_ne_depasse_pas_le_delai` échouait par intermittence. | `Infrastructure/Lettra.Infrastructure.Observability/DossierDataScrubber.cs:47` | Chaque motif contient un « @ » ou un chiffre : une valeur qui n'en a aucun ne passe plus par l'expression (recherche vectorisée `SearchValues`), en production comme sous couverture (corrigé, PR 7 bis). |
| D7 | Une charge Alertmanager en JSON illisible faisait répondre 500 ; Alertmanager rejoue les 5xx, donc la charge revenait sans fin. | `Modules/Supervision/Lettra.Supervision.Infrastructure/SupervisionEndpoints.cs:41` | Répondre 400 (corrigé, PR 2). |

### Duplication (DRY)

| # | Écart | Où |
|---|---|---|
| R1 | `result.IsSuccess ? TypedResults.Ok(...) : result.Errors.ToProblem()` écrit environ 55 fois, plus 4 aides privées équivalentes. | `Hosts/Lettra.Api/Endpoints/*` (`BillingEndpoints.cs:350`, `BankingEndpoints.cs:180`, `ExportEndpoints.cs:159`, `AdministrationEndpoints.cs:121`) |
| R2 | « Écrire, relire, joindre la version courante en cas de conflit » en 4 exemplaires, dont deux identiques ligne à ligne. | `Presentation/Lettra.UI.Gateway.InProcess/InProcessPartiesAndCatalogGateways.cs:53` et `InProcessBillingGateways.cs:24`, `Hosts/Lettra.Api/Infrastructure/ConcurrencyResults.cs:71` |
| R3 | « Aujourd'hui à Paris » calculé 4 fois, avec deux politiques de repli différentes ; « Europe/Paris » écrit dans 10 fichiers. | `Core/Lettra.Application/Features/Billing/BillingSupport.cs:10`, `Modules/Exports/Lettra.Exports.Application/Banking/BankingFeatures.cs:106`, `Tax/TaxQueries.cs:200`, `EInvoicing/Platform/Receivables.cs:82` |
| R4 | Validation d'un règlement écrite dans le handler et dans l'entité. | `Modules/Exports/Lettra.Exports.Application/Banking/Payments.cs:83-133` et `Core/Lettra.Domain/Banking/Payment.cs:142-157, 243-271` (montant recevable unique `Payment.IsValidAmount`, saisie contrôlée par `RecordPaymentValidator` ; corrigé, PR 4) |
| R5 | Préambule « charger, introuvable, vérifier la version, brouillon ? » copié dans une dizaine de handlers ; `IVersionedCommand` n'est utilisé par rien. | `Core/Lettra.Application/Features/Billing/QuoteFeatures.cs:195, 230, 319, 344`, `InvoiceFeatures.cs:229, 274, 322, 404` (`Concurrency.Load` dans 17 handlers ; corrigé, PR 4) |
| R6 | 83 réemballages `Result<T>.Failure(x.Errors)` faute de conversion et de `Bind` sur `Result`. | `Core/Lettra.Domain/Common/Result.cs:58-108` |
| R7 | Validateurs de liste paginée identiques ; `MaxPageSize = 200` redéclaré 7 fois. | `QuoteFeatures.cs:97`, `InvoiceFeatures.cs:123`, `SupplierQueries.cs:35`, `CatalogQueries.cs:45` (`Paging.MaxPageSize` et `Paging.Validate` partagés par le cœur et le module Exports ; corrigé, PR 5) |
| R8 | Fin de validation d'une facture dupliquée entre validation en ligne et import hors ligne. | `Features/Billing/ValidateInvoice.cs:133-143, 195-200`, `Features/Sync/ImportOfflineInvoice.cs:107-142` (`InvoiceValidationEffects.Blocked` et `RecordAsync` ; corrigé, PR 4) |
| R9 | Éditeurs de devis et de facture : une dizaine de méthodes copiées (prix, brouillon local, PDF, focus). | `Presentation/Lettra.UI.Shared/Pages/Billing/InvoiceEditor.razor` (836 lignes), `QuoteEditor.razor` (724 lignes) |
| R10 | Squelette de tâche périodique recopié 6 fois, sans `TimeProvider` pour `PeriodicTimer`. | `IntegrityCheckJob`, `FiscalDeadlineAlertJob`, `CabinetOverviewJob`, `OutboxDispatchJob`, `DossierMigrationJob`, `EscalationJob` |
| R11 | Liste des États membres de l'UE en deux exemplaires ; « EUR » défini de 4 façons. | `Calculations/Compliance/ComplianceChecker.cs:102`, `Exports.Application/Tax/DesCalculator.cs:26` |
| R12 | Normalisation de la recherche écrite 10 fois, et pas partout pareil (les devis ne retirent pas les espaces). | endpoints et passerelles (`ListSearch.Normalize` dans les endpoints, les passerelles et les cas d'usage, devis compris ; la base ignore accents et casse ; corrigé, PR 6) |

### Responsabilité unique, ouverture / fermeture

| # | Écart | Où |
|---|---|---|
| S1 | `RecordPaymentHandler` : 16 dépendances, une méthode de 100 lignes qui fait six choses, et une commande imbriquée. | `Exports.Application/Banking/Payments.cs:33-271` (saisie et déclaration à la plateforme sorties du handler (`RecordPaymentValidator`, `PaymentPlatformReporter`), 14 dépendances au lieu de 16 ; partiel, PR 4) |
| S2 | `SyncCoordinator.RunInDossierAsync` : 150 lignes, compteurs mutables capturés dans des closures. | `Modules/Sync/Lettra.Sync.Client/SyncCoordinator.cs:302` |
| S3 | `ComplianceChecker.Check` : 150 lignes, à découper par rubrique (vendeur, client, pièce, lignes, paiement) avec D2. | `Calculations/Compliance/ComplianceChecker.cs:106` (une méthode par rubrique : émetteur, client, pièce, lignes, TVA et avoir, paiement ; ordre des constats inchangé ; corrigé, PR 6) |
| S4 | `AccountingAssistant.AskAsync` : 112 lignes (politique, pseudonymisation, contexte, boucle d'outils). | `Ai/Lettra.Ai.Orchestration/AccountingAssistant.cs:28` |
| S5 | `SyncOutboxBehavior` : `switch` sur 9 types de commandes ; une nouvelle commande synchronisable est ignorée sans erreur. | `Modules/Sync/Lettra.Sync.Client/SyncOutboxBehavior.cs:46, 64-150` |
| S6 | `BusinessMetrics` repère les commandes par leur nom de type écrit en chaîne. | `Core/Lettra.Application/Diagnostics/BusinessMetrics.cs:18-73` |
| S7 | Passerelle HTTP clients : le fichier contient aussi les gestionnaires HTTP et l'injection de dépendances de 20 passerelles. | `Presentation/Lettra.UI.Gateway.Http/HttpCustomersGateway.cs` |

### Domaine anémique, frontières de couches

| # | Écart | Où |
|---|---|---|
| A1 | Calcul de l'acompte, « seuls les brouillons se suppriment », détachement de la facture finale du devis : règles écrites dans les handlers. | `QuoteFeatures.cs:242, 395-400`, `InvoiceFeatures.cs:286-296, 374-379` |
| A2 | Approbation d'un avoir : le handler renvoie une erreur, l'entité lève une exception pour le même cas. | `InvoiceFeatures.cs:416`, `Domain/Billing/Invoice.cs:534-537` |
| A3 | Rôles et affectations : simples records, toutes les règles dans les handlers. | `Application/Abstractions/Identity/Authorization.cs:64, 70`, `Features/Rights/RightsCommands.cs:160-215`, `Features/Administration/AdministrationCommands.cs:166-226` (portée et échéance dans `RoleDefinition.AllowsScope` et `AcceptsExpiry` ; corrigé, PR 3) |
| A4 | Les endpoints de synchronisation et de démonstration contournent le médiateur (permissions refaites à la main, second conteneur de services à chaque appel). | `Hosts/Lettra.Api/Endpoints/SyncEndpoints.cs:136-153`, `DemoEndpoints.cs:38-54` |
| A5 | Interface Blazor : `IServiceProvider.GetService(...)` (localisateur de services) dans 23 composants. | `Presentation/Lettra.UI.Shared` |
| A6 | État statique modifiable `ExplorerSettings.SearchDelay`, modifié par les tests. | `Presentation/Lettra.UI.Shared/Components/Explorer/RecordExplorerPage.cs:19` |
| A7 | Les modules Exports (64 handlers) et Supervision (10) n'ont aucun `IValidator` : la validation des requêtes est écrite dans les handlers. | `Exports.Application/Banking/Payments.cs:83`, `BankStatements.cs:32-50`, `Tax/TaxDeclarations.cs:107` (premier validateur du module : `RecordPaymentValidator`, PR 4 ; les listes du module passent par `Paging`, PR 5. Les autres contrôles restent là où la saisie est décodée (relevé bancaire en base 64, périodes fiscales du service partagé) : les sortir dupliquerait le décodage) |
| A8 | Le test de parité des codes d'erreur ne couvre pas `Supervision.Domain`. | `tests/Lettra.Architecture.Tests/ErrorCodeAndResourceTests.cs:29` |

Écarts constatés par les tests de parité, identiques sur les deux transports, à revoir plus tard :
un conflit de version sur les paramètres fiscaux répond 409 sans les paramètres actuels (les
autres pièces joignent la version actuelle) ; un relevé bancaire de format inconnu est refusé avec
`BankStatement.CsvColumnsInvalid` plutôt que `FormatUnknown`. Relevés par les tests d'infrastructure : dans
`SyncServer.PushOneAsync`, une intention d'un type inconnu des contrats fait échouer tout le lot au
calcul de l'empreinte, avant la branche qui devait la refuser seule (`SyncServer.cs:271`, code
mort) ; le webhook accepte une gravité numérique (`"1"` lue comme S1).

A2 (approbation d'avoir) et R6 (réemballages de `Result`) sont de faible valeur : la double
vérification d'A2 est une défense en profondeur, et un `Bind` sur `Result` rendrait les handlers
moins lisibles qu'aujourd'hui (KISS). Ils restent listés mais ne seront traités que s'ils gênent.

Ce qui est propre : `Lettra.Calculations`, le module Supervision (petit et cohérent ; son
`AlertCommandHandlers.ApplyAsync` est le bon modèle pour la facturation), l'hôte Worker, la
composition par mode de déploiement, l'identité, la voix, l'impression, les trois fournisseurs
EF (fins, duplication légère).

## Base propre

Depuis la PR 7, la compilation (`TreatWarningsAsErrors`, analyseurs .NET en mode `Recommended`,
`EnforceCodeStyleInBuild`) ne laisse passer ni avertissement ni message de l'IDE de la liste
ci-dessous : chacun est une erreur de build, en local comme en CI. Les règles sont dans
`.editorconfig`, section « Base propre ».

| Règle | Ce qu'elle refuse |
|---|---|
| IDE0005 | `using` inutile (demande `GenerateDocumentationFile`, activé aussi pour les tests, sans exiger de commentaires : CS1591 reste ignoré). |
| IDE0051, IDE0052, IDE0059, IDE0060 | Membre privé jamais utilisé ou jamais lu, affectation inutile, paramètre inutilisé. |
| IDE0004, IDE0035, IDE0370 | Conversion inutile, code inaccessible, `!` superflu. |
| IDE0055 | Mise en forme (indentation, espaces, retours à la ligne). Les blocs de `case` ne sont pas indentés (`csharp_indent_case_contents_when_block = false`), comme le code existant. |
| IDE0028, IDE0300, IDE0301 | Initialisation de collection en expression de collection (`[a, b]`, `[]`). |
| IDE0031, IDE0039, IDE0062, IDE0066, IDE0074, IDE0078, IDE0120, IDE0340, IDE0350 | Simplifications : `?.`, fonction locale plutôt que lambda, fonction locale `static`, expression `switch`, affectation composée, filtre de motif, `Where(...).Any()` → `Any(...)`, type générique non lié, lambda typée implicitement. |

Corrections de la PR 7 (160 fichiers ; couverture ensuite : 92,2 % des lignes, 82,1 % des branches) : 129 `using` inutiles retirés, une trentaine
d'initialisations en expressions de collection, 2 paramètres inutilisés supprimés (`DemoScenarioGenerator.Customers`,
`RecordPaymentHandler.RecordWithNumberAsync`), mise en forme. Un constructeur réservé à EF n'est appelé que par réflexion : IDE0051 le croit inutile. Le
constructeur métier le reprend donc (`: this(id)`, comme `CatalogItem`), ce qui le rend utilisé sans
suppression (`Customer`, `Supplier`). Aucun comportement ne change ; toute
la suite de tests passe.

Règle des dates du jour : la date du jour vient toujours du calendrier légal, jamais de
`DateOnly.FromDateTime(DateTime.UtcNow)`, `DateTime.Now` ou `DateTime.Today`, dans `src` comme dans
`tests`. Côté serveur et dans les tests, `LegalCalendar.Today(clock)` ou `LegalCalendar.DateOf(instant)` ;
dans les écrans (qui ne voient pas le domaine), `TimeProvider` injecté et `UiClock.LocalToday`. Sinon,
entre 22 h et minuit UTC, la date est celle de la veille à Paris (D8 ; `main` rouge sur une CI lancée à
minuit, corrigée par la PR #157).

Le SDK local doit être de la même bande que celui de la CI (dernière 10.0, aujourd'hui 10.0.402) :
`global.json` accepte toute 10.0 récente, et un SDK plus ancien (10.0.1xx) embarque un Roslyn qui
signale des IDE0028 que la CI ne voit pas. Le build d'un projet seul et celui de `Lettra.Ci.slnf`
appliquent les mêmes règles (même `.editorconfig`, même `Directory.Build.props`).

Règles volontairement non imposées, car ce sont des préférences de style sans gain de justesse et
le code ne les suit pas uniformément : IDE0008 / `var` (type explicite), IDE0010 et IDE0072 (cas
manquants d'un `switch`, couverts par les tests), IDE0021 à IDE0024 et IDE0061 (corps
d'expression), IDE0022, IDE0046 (`if` → expression conditionnelle, souvent moins lisible),
IDE0047 / IDE0048 (parenthèses), IDE0058 (valeur de retour ignorée : chaque appel fluide devrait
s'écrire `_ = ...`), IDE0290 (constructeur principal), IDE0305 (`.ToList()` / `.ToArray()` en `[.. requête]` : pas plus
lisible, et le compilateur déplie la décomposition en code à branches, ce qui ajoutait 3 700 branches
fictives à la mesure et faisait passer la couverture des branches de 81,9 % à 77,3 %).

Exceptions :

- **Migrations EF** (`Migrations`, `CatalogMigrations`, `SupervisionMigrations`) : code généré,
  aucune analyse.
- **Application MAUI** (`src/Hosts/Lettra.App.Maui`) : ces règles y restent des suggestions. Elle ne
  se compile qu'avec les charges de travail Android, iOS, Windows et macOS, présentes seulement sur
  les machines de la CI ; elle sera nettoyée à la main puis passée en erreur dans une PR vérifiée
  par la CI.

Suppressions d'avertissements restantes, chacune justifiée sur place :

| Où | Avertissement | Pourquoi |
|---|---|---|
| `Directory.Build.props:18` | CS1591 | Commentaires XML non exigés (seuls les types publics importants sont documentés). |
| `Directory.Build.props:38` (tests) | CA1707, CA2007, CA1515, CA1861, CA2000, CA1062, CA1001, CA1812 | Conventions des tests : noms en phrases avec `_`, pas de `ConfigureAwait`, classes de test publiques, tableaux en ligne dans les `InlineData`, objets jetables gérés par xUnit. |
| `Lettra.Api.csproj:7`, `Lettra.Web.csproj:8`, `Lettra.App.Maui.csproj:25` | CA1515 (et CA1848 pour MAUI) | `Program` et les types d'hôte restent publics pour `WebApplicationFactory` et MAUI. |
| `Lettra.DemoData.csproj:8` | CA5394 | `Random` à graine fixe : le jeu de démonstration doit être reproductible, ce n'est pas de la sécurité. |
| `SharedResource.cs:8`, `DiagnosticsResource.cs:7` | CA1052 | Types marqueurs de `IStringLocalizer<T>` : un type `static` ne peut pas être argument générique. |
| `SyncCenter.cs:187`, `GridBenchmark.razor:158`, `DemoSeeding.cs:99`, `ConnectedDevice.cs:92` | CA1031 | Évènement, page de diagnostic et cycles de fond : une erreur est journalisée ou affichée, jamais fatale. |
| `SqlServerConfigurator.cs:66, 85`, `PostgreSqlConfigurator.cs:83, 101` | CA2100 | Nom de base validé par `Quote` (lettres, chiffres, souligné) ; le DDL ne prend pas de paramètres. |
| `SyncReplica.cs:172`, `AccentInsensitiveSearchScenarios.cs:55` | EF1002 | Noms de tables tirés du modèle EF ou fixes, jamais d'une saisie. |
| `BillingStores.cs:177`, `RecurringInvoiceStores.cs:134` | CA1304, CA1311, CA1862 | Arbre d'expression traduit en `UPPER(...)` SQL, jamais exécuté en .NET. |
| `WhisperModelStore.cs:129` et deux tests de la voix | CA5350 | whisper.cpp ne publie que des SHA-1 : contrôle d'intégrité, doublé d'un SHA-256 épinglé. |
| `RedactionTests.cs:129` | CA1848, CA2254 | Le test simule volontairement un journal mal écrit. |

## Audit d'architecture (octobre 2026)

Deuxième passage complet, sur `main` a6a7c1d : frontières entre couches (hôtes et composition compris), cœur et
modules, interface et passerelles. Chaque écart ci-dessous a été vérifié dans le code ; il s'ajoute à ceux de
« Écarts relevés ». Gravité : **haute** = défaut réel ou frontière franchie, **moyenne** = maintenance, **basse** =
cohérence.

Ce qui est sain et vérifié : aucun hôte ni le jeu de démonstration n'accède à EF ou à une base ; les endpoints passent
par le médiateur (sauf les cas notés) ; le domaine n'a ni setter public ni constructeur public sans paramètre ; aucun
`.Result` / `.Wait()` hors MAUI (X3) ; `ConfigureAwait(false)` partout côté bibliothèques ; toutes les classes sont
`sealed` ; chaque `catch (Exception)` est filtré et journalisé ; `HttpClient` passe par `IHttpClientFactory` sauf X12.

### Gravité haute

| # | Écart | Où |
|---|---|---|
| X1 | Un Worker lancé en job de migration (`RunOnce`) n'est censé que migrer, mais six tâches restent enregistrées hors du `if` (export FEC, outbox, alertes fiscales, vue cabinet, factures récurrentes, relances) : il peut créer des factures ou envoyer des e-mails avant de s'arrêter. | `Hosts/Lettra.Worker/Program.cs:35-47` |
| X2 | L'empreinte d'intégrité figée d'un encaissement (v1) contient `Method.ToString()` et `Status.ToString()` : renommer un membre d'énumération casserait la vérification de tous les journaux scellés. | `Core/Lettra.Domain/Banking/Payment.cs:232, 240` |
| X3 | Démarrage MAUI : `InitializeLettraAsync(...).GetAwaiter().GetResult()` bloque le fil principal (risque de blocage ou d'ANR sur Android). | `Hosts/Lettra.App.Maui/MauiProgram.cs:112` |
| X4 | Les tests d'architecture ne couvrent ni Api, ni Web, ni Worker, ni MAUI, ni DemoData, et exemptent `Lettra.Bootstrap` : un nouveau projet échappe à toutes les règles. | `tests/Lettra.Architecture.Tests/Assemblies.cs:42-50`, `LayerDependencyTests.cs:66-71` |
| X5 | Le jeu de démonstration utilise un type du fournisseur SQLite (`LocalDossierRegistry`), contraire à la règle « seule la composition connaît les fournisseurs », que rien ne vérifie (X4). | `Composition/Lettra.DemoData/LettraDemoData.cs:9, 149, 253` |
| X6 | Liste des encaissements et historique des relances : tout est lu en mémoire puis cherché, trié et paginé en .NET ; la recherche dépend en plus de la culture du serveur. | `Exports.Infrastructure/EfBankingStores.cs:66-76`, `EfReminderStores.cs:100-112` |
| X7 | Aucune des 23 options (`AddOptions` / `Configure`) n'est validée au démarrage : un intervalle nul fait échouer `PeriodicTimer` à l'exécution, un serveur SMTP ou OIDC manquant n'apparaît qu'au premier appel. | toutes les `DependencyInjection` et tâches de fond |
| X8 | Tableau de bord d'accueil : le chiffre d'affaires du mois et le signe des avoirs sont recalculés dans le navigateur en lisant toutes les pièces sur 6 mois, alors que le serveur a la même règle (`BillingKpis`). | `UI.Shared/Billing/DashboardFigures.cs:33-61`, `Pages/Home.razor:283-293` |
| X9 | 209 `CancellationToken.None` dans l'interface ; la recherche différée de l'explorateur et du journal ne transmet pas son jeton : deux recherches qui se chevauchent peuvent afficher l'ancien résultat. | `Components/Explorer/RecordExplorerPage.cs:247-596`, `Pages/Accounting/JournalPage.razor:215, 225` |

### Gravité moyenne

| # | Écart | Où |
|---|---|---|
| X10 | Règles de framework (« ni EF ni ASP.NET ») vérifiées pour `Lettra.Application` seulement, pas pour `Exports.Application`, `Supervision.Application`, `Sync.*` ; les règles des cas d'usage (permission, `Result`) ne voient pas `Supervision.Application`. | `LayerDependencyTests.cs:111`, `UseCaseRulesTests.cs:11` |
| X11 | Trois boucles de tâche périodique contournent `PeriodicPass` et l'horloge injectée (retour de R10) ; la boucle « pour chaque dossier actif » est recopiée 9 fois. | `PaymentReminderJob.cs:51`, `RecurringInvoiceJob.cs:78`, `FecBulkExportJob.cs:85` |
| X12 | Clients Claude hors `AddHttpClient` (sans délai ni résilience) ; `new HttpClient()` pour Vertex. | `Ai/Lettra.Ai.Claude/DependencyInjection.cs:96-126` |
| X13 | Cinq façons de chercher dans les listes (clé sans accents, `SearchText`, `ToUpper().Contains`, mémoire) : la même saisie ne trouve pas la même chose d'un écran à l'autre. | `BillingStores.cs:174`, `RecurringInvoiceStores.cs:131`, `PartiesAndCatalogStores.cs:100`, X6 |
| X14 | Localisateur de services : 34 types de l'interface (21 composants, 13 services, environ 70 appels, mesure plus fine que A5) et 25 appels dans `Sync.Client` ; le port `IDossierWorkScope` impose `Func<IServiceProvider, …>`. | `UI.Shared`, `Sync.Client/SyncCenter.cs`, `SyncCoordinator.cs`, `Application/Abstractions/Sync/Sync.cs:283` |
| X15 | Séquence « ouvrir une portée, initialiser le dossier, lire le contexte » recopiée une vingtaine de fois (synchronisation, passerelles en processus, démonstration) ; `InProcessScope` existe mais n'est presque pas utilisé. | `SyncCoordinator.cs:262, 309, 458`, `InProcess*Gateway.cs`, `DemoDataGateway.cs:25` |
| X16 | Passerelles HTTP : le bloc « appel, conversion, coupure réseau » recopié 16 fois au lieu de `HttpCalls.SendAsync` ; `HttpPilotageGateway` refait le filtre de coupure à la main (retour de D4). | `UI.Gateway.Http/*` |
| X17 | Deux toasters : `AdminToasts` double `ToastService` (même limite, autre durée de vie, sans `TimeProvider`). | `Pages/Administration/AdminToasts.razor` |
| X18 | Endpoints hors médiateur : jeton de droits hors ligne (type concret d'infrastructure), indicateurs Prometheus ; garde « relevé de démonstration » dans l'endpoint et non dans le cas d'usage ; `AlertIngestion` appelée hors pipeline. | `RightsSnapshotEndpoints.cs:36`, `KpiEndpoints.cs:40`, `BankingEndpoints.cs:157`, `Supervision.Application/AlertIngestion.cs:11` |
| X19 | `ErrorKind` n'a pas de valeur « indisponible » : le 503 est choisi par code d'erreur dans les endpoints. | `Domain/Common/Error.cs:4-15`, `SpeechEndpoints.cs:98`, `ServerDiscoveryEndpoints.cs:56` |
| X20 | Trois filtres de permission presque identiques (dossier, cabinet, groupe). | `Api/Security/PermissionEndpointFilter.cs:11-126` |
| X21 | Le module Exports n'a pas de projet Domaine : `PlatformTransmission` (état et invariants) vit dans l'Application et prend des énumérations de contrats. | `Exports.Application/EInvoicing/Platform/PlatformTransmission.cs:13` |
| X22 | Grands composants qui mêlent accès aux données et écran : `DocumentLinesGrid` (603 lignes), `Administration` (723), `RemindersPage` (573), `VoiceHost` (502) ; la dictée charge toute la liste des clients pour reconnaître un nom. | `UI.Shared/Components/Billing/DocumentLinesGrid.razor`, `Voice/VoiceHost.razor:429` |
| X23 | Gestion « occupé / erreur / conflit » recopiée dans 34 pages (57 indicateurs, 9 tests de conflit identiques) ; 47 tâches lancées sans observer leurs exceptions. | `UI.Shared/Pages/*` |
| X24 | Port `IKekStore` défini dans l'Infrastructure mais implémenté par l'hôte MAUI. | `Infrastructure.Persistence/Security/DossierKeyring.cs:11` |
| X25 | Le jeu de démonstration construit un second conteneur complet à chaque chargement ; MAUI décore un service par chirurgie des descripteurs. | `LettraDemoData.cs:131, 215`, `App.Maui/DemoSeeding.cs:29` |
| X26 | Méthodes et classes longues non encore listées : `SpokenNumbers.TryRead` (176 lignes), `CatalogDbContext.OnModelCreating` (124), `FecValidator.Validate` (103), `RecurringInvoice.Apply` (102) ; `Invoice` 745 lignes ; 6 constructeurs à 9 ou 11 dépendances (`AuditKitService`, `ValidateInvoiceHandler`…). | `Core`, `Infrastructure`, `Modules` |

### Gravité basse

Constantes recopiées (« fr-FR » 6 fois, « FR-20 » hors `VatRate.Normal`, troisième liste des États de l'UE dans
`DirectoryLookups.cs:71`, empreinte SHA-256 hexadécimale réécrite 8 fois, formats de date ISO dans 13 fichiers) ;
`MentionCodes` en deux exemplaires ; DTO de requête mêlant `List<T>` et `IList<T>` ; état statique de télémétrie
(`LettraTelemetry.Outbox._last`) au lieu de `IMeterFactory` ; exception d'erreur de format attrapée comme
`NullReferenceException` (`SealedArchives.cs:189`) ; outil de l'assistant en échec sans journal
(`AccountingAssistant.cs:241`) ; message d'exception du fournisseur stocké comme état d'erreur du dossier
(`CatalogDossierProvisioner.cs:108`) ; `SmtpClient` déconseillé par Microsoft ; tables de correspondance statiques
qui pourraient être des `FrozenDictionary` ; jeton porteur comparé deux fois de la même façon (`KpiEndpoints`,
`SupervisionEndpoints`) ; aide JavaScript et `JSDisconnectedException` recopiées dans 15 composants.

### Ordre de correction

Les écarts de gravité haute d'abord, une PR petite et verte par thème ; les fichiers de Socle (`Program.cs`, `slnx`,
workflows, `LayerDependencyTests`, `MauiProgram.cs`) passent par le coordinateur.

| PR | Contenu | Écarts |
|---|---|---|
| 8 | Ce chapitre ; noms d'énumérations des empreintes scellées figés (même sortie) ; règles d'architecture étendues (cas d'usage de Supervision, frameworks pour toutes les couches Application, hôtes et démonstration) ; validation des options au démarrage. | X2, X4, X5, X7, X10 |
| 9 | Encaissements et relances cherchés et paginés en base ; une seule recherche pour toutes les listes. | X6, X13 |
| 10 | Tâches de fond : `PeriodicPass` et horloge injectée partout ; job de migration sans autre tâche (Socle). | X1, X11 |
| 11 | Interface : jetons d'annulation par page, recherche différée annulable, un seul toaster, base « occupé / erreur / conflit ». | X9, X17, X23 |
| 12 | Interface : fin du localisateur de services (implémentations « indisponible » par hôte), indicateurs d'accueil lus au serveur. | X8, X14, A5, A6 |
| 13 | Passerelles : `HttpCalls` et `InProcessScope` partout, fichiers rangés. | X15, X16, S7 |
| 14 | Endpoints et erreurs : `ErrorKind.Unavailable`, filtres de permission unifiés, endpoints restants via le médiateur. | X18, X19, X20, A4 |
| 15 | Découpages : longues méthodes, grands composants, constructeurs chargés ; domaine du module Exports. | X21, X22, X26, R9, S2, S4 |
| 16 | Basse gravité, au fil des PR qui touchent les fichiers. | basse |

### Fait dans la PR 8

- **X2** : les noms des énumérations écrites dans les empreintes scellées (forme canonique d'une pièce, empreinte v1
  d'un encaissement) sont figés par `SealedFingerprintTests` ; l'empreinte calculée ne change pas. Ajouter un membre
  reste possible, en renommer ou en retirer un fait échouer la CI.
- **X4** : `HostRulesTests` couvre Api, Web, Worker et DemoData : matrice des références Lettra de chaque hôte, ni EF
  ni fournisseur de base, endpoints sans infrastructure (deux écarts connus, X18, listés dans le test). MAUI n'est
  compilé qu'en CI : ses règles restent vérifiées à la revue.
- **X5** : nouveau port `IOpenedLocalDossier` (dossier ouvert sur le poste), implémenté par `LocalDossierRegistry` ; le
  jeu de démonstration ne référence plus le fournisseur SQLite.
- **X7** : le calendrier des huit tâches de fond (intervalle strictement positif, délai initial positif ou nul) est
  validé au démarrage de l'hôte (`ValidatePeriodicJob`, `ValidateOnStart`) : un réglage faux empêche le démarrage avec
  un message qui le nomme. Restent à valider : e-mail, OIDC et la migration du Worker (Socle).
- **X10** : `ApplicationLayerRulesTests` applique « ni EF, ni ASP.NET, ni infrastructure, ni écran » à
  `Exports.Application` et `Supervision.Application` ; les règles des cas d'usage couvrent aussi `Supervision.Application`.

### Fait dans la PR 9

- **X6** (encaissements) : recherche, filtre de montant, tri et pagination en base. Le nom du client a sa colonne
  `SearchKey` (migration `RechercheEncaissements`, remplie en SQL pour les lignes existantes) ; la recherche ne dépend
  plus de la culture du serveur et trouve « eclair » dans « Éclair ». L'historique des relances reste trié en mémoire
  après filtrage en base : son tri par défaut porte sur une date avec fuseau, que SQLite ne sait pas trier, et sa
  recherche sur l'objet ignore déjà les accents.
- **X13** (début) : `ListSearchTerm`, la règle unique (nom sans accents, code en majuscules), sert aux encaissements.
  Restent à y passer les pièces et les factures récurrentes (clé de recherche à plusieurs sources).

### Fait dans la PR 10

- **X1** (Socle, PR #166) : un Worker lancé en job de migration ne fait plus que migrer.
- **X11** : les relances et les factures récurrentes passent par `PeriodicPass` et l'horloge injectée, comme les
  autres tâches ; l'attente initiale de l'export FEC en masse aussi. La boucle « pour chaque dossier » reste écrite dans
  chaque tâche, volontairement : chacune agrège ses propres compteurs et l'une s'arrête en cours de passe (factures
  récurrentes sans compte de service) ; une aide commune demanderait trois rappels (liste en échec, dossier en échec,
  arrêt) pour une dizaine de lignes gagnées par tâche, contraire à KISS. Seule l'ouverture de la portée du dossier est
  partagée (`DossierJobScope`).


## Plan

Une PR par couche, petite, verte en CI et assignée à Leub. Chaque refactorisation commence par
des tests qui figent le comportement actuel, puis change le code sans changer le comportement.
Aucun test existant n'est retiré, désactivé ni affaibli.

| PR | Contenu | État |
|---|---|---|
| 1 | Ce document et le script `build/couverture/fusionner.py` ; API : `HttpResults` remplace les ternaires `Result` → HTTP recopiés (R1) ; défauts D1 et D3 corrigés ; parité des codes d'erreur étendue à `Supervision.Domain` (A8). | Fusionnée (PR #119) |
| 2 | Doublons transverses : calendrier légal unique `LegalCalendar` (R3), liste UE et devise uniques (R11), boucle unique des tâches de fond `PeriodicPass` sur l'horloge injectée (R10), textes des mentions légales partagés (D2), « écrire puis relire » unique dans les passerelles en processus (R2), règles d'acompte et de suppression dans les entités (A1), défauts D6 et D7, tests de parité de toutes les passerelles (68 cas, HTTP et en direct). | Fusionnée (PR #129, réparée par #134) |
| 3 | Rôles : portée et échéance d'un rôle portées par `RoleDefinition` (A3, la règle était recopiée dans l'attribution et dans le calcul des droits) ; passerelles HTTP : délai d'attente traité comme une coupure (D4) ; tests. | Fusionnée (PR #139) |
| 4 | Tests d'infrastructure : tâche de la vue cabinet et ses sources, serveur de synchronisation (rejeu après écriture concurrente), webhook de supervision, annuaire d'autorisations (83 cas). Livrés avec la PR 2. | Fusionnée avec la PR 2 |
| 5 | Listes paginées : une seule règle de page et de taille (R7) ; interface : horloge injectée (D5) ; tests. | Fusionnée (PR #145) |
| 5 bis | Recherche normalisée en un seul endroit (R12), découpage du contrôle des mentions (S3), tests. | Fusionnée (PR #146) |
| 7 | Base propre : règles de l'IDE bloquantes (using inutiles, code mort, mise en forme, simplifications), 160 fichiers corrigés, suppressions d'avertissements revues (voir « Base propre »). | Fusionnée (PR #153) |
| 7 bis | Dates du jour au calendrier légal (D8) : documents imprimables, assistant, kit contrôle fiscal, derniers écrans sur l'horloge système ; tests à minuit à Paris. | Fusionnée (PR #159) |
| 6 | Application : chargement versionné (R5), fin de validation de facture (R8), saisie d'un encaissement (R4, S1, premier `IValidator` d'Exports pour A7), tests. | Fusionnée (PR #141) |
| 8 à 16 | Audit d'architecture d'octobre 2026 : voir « Ordre de correction » ci-dessus (A5, A6 et R9 y sont repris). | En cours |

Seuil de couverture appliqué par la CI depuis la PR #136 : 88 % des lignes et 78 % des branches au
global (`fusionner.py --seuil-lignes 88 --seuil-branches 78`) ; chaque PR garde au moins le niveau
de chaque assembly.
