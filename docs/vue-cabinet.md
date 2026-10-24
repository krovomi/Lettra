# Vue cabinet : tous les dossiers d'un coup d'œil

Un collaborateur suit plusieurs dizaines de dossiers. Jusqu'ici Lettra ouvrait un seul dossier, fixé par la
configuration, sans moyen d'en changer. La vague 6 ajoute trois choses :

1. **Le sélecteur de dossier**, dans la barre du haut : le dossier ouvert, et un panneau pour en changer (recherche sans
   accents au-delà de 5 dossiers, favoris avec l'étoile, récents, lien vers la vue cabinet). La palette (Ctrl+K) propose
   aussi « Ouvrir le dossier … » et « Vue cabinet ».
2. **La vue cabinet** (menu « Vue cabinet », adresse `/cabinet`) : quatre chiffres clés en haut (dossiers à traiter,
   puis les trois premiers compteurs, avec une icône orange dès qu'il y a quelque chose à faire), puis la liste des
   dossiers que l'utilisateur peut ouvrir, les plus urgents d'abord. Chaque dossier a une pastille à ses initiales et
   un badge de niveau (« Urgent », « À traiter », « À surveiller », « À jour ») dans la couleur du niveau, l'heure du
   dernier calcul, et ses compteurs (factures en retard, rejets de la plateforme, alertes ouvertes, revue du mois, et
   ceux des autres modules comme « TVA à déclarer » et « Banque non rapprochée »). Un compteur à zéro reste discret ;
   seuls ceux qui demandent une action sont en couleur. « Ouvrir » et « Revue » changent de dossier en un clic. La
   recherche, « Seulement ceux à traiter » et « Effacer les filtres » utilisent la barre de filtres commune des listes
   ([tris-et-filtres.md](tris-et-filtres.md)).
   Le bouton « Tableaux de bord » en haut de la page ouvre l'onglet « Mes dossiers » des tableaux de bord
   (`/pilotage?vue=dossiers`, voir [tableaux-de-bord.md](tableaux-de-bord.md)) : les mêmes dossiers en graphiques.
3. **La revue de dossier** (menu Comptabilité › « Revue du dossier », adresse `/review`) : une liste de contrôle par mois
   ou par trimestre, cochée point par point, avec qui a coché et quand, et une note facultative.

Chaque action (changer de dossier, actualiser, cocher, décocher, enregistrer une note) est confirmée dans le toaster.

## Sur chaque appareil

La liste des dossiers choisit sa forme selon la place qu'elle a (requête de conteneur sur son panneau, pas sur la
largeur de l'écran) : un tableau à partir de 60rem de large, des cartes en dessous. Elle s'adapte ainsi aussi au menu
latéral déplié ou replié.

| Taille | Sélecteur | Vue cabinet | Revue |
|---|---|---|---|
| Grand écran (≥ 1280 px environ) | bouton avec le nom du dossier, panneau sous le bouton | tableau trié par en-têtes cliquables, un liseré de la couleur du niveau à gauche de chaque ligne, « Ouvrir » et l'icône de revue côte à côte | liste, note en ligne |
| Petit écran d'ordinateur et tablette (768 à 1279 px environ) | bouton raccourci | cartes, deux par ligne quand la place le permet ; tri par liste déroulante ; chiffres clés deux par ligne sur tablette | liste |
| Téléphone (< 768 px) | icône dossier (le logo lui laisse la place), panneau sur toute la largeur sous la barre | une carte par dossier, chiffres clés deux par ligne, tri replié derrière « Filtres » ; boutons de 44 px | liste à une colonne, cibles de 44 px |

En carte, seuls les compteurs non nuls sont affichés (avec leur échéance à côté) ; un dossier sans rien à faire
affiche « Tout est à jour ». Les boutons restent alignés en bas des cartes voisines.

Vérifié dans la démo Docker à 1440, 1280, 1200, 1024, 768 et 375 px (aucun débordement horizontal ; dans la console,
seulement les deux 401 déjà connus avant la connexion), y compris après une mise à jour d'une démo existante
(`up --build`) : les deux dossiers de démonstration supplémentaires s'ajoutent sans effacer les données.

Selon l'installation :

- **Navigateur et PWA** : changer de dossier est retenu sur l'appareil ; au lancement suivant, le même dossier se rouvre
  s'il est toujours accessible. Favoris et récents sont rangés dans le compte et suivent l'utilisateur d'un
  appareil à l'autre (sur l'appareil seulement sans compte, voir [preferences-utilisateur.md](preferences-utilisateur.md)).
- **Application installée reliée au serveur du cabinet** (Windows, macOS, Android, iOS) : même sélecteur, le dossier
  choisi est retenu sur l'appareil comme celui de la connexion.
- **Poste isolé (mode 1)** : la vue cabinet et la revue marchent sur les dossiers du poste ; le sélecteur propose
  aussi d'en changer, parmi les dossiers du poste (câblage de `MauiProgram.cs`, voir plus bas).

## Les droits

- La vue cabinet ne montre que les dossiers que l'utilisateur peut ouvrir (`DossierLire`), comme `GET /api/v1/dossiers`.
- La revue se lit avec `DossierLire` et se coche avec `FactureSaisir` (collaborateur, manager) : le client du portail
  et l'auditeur la lisent sans la modifier.

## Pourquoi la page reste rapide

La vue ne parcourt pas les bases des dossiers à l'ouverture. Les compteurs sont calculés à l'avance et rangés dans la
base Catalog (table `DossierIndicators`, une ligne par dossier et par compteur) :

- **peu après le démarrage, puis toutes les 24 h**, par la tâche `CabinetOverviewJob` (2 min après le démarrage par
  défaut, 15 s dans la démo) ;
- **à la demande**, par le bouton « Actualiser » (`POST /api/v1/dossiers/overview/refresh`) ;
- **au premier affichage**, si aucun dossier n'a encore été calculé et qu'il y en a 30 au plus (installation neuve,
  démo, poste isolé).

Ce ne sont que des compteurs, une échéance et une urgence : aucun montant ni nom de client ne sort de la base du dossier.

## Ajouter un compteur (TVA, banque, autres modules)

Un module ajoute une colonne sans migration ni changement d'écran :

```csharp
internal sealed class VatToDeclareIndicatorSource(...) : IDossierIndicatorSource
{
    public async Task<IReadOnlyList<DossierIndicator>> ComputeAsync(DateOnly today, CancellationToken cancellationToken) =>
        [new DossierIndicator(DossierIndicatorKeys.VatToDeclare, count, IndicatorLevel.Warning, DueOn: nextDeadline)];
}

services.AddScoped<IDossierIndicatorSource, VatToDeclareIndicatorSource>();
```

- La source s'exécute dans la portée du dossier (sa base est le dossier courant). Une source en échec n'empêche pas les
  autres.
- Les clés `tva.a-declarer` (`VatToDeclare`) et `banque.non-rapprochee` (`BankUnreconciled`) sont déjà réservées dans
  `DossierIndicatorKeys`, avec leur libellé et leur place dans le tableau ; une clé inconnue s'affiche à la fin, sous son
  nom.
- `IndicatorLevel` décide de la couleur et de l'ordre des dossiers : `Ok`, `Info`, `Warning` (« à traiter »),
  `Critical` (« urgent »).

Compteurs livrés : factures en retard (critique après 30 jours ; une facture soldée par des avoirs ou des
encaissements n'y compte plus), rejets de la plateforme agréée, alertes ouvertes (critique pour S1 et S2), points de la
revue du mois écoulé non cochés (« à surveiller » jusqu'au 15, puis « à traiter »), TVA à déclarer (`tva.a-declarer` :
CA3, CA12 et acomptes de TVA en retard ou dus dans les 7 jours, pas encore marqués déposés ; « urgent » dès qu'une
échéance est passée), opérations du relevé bancaire à rapprocher (`banque.non-rapprochee` : « à traiter » quand la plus
ancienne a plus de 15 jours, « urgent » au-delà de 30).

## La liste de contrôle de la revue

Pièces validées et émises · transmissions à la plateforme suivies · retards relancés · rapprochement bancaire fait · TVA
déclarée · pièces manquantes demandées · période clôturée et chaîne d'intégrité vérifiée · points notés pour le manager.

Les coches sont rangées dans la base du dossier (table `ReviewMarks`, clé période + point) : elles suivent le dossier
et survivent aux mises à jour. Une clé de point n'est jamais renommée.

## API

| Méthode | Adresse | Droit | Rôle |
|---|---|---|---|
| GET | `/api/v1/dossiers/overview` | authentifié, filtré par `DossierLire` | tableau des dossiers |
| POST | `/api/v1/dossiers/overview/refresh[?dossier=]` | idem | recalcul immédiat |
| GET | `/api/v1/review/{période}` | `DossierLire` (en-tête `X-Dossier`) | revue de la période (`2026-09` ou `2026-T3`) |
| PUT | `/api/v1/review/{période}/items/{point}` | `FactureSaisir` | cocher, décocher, noter |

## Réglages

```json
"Lettra": {
  "CabinetOverview": { "Enabled": true, "Interval": "1.00:00:00", "InitialDelay": "00:02:00" }
}
```

La démo Docker, sans Worker, fait tourner la passe dans l'API dès 15 s après le démarrage
(`Lettra__CabinetOverview__InitialDelay: "00:00:15"`) : sur une démo neuve, la vue cabinet, les « Tableaux de bord » et
Grafana ont leurs compteurs dès la première minute, sans attendre les 2 minutes du réglage par défaut ni un clic sur
« Actualiser ».

La démo Docker crée deux dossiers fictifs de plus (« Bâti Rénov' Lemoine », « Négoce Méditerranée Distribution ») avec
`Lettra__Demo__AdditionalDossiers: "2"`, pour que le sélecteur et la vue cabinet aient de quoi montrer. Le jeu ne touche
jamais aux dossiers qui existent déjà.

## Câblage (fichiers du socle)

Fait à la PR #113 :

- `AddCabinetOverviewJob(...)` dans `Lettra.Worker/Program.cs`, et dans `Lettra.Api/Program.cs` pour les installations
  sans Worker (démo), comme la tâche de l'outbox. Sans elle, la vue se calcule au premier affichage et avec
  « Actualiser ».
- `deploy/demo/compose.yaml` : `Lettra__Demo__AdditionalDossiers: "2"`.
- `MauiProgram.cs` (poste isolé) : la sélection du poste (`ActiveDossierSelection`) implémente
  `ISelectableDossierSelection` (`registry.SetActiveAsync(...)`), si bien que le sélecteur y change aussi de dossier.
