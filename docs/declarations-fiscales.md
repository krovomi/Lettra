# Déclarations : TVA, DES et calendrier fiscal

Menu **Comptabilité › Déclarations** (`/tax`). Lettra prépare, le cabinet dépose : rien n'est télédéclaré (ni
EDI-TVA, ni impots.gouv, ni douane). Les montants sont ceux des pièces validées du dossier, sans aucun recalcul de ce
qui a été imprimé.

| Onglet | Ce qu'on y fait |
|---|---|
| **TVA** (`/tax/vat`) | Brouillon de la CA3 (réel normal, mini-réel) ou de la CA12 (réel simplifié) de la période, partie ventes. Chaque case s'ouvre sur les factures, acomptes et avoirs qui la composent ; chaque pièce ouvre sa fiche. |
| **DES** (`/tax/des`) | Déclaration européenne de services du mois : une ligne par numéro de TVA client de l'Union, montant HT arrondi à l'euro, fichier CSV à déposer soi-même. |
| **Contrôle fiscal** (`/tax/audit-kit`) | Kit contrôle fiscal : l'archive horodatée d'une période, prête à remettre au vérificateur ([kit-controle-fiscal.md](kit-controle-fiscal.md)). |
| **Calendrier fiscal** (`/tax/calendar`) | Les échéances de l'année calculées à partir du régime et des réglages ; chacune se marque « déposée ». |
| **Réglages fiscaux** (`/tax/settings`) | Date de clôture, CA3 mensuelle ou trimestrielle, jour limite de la TVA (du 15 au 24), IS, CFE. |

Le régime de TVA se choisit dans **Pilotage › Entreprise** (« Régime de déclaration de la TVA »). Sans régime,
l'onglet TVA dit quoi compléter ; en franchise en base, il n'y a pas de déclaration de TVA.

## Le brouillon de TVA

Correspondance des cases, **à faire relire par l'expert-comptable** :

| Opérations (catégorie EN 16931) | CA3 (3310-CA3-SD) |
|---|---|
| Ventes et prestations taxées (S, Z) | A1, puis ligne du taux : 08 (20 %), 9B (10 %), 09 (5,5 %) ; le 2,1 % et les autres taux ont une case à part, sans numéro, et un avertissement |
| Exportations (G) | E1 |
| Exonérées (E), autoliquidation BTP et services intracommunautaires (AE) | E2 |
| Livraisons intracommunautaires (K) | F2 |
| Hors champ (O) et débours (`VATEX-EU-79-C`) | nulle part, listés à part |
| TVA brute, déductible, crédit, net à payer | 16, 23, 25, 28 |

Les avoirs se déduisent de la période de leur date. La CA12 (3517-S-SD) reprend les mêmes regroupements, sans numéro
de ligne pour l'instant ; elle montre aussi les deux acomptes (55 % en juillet, 40 % en décembre, de la TVA de
l'exercice précédent, dispensés sous 1 000 €).

**La TVA déductible reste à 0** : les achats ne sont pas encore dans Lettra. Le brouillon le rappelle.

### Contrôles avant dépôt

| Contrôle | Gravité |
|---|---|
| La TVA brute du brouillon est celle du compte 44571 du journal sur la période | bloquant si elle diffère |
| Des brouillons de facture attendent d'être validés | avertissement |
| Des factures à un client professionnel français n'ont pas été transmises à la plateforme agréée | avertissement |
| Des prestations de services sont dues à l'encaissement (pas d'option pour les débits sur la pièce) | avertissement, tant que les encaissements ne sont pas suivis |
| Un taux sans ligne dans la CA3 | avertissement |
| TVA déductible non reprise | avertissement, toujours |

**TVA sur les encaissements.** Le brouillon part de la TVA sur les débits (date de la pièce). Pour les prestations
de services sans option pour les débits, la TVA est due à l'encaissement : le port `IVatOnReceiptsSource` est branché
sur les encaissements du dossier (`EfVatOnReceiptsSource`) ; la TVA des services d'une facture passée en 44574 n'est
déclarée qu'à la date de chaque encaissement, pour la part payée ([encaissements.md](encaissements.md)).

## La DES

Y figurent les lignes de services d'une pièce en autoliquidation intracommunautaire (AE) et, par prudence, les
services classés en K, à un client établi dans un autre État membre. Le mois retenu est celui de la date de la pièce.

- Un client de l'Union sans numéro de TVA bloque (la ligne ne peut pas être déclarée).
- Un client hors Union dans ces catégories, ou un total négatif pour un client (avoir plus fort que les factures du
  mois), avertit.
- Le fichier `DES-AAAA-MM.csv` (UTF-8, point-virgule) contient `ligne;numero_tva_client;montant_eur;periode`. Le
  gabarit d'import du service en ligne des douanes est **à confirmer** avant tout dépôt réel.

## Le calendrier fiscal

| Échéance | Date retenue |
|---|---|
| CA3 | le jour limite des réglages (15 par défaut) du mois qui suit le mois ou le trimestre |
| CA12 | 2e jour ouvré après le 1er mai (exercice civil), 3 mois après la clôture sinon |
| Acomptes de CA12 | jour limite du 7e et du 12e mois de l'exercice |
| DES | 10e jour ouvrable du mois qui suit, pour un mois qui a des prestations intracommunautaires |
| CFE | 15 décembre |
| IS | acomptes le 15 du 3e, 6e, 9e et 12e mois de l'exercice ; solde le 15 mai (exercice civil) ou le 15 du 4e mois après la clôture ; liasse comme la CA12 |

Une date qui tombe un jour non ouvré n'est pas reportée : Lettra rappelle la date la plus tôt. Les jours fériés
français (dont lundi de Pâques, Ascension, lundi de Pentecôte) sont pris en compte pour la CA12 et la DES.

États : **À venir**, **Proche** (dans les 7 jours), **En retard**, **Déposée**, **Dispensée**, **Passée**. Une
échéance n'est « en retard » qu'à partir du premier enregistrement des réglages : avant, Lettra ne sait pas ce qui a
été déposé ailleurs et la dit simplement « passée ».

**Centre d'alertes.** Une passe du Worker (toutes les 12 heures, `Lettra:Worker:FiscalCalendar` ; l'API la fait
elle-même dans une installation sans Worker, `Lettra:Outbox:Job:HostedByApi`) lève pour chaque
dossier l'alerte « Échéance fiscale proche » (S3) et « Échéance fiscale dépassée » (S2), et les résout quand
l'échéance est marquée déposée. Les alertes ne contiennent aucune donnée de client.

## Pour les développeurs

| Couche | Fichiers |
|---|---|
| Contrats | `Lettra.Contracts/Tax/TaxContracts.cs`, codes d'erreur `Tax.*` |
| Calculs (fonctions pures) | `Lettra.Exports.Application/Tax/` : `VatOperations`, `VatReturnCalculator`, `DesCalculator`, `FiscalCalendar`, `FrenchCalendar` |
| Cas d'usage | `TaxDeclarations` et `TaxQueries` (lecture `DossierLire` ; fichier DES, réglages et dépôts `FecExporter`) |
| Stockage | tables `FiscalSettings` et `FiscalFilings` de la base du dossier (migration `ReglagesFiscaux`, trois fournisseurs) ; `EfTaxStores` |
| Alertes | `FiscalDeadlineAlertJob`, branché par `AddFiscalDeadlineAlerts` dans le Worker (ou dans l'API sans Worker) |
| API | `GET /api/v1/tax/vat-return?date=`, `GET /api/v1/tax/des?year=&month=`, `GET /api/v1/tax/des/file`, `GET /api/v1/tax/calendar?from=&to=`, `GET /api/v1/tax/calendar/upcoming?days=7`, `PUT /api/v1/tax/calendar/filings`, `GET` et `PUT /api/v1/tax/settings` |
| Écrans | `Lettra.UI.Shared/Pages/Tax/`, `Components/Tax/`, `wwwroot/lettra-tax.css`, ressources `Localization/Modules/Fiscalite*.resx` ; passerelles `ITaxGateway` HTTP et in-process |

**Vue cabinet multi-dossiers.** La colonne « TVA à déclarer » (`tva.a-declarer`) vient de
`VatToDeclareIndicatorSource` : le nombre de CA3, CA12 et acomptes de TVA en retard ou dus dans les 7 jours, pas
encore marqués déposés, et la plus proche de leurs échéances. « À traiter » s'il y en a, « urgent » dès qu'une est en
retard (donc jamais avant le premier enregistrement des réglages). Un dossier sans fiche entreprise n'a pas
d'indicateur ; en franchise, il est à zéro. La DES et l'IS n'y entrent pas : ils restent au calendrier et dans le
centre d'alertes. La même liste se lit dans un dossier par `GetUpcomingFiscalDeadlinesQuery`,
`GET /api/v1/tax/calendar/upcoming?days=7` ou `ITaxGateway.GetUpcomingAsync(7)`.

Tests : `tests/Lettra.Exports.Tests/Tax/` (cases, DES, calendrier, jours fériés, indicateur de la vue cabinet), `tests/Lettra.Api.Tests/TaxApiTests.cs`
(API réelle sur SQLite : brouillon relié au journal, réglages et conflit de version, dépôts, refus),
`tests/Lettra.UI.Tests/Tax/TaxPagesTests.cs` (écrans et messages du toaster).
