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
| Relances : factures en retard | Facture, client, échéance, retard, reste dû | Recherche, « À envoyer aujourd'hui seulement » | Écran |
| Relances : historique | Date, facture, client, niveau | Recherche, niveau, résultat, canal, période | Serveur |

Les autres listes (clients, fournisseurs, catalogue, journaux et grand livre, encaissements et rapprochement,
déclarations, Vue cabinet, Équipe et accès, alertes) suivent dans une seconde livraison, avec les mêmes briques.

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
  (`BillingSortDto`), ou une énumération privée de l'écran quand il trie lui-même.
- `SortOption<TColumn>(colonne, libellé, premier sens)` : `SortDirectionDto.Descending` pour les dates et montants.
- Tri à l'écran : `ListSorting.Sort(lignes, _sort, clés)` avec un dictionnaire colonne → valeur ; textes comparés sans
  casse ni accents, valeurs absentes en fin de liste, ordre d'origine pour les égalités.
- `SortSelect` est masqué à partir de 768 px (`PhoneOnly="false"` pour le garder partout, sur une liste sans tableau).
- Styles : `wwwroot/lettra-lists.css`, uniquement des jetons du système de design.
- Libellés : module `Localization/Modules/TrisEtFiltres(.en).resx` (clés `Lists.*`).

## Tests

- `Lettra.Application.Tests/BillingListValidationTests` : colonnes, sens et bornes refusés.
- `Lettra.Persistence.Tests` (`Listes_de_pieces_triees_et_bornees_par_montant`) : chaque colonne dans les deux sens et
  les bornes, sur SQLite, PostgreSQL et SQL Server.
- `Lettra.Api.Tests/GatewayParityListTests` : mêmes résultats et même refus par HTTP et en direct.
- `Lettra.UI.Tests/Lists` : état du tri, tri à l'écran, en-têtes, liste déroulante, fourchettes, barre de filtres, et
  les critères envoyés par les écrans Factures et Devis.
- Vérifié dans la démo Docker à 375, 768 et 1440 px (captures dans `captures/tris-et-filtres` du dossier du projet).
