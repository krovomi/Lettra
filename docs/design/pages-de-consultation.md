# Pages de consultation

Une page par entité, sur un seul écran : la **liste** (avec recherche) et la **fiche**. On consulte en lecture, et on
passe en saisie dans la même fiche, sans changer d'écran. Pages concernées : **Clients**, **Fournisseurs**,
**Catalogue** (articles et prestations), et la comptabilité : **Journal des ventes**, **Balance**, **Grand livre**.

## Le passage entre consultation et saisie

| Je veux… | Je fais | L'adresse devient |
|---|---|---|
| Parcourir | Liste ; ↑ ↓ (Origine, Fin) déplacent la sélection, la fiche suit | `/customers/{id}` |
| Chercher | **F3** ; la liste se filtre pendant la frappe (pause de 250 ms) | inchangée |
| Créer | **Nouveau client** (**Inser**) ; ou, si la recherche ne trouve rien, **Créer « Dupont »** (le nom est repris) | `/customers/new?name=Dupont` |
| Modifier | **Modifier** (**F2**, ou **Entrée** sur la ligne sélectionnée) | `/customers/{id}/edit` |
| Revenir | **Annuler** (**Échap**) : de la saisie à la lecture, de la fiche à la liste | `/customers/{id}` |

- Chaque état a son adresse : un lien vers une fiche s'ouvre directement, le bouton retour du navigateur ou du
  téléphone fait ce qu'on attend.
- Une saisie modifiée n'est jamais perdue sans le dire : **Annuler**, **Échap**, un lien du menu ou une autre ligne de
  la liste demandent « Vos modifications ne sont pas enregistrées. Les abandonner ? ». Pas de boîte de dialogue du
  navigateur, donc même comportement dans l'application installée.
- Une fiche modifiée par quelqu'un d'autre pendant la saisie (refus 412, ADR-0013) : « Garder mes valeurs et
  enregistrer » ou « Reprendre ses valeurs ».
- Après **Créer** ou **Enregistrer**, la fiche revient en lecture, à jour, et la liste est relue.
- L'ancienne grille du catalogue reste disponible (**Saisie en grille**, `/catalog/grid`) pour saisir beaucoup
  d'articles au clavier.

## Des fiches légères

- **Chiffres clés en bandeau** (`lt-stats`) plutôt qu'en cartes : trois valeurs, une ligne.
- **Blocs repliables** (`Collapsible` du système de design, sans cadre dans une fiche : `Card="false" Level="3"`) :
  Coordonnées, Dernières factures, Derniers devis, Coordonnées bancaires, Description, Caractéristiques. Le titre est un
  bouton (`aria-expanded`), l'état replié est retenu sur l'appareil d'une fiche à l'autre. Les blocs secondaires
  (derniers devis, caractéristiques d'un article) sont repliés au départ.
- **Confirmations dans le toaster** : « Client « Dupont » créé », « … enregistré », un article archivé ; les erreurs
  sans champ (droits, serveur injoignable) aussi. Les erreurs d'un champ restent sous le champ.
- **Saisie** : les champs en deux colonnes à partir de 640 px, une seule en dessous ; la barre **Enregistrer /
  Annuler** reste collée en bas de la fiche (au-dessus des onglets sur téléphone).

## Par appareil

| Largeur | Ce qu'on voit |
|---|---|
| ≥ 1024 px (ordinateur, tablette paysage) | Liste à gauche (fixe pendant que la fiche défile), fiche à droite. Sans fiche ouverte, un rappel des raccourcis. |
| < 1024 px (tablette portrait, téléphone) | La liste **ou** la fiche, selon l'adresse ; « Retour à la liste » en haut de la fiche. |
| < 768 px (téléphone) | Boutons de la fiche sur toute la largeur, sans rappel de touches ; tableaux comptables défilant dans leur cadre. |
| Écran tactile | Cibles de 44 px, pas de rappel de raccourcis clavier. |

Aucun défilement horizontal de la page à 375 px. Vérification : `build/design-captures.cjs` photographie les écrans de
la galerie (dont `client`, `client-saisie`, `article`, `journal`, `balance`, `grand-livre`) à 375, 768 et 1440 px
dans les quatre thèmes et passe l'audit axe.

## Comptabilité

Lecture seule, calculée à partir des écritures enregistrées à la validation des pièces (les mêmes que le FEC) :

- **Journal des ventes** (`/accounting/journal`) : écritures de la période, de la plus récente à la plus ancienne,
  recherche par n° de pièce ou client (sans accents ni casse), filtre par compte. Chaque écriture se déplie pour
  montrer ses lignes ; totaux débit et crédit avec le badge « Équilibré ».
- **Balance** (`/accounting/balance`) : chiffre d'affaires HT (706 et 707, avoirs déduits), TVA collectée (44571),
  facturé net aux clients (411) ; balance des comptes ; balance auxiliaire des clients.
- **Grand livre** (`/accounting/accounts/{compte}`, `?customer=` pour un seul client) : solde d'ouverture, lignes avec
  solde cumulé, solde de clôture. La fiche client y renvoie (« Compte client »).
- La période (ce mois, mois dernier, ce trimestre, cette année, année dernière, ou dates libres) est dans l'adresse et
  suit d'un onglet à l'autre. Une consultation couvre cinq ans au plus.
- Les encaissements ne sont pas encore comptabilisés dans Lettra : le solde 411 est ce qui a été facturé, avoirs
  déduits. L'écran le dit.

API : `GET /api/v1/accounting/journal`, `/accounting/balance`, `/accounting/accounts/{account}/ledger` (permission
`DossierLire`), cas d'usage dans `Lettra.Exports.Application/Accounting`.

## Pour le développeur

- Une page de consultation hérite de `RecordExplorerPage<TSummary, TRecord, TForm>`
  (`Lettra.UI.Shared/Components/Explorer`) : mode déduit de l'adresse, recherche, liste paginée, clavier, garde de
  saisie, enregistrement avec version, message de réussite dans le toaster (`ToastService`). La page fournit le rendu
  (`ExplorerLayout`, `EditBar`, `Fact`, et le `Collapsible` de `Components/Feedback`) et les appels à sa passerelle.
- Styles : `wwwroot/lettra-explorer.css`, jetons seulement.
- Textes : `Localization/Modules/Consultation.resx` et `Comptabilite.resx` (+ `.en.resx`).
- Tests : `tests/Lettra.UI.Tests/CustomersPageTests.cs`, `SuppliersPageTests.cs`, `Consultation/`,
  `tests/Lettra.Exports.Tests/Accounting/`, `tests/Lettra.Api.Tests/AccountingApiTests.cs`.
