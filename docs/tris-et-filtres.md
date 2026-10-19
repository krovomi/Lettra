# Trier et filtrer les listes

Les listes de Lettra se trient en cliquant sur l'en-tête d'une colonne et se filtrent depuis la barre au-dessus du
tableau. Les briques sont dans `Lettra.UI.Shared/Components/Lists` : elles sont donc les mêmes dans le navigateur (PWA)
et dans l'application MAUI, sur tous les appareils.

## Ce que voit l'utilisateur

- **Trier** : un clic sur un en-tête trie la colonne, un second clic inverse le sens. La flèche de l'en-tête montre la
  colonne triée et son sens ; les autres colonnes triables portent une double flèche discrète. Les dates et les
  montants se trient d'abord du plus récent ou du plus grand ; les textes de A à Z. Les valeurs absentes (brouillon
  sans numéro ni date) restent toujours en fin de liste.
- **Filtrer** : recherche, puis les filtres propres à la liste (nature, statut, période, fourchette de montant TTC,
  bornes comprises). Chaque filtre s'applique dès qu'on quitte le champ. **Effacer les filtres** apparaît dès qu'un
  filtre est posé ; une liste filtrée vide propose le même bouton.
- **Ordinateur et tablette** (768 px et plus) : tous les filtres sont visibles sur la barre ; un tableau plus large que
  l'écran défile seul, la barre reste en place.
- **Téléphone** (moins de 768 px) : les lignes deviennent des cartes, sans en-têtes. La recherche reste visible ; le
  bouton **Filtres** (avec le nombre de filtres posés) déplie les autres filtres et une liste **Trier par** qui propose
  chaque colonne dans les deux sens. Les champs font 44 px de haut.
- **Accessibilité** : chaque en-tête triable est un bouton (Tab, Entrée) et porte `aria-sort` ; le bouton Filtres
  porte `aria-expanded` ; les champs des fourchettes ont chacun leur nom (« Date d'émission, à partir du »).

## Écrans couverts

| Écran | Tri | Filtres | Où |
|---|---|---|---|
| Factures, avoirs, factures d'acompte | Numéro, client, date, échéance, statut, total HT, total TTC | Recherche, nature, statut, date d'émission, montant TTC | Serveur |
| Devis | Numéro, client, date, fin de validité, statut, total HT, total TTC | Recherche, statut, date d'émission, montant TTC | Serveur |
| Factures récurrentes | Nom, client, prochaine facture, total HT | Recherche, statut (actif, en pause, à facturer) | Serveur |
| Clients | Nom, ville | Recherche, nature (entreprise ou particulier) | Serveur |
| Fournisseurs | Nom, ville | Recherche | Serveur |
| Catalogue | Référence, désignation, prix HT | Recherche, nature (bien ou service), archivés | Serveur |
| Encaissements | Numéro, date, client, montant | Recherche, période, montant | Serveur |
| Rapprochement bancaire | Opération, date, montant (ordre du relevé par défaut) | Recherche (libellé, tiers, client proposé), période, montant, à traiter ou toutes | Écran |
| Alertes | Titre, gravité, portée, état, dernière occurrence, occurrences (gravité puis date par défaut) | Recherche, gravité, état, tout le cabinet, closes | Écran |
| Vue cabinet | Dossier, chaque indicateur (urgence par défaut) | Recherche, à traiter seulement | Écran |
| Équipe et accès | Personnes : nom, arrivée, accès ; équipes : nom, membres, dossiers | Recherche, filtres existants | Écran |
| Balance, soldes clients | Client, pièces, débit, crédit, solde (plus gros solde par défaut) | Recherche | Écran |
| Déclaration européenne de services | Client, ligne, numéro de TVA, montant (ordre du fichier par défaut) | Recherche (client, numéro de TVA) | Écran |
| Calendrier fiscal | Toujours par date (c'est un calendrier) | Échéance, état | Écran |

Sur les rapprochements, « Tout sélectionner » et « Valider la sélection » ne portent que sur les lignes que les filtres
laissent voir : une ligne masquée n'est jamais validée sans avoir été vue.

Restent volontairement dans leur ordre :

- **Journal des ventes et grand livre** : ordre chronologique, parce que le solde progressif se lit ligne après ligne ;
  ils se filtrent déjà par période, client et compte.
- **Constats du contrôle FEC** : ordre des lignes du fichier, pour retrouver chaque ligne signalée.
- **File de synchronisation et conflits** : ordre d'envoi des changements, qui est celui dans lequel le serveur les
  rejoue.
- **Grille de saisie du catalogue** (`catalog/grid`) : grille d'édition au clavier, dans l'ordre des références ; la
  fiche Catalogue (`catalog`) trie et filtre.

## Tri et filtres sur le serveur

Une liste paginée ou qui grandit (pièces, encaissements) trie et filtre sur le serveur ; une liste chargée en entier
(quelques dizaines de lignes) trie à l'écran avec `ListSorting`.

`GET /api/v1/invoices` et `GET /api/v1/quotes` acceptent, en plus de `search`, `customerId`, `from`, `to`, `status`,
`page` et `pageSize` :

| Paramètre | Valeurs | Par défaut |
|---|---|---|
| `sort` | `IssueDate`, `Number`, `Customer`, `DueDate` (échéance d'une facture, fin de validité d'un devis), `Status`, `TotalExclTax`, `TotalInclTax` | `IssueDate` |
| `direction` | `Ascending`, `Descending` | `Descending` |
| `minAmount`, `maxAmount` | montant TTC en euros, point décimal, bornes comprises | aucun |

Une colonne ou un sens inconnu, ou `minAmount` supérieur à `maxAmount`, renvoie un 400 `Validation.Invalid` sur le
champ (`Sort`, `Direction`, `MaxAmount`). Le numéro puis l'identifiant départagent toujours les égalités, d'où un ordre
stable d'une page à l'autre. Les trois bases (SQLite, PostgreSQL, SQL Server) rendent le même ordre : les montants sont
comparés en réel (SQLite ne trie pas les decimal) et les valeurs absentes sont placées en fin de liste explicitement.

Côté application, les critères passent par `ListInvoicesQuery` / `ListQuotesQuery` (champs `Sort`, `Direction`,
`MinAmount`, `MaxAmount`) jusqu'à `BillingListFilter` et `BillingQueries.Sort` dans la persistance. L'UI les envoie par
`BillingListQuery`, sur les deux transports (HTTP et en direct pour MAUI hors ligne).

Les autres listes paginées suivent le même schéma :

| Route | `sort` (défaut en premier) | `direction` par défaut | Autres paramètres |
|---|---|---|---|
| `GET /api/v1/customers` | `Name`, `City` | `Ascending` | `kind` = `Company` ou `Individual` |
| `GET /api/v1/suppliers` | `Name`, `City` | `Ascending` | |
| `GET /api/v1/catalog/items` | `Reference`, `Designation`, `UnitPrice` | `Ascending` | `kind` = `Good` ou `Service` |
| `GET /api/v1/payments` | `Date`, `Number`, `Customer`, `Amount` | `Descending` | `minAmount`, `maxAmount` (bornes comprises) |

Une valeur inconnue renvoie un 400 `Validation.Invalid` sur le champ (`Sort`, `Direction`, `Kind`, `MaxAmount`). Les
égalités se départagent toujours (nom puis identifiant pour les tiers, référence pour les articles, numéro pour les
encaissements). La désignation d'un article se trie sur sa forme sans accents ni casse et le prix en réel, d'où le
même ordre sur SQLite, PostgreSQL et SQL Server. Les règles communes de validation sont dans
`Lettra.Application/Features/ListSortRules.cs` ; le tri des stores dans `Repositories/ListOrdering.cs`.

Côté UI : `PartyListQuery` (clients, fournisseurs), `CatalogListQuery` (catalogue) et `PaymentListQuery`
(encaissements) portent les mêmes critères sur les deux transports. Dans les écrans en deux volets (clients,
fournisseurs, catalogue), le tri est une liste **Trier par** sous la recherche, visible sur tous les appareils.

## Utiliser les briques dans un nouvel écran

```razor
<ListFilterBar Id="items-filters" ActiveCount="ActiveFilters" OnReset="ResetFiltersAsync">
    <ChildContent>   @* toujours visible : la recherche *@
        <div class="lt-filter"><label for="items-search">…</label><input id="items-search" type="search" … /></div>
    </ChildContent>
    <More>           @* replié derrière « Filtres » sur téléphone *@
        <DateRangeFilter Id="items-dates" Label="@L["Lists.Filter.IssueDate"]" @bind-From="_from" @bind-To="_to" OnChange="LoadAsync" />
        <AmountRangeFilter Id="items-amount" Label="@L["Lists.Filter.AmountInclTax"]" @bind-Min="_min" @bind-Max="_max" OnChange="LoadAsync" />
        <SortSelect Id="items-sort" Options="_sortOptions" State="_sort" OnSort="LoadAsync" />
    </More>
    <Actions><a class="lettra-button" href="items/new">…</a></Actions>
</ListFilterBar>

<div class="lt-list-scroll">
    <table class="lettra-table lt-cards">
        <thead><tr>
            <SortHeader Option="Column(ItemSort.Name)" State="_sort" OnSort="LoadAsync" Id="items-sort-name" />
            …
```

- `SortState<TColumn>` garde la colonne et le sens ; `TColumn` est l'énumération du contrat quand le serveur trie
  (`BillingSortDto`, `PartySortDto`, `CatalogSortDto`, `PaymentSortDto`), une énumération privée de l'écran quand il
  trie lui-même, ou une chaîne quand les colonnes viennent des données (indicateurs de la Vue cabinet).
- Ordre d'origine qui n'est pas une colonne (urgence, gravité, ordre du relevé) : une valeur sans clé de tri comme
  colonne initiale, et `DefaultLabel` sur `SortSelect` pour la proposer en tête de liste et y revenir.
- Recherche à l'écran : `ListSorting.Matches(texte, recherche)`, sans casse ni accents.
- `SortOption<TColumn>(colonne, libellé, premier sens)` : `SortDirectionDto.Descending` pour les dates et montants.
- Tri à l'écran : `ListSorting.Sort(lignes, _sort, clés)` avec un dictionnaire colonne → valeur ; textes comparés sans
  casse ni accents, valeurs absentes en fin de liste, ordre d'origine pour les égalités.
- `SortSelect` est masqué à partir de 768 px (`PhoneOnly="false"` pour le garder partout, sur une liste sans tableau).
- Styles : `wwwroot/lettra-lists.css`, uniquement des jetons du système de design.
- Libellés : module `Localization/Modules/TrisEtFiltres(.en).resx` (clés `Lists.*`).

## Tests

- `Lettra.Application.Tests` : `BillingListValidationTests`, `PartyListTests`, `CatalogListTests` (colonnes, sens,
  natures et bornes refusés ; critères transmis au stockage).
- `Lettra.Persistence.Tests` (`Listes_de_pieces_triees_et_bornees_par_montant`,
  `Listes_de_tiers_et_d_articles_triees_et_filtrees_par_nature`) : chaque colonne dans les deux sens, les bornes et les
  natures, sur SQLite, PostgreSQL et SQL Server.
- `Lettra.Api.Tests/GatewayParityListTests` : pièces, tiers, articles et encaissements, mêmes résultats et même refus
  par HTTP et en direct.
- `Lettra.UI.Tests/Lists` : état du tri, tri à l'écran, en-têtes, liste déroulante, fourchettes, barre de filtres ;
  `ClientSideListTests` (alertes, Vue cabinet, Équipe et accès, balance, DES, calendrier, rapprochement) et
  `ServerSideListTests` (clients, fournisseurs, catalogue, encaissements) ; les critères envoyés par Factures et Devis.
- Vérifié dans la démo Docker à 375, 768 et 1440 px (captures dans `captures/tris-et-filtres` du dossier du projet).
