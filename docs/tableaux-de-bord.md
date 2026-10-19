# Tableaux de bord dans Lettra

Les indicateurs des tableaux Grafana ([indicateurs-grafana.md](indicateurs-grafana.md)) sont aussi dans Lettra, au menu
**Tableaux de bord** (section « Pilotage » du menu, adresse `/pilotage` ; aussi dans la palette Ctrl+K), pour que chacun s'en serve sans quitter
l'application ni ouvrir un autre outil. Les mêmes chiffres, les mêmes seuils de couleur, et les droits de Lettra.

| Onglet | Qui le voit | Ce qu'il montre |
|---|---|---|
| **Ce dossier** | qui peut lire le dossier ouvert (`dossier.lire`) | délai moyen de paiement, part des créances en retard, part encaissée, évolution du chiffre d'affaires, clients actifs, poids du premier client, conversion et devis en attente ; deux graphiques sur douze mois (indice du chiffre d'affaires, factures émises) ; la liste « À faire » de la Vue cabinet, chaque ligne menant à l'écran où la traiter |
| **Mes dossiers** | tout utilisateur ; seuls les dossiers qu'il peut lire sont listés | les indicateurs clés de chaque dossier, avec recherche, tri par colonne (ou par liste déroulante sur téléphone) et « Voir » qui ouvre le dossier sur son tableau de bord |
| **Administration** | seulement qui administre au moins un dossier (`dossier.administrer`) ; l'onglet est caché pour les autres | taille de chaque base, postes actifs, révoqués et silencieux, plus long silence de synchronisation, âge du calcul de nuit |

Les couleurs suivent la règle de Grafana : vert, tout va bien ; orange, à surveiller ; rouge, à traiter. Un indicateur
coloré a aussi sa bulle d'aide en texte : la couleur n'est jamais la seule information. « Actualiser » relit les trois
onglets et le dit dans le toaster ; changer de dossier depuis « Mes dossiers » aussi.

## Droits : qui voit quoi

Grafana OSS ne sait pas filtrer un tableau selon la personne connectée : une page Grafana intégrée telle quelle (iframe)
aurait montré tous les dossiers de la cellule à tout le monde. Les tableaux de bord de Lettra sont donc des écrans
Lettra, et c'est le serveur qui filtre, dossier par dossier, avec les mêmes règles que le reste de l'application :

- **Ce dossier** passe par l'autorisation habituelle du dossier ouvert (`RequirePermission(dossier.lire)`). Un client
  du portail ou un administrateur sans droit de lecture reçoit un refus ; l'onglet le dit sans bloquer les autres.
- **Mes dossiers** et **Administration** lisent la dernière collecte du serveur (la même que Prometheus), qui couvre
  toute la cellule, puis n'en gardent que les dossiers que `AccessibleDossiers` ouvre à l'utilisateur pour la
  permission demandée. Un dossier d'un autre cabinet, ou d'un groupe auquel il n'a pas accès, n'est jamais envoyé.
- Un compte suspendu ne reçoit aucun dossier.
- Aucun montant, aucun nom de client : des ratios, des délais, des indices et des compteurs, comme dans Grafana.

Le réglage `Lettra:Kpi:BusinessIndicators=false` ne touche que la publication vers Prometheus : dans Lettra, chacun
garde les indicateurs de gestion de ses propres dossiers.

## Selon l'installation

| Mode | Ce dossier | Mes dossiers et Administration |
|---|---|---|
| Serveur du cabinet, SaaS, démo Docker (modes 2, 3, 5) | calculé à l'instant | dernière collecte du serveur (5 minutes au plus) |
| Poste isolé, poste relié (modes 1 et 4) | calculé à l'instant | « Pas de vue d'ensemble sur ce poste » : aucune collecte n'y tourne |

## Sur chaque appareil

| Taille | Rendu |
|---|---|
| Ordinateur (≥ 1200 px) | indicateurs en grille, les deux graphiques côte à côte, tableaux complets avec tri par en-tête |
| Tablette (768 à 1199 px) | graphiques l'un sous l'autre, tableaux qui défilent de côté dans leur cadre |
| Téléphone (< 768 px) | deux indicateurs par ligne, onglets qui défilent, une carte par dossier et tri par liste déroulante ; les valeurs au-dessus des barres sont masquées (le titre donne l'unité) |

## Code

- Contrats : `Lettra.Contracts/Pilotage/PilotageContracts.cs`.
- Cas d'usage : `Lettra.Application/Features/Kpi/Pilotage.cs` (requêtes, gestionnaires, `PilotageMapper` qui traduit
  les valeurs de collecte en lignes d'écran).
- API : `Lettra.Api/Endpoints/PilotageEndpoints.cs` — `GET /api/v1/pilotage` (dossier ouvert, en-tête `X-Dossier`),
  `GET /api/v1/dossiers/pilotage` et `GET /api/v1/dossiers/pilotage/administration`.
- Écran : `Lettra.UI.Shared/Pages/Pilotage/` (`PilotagePage`, `PilotageDisplay` pour les formats et les seuils),
  textes dans `Localization/Modules/Pilotage*.resx`, styles dans `wwwroot/lettra-pilotage.css`.
- Passerelles : `HttpPilotageGateway` (navigateur, application reliée) et `InProcessPilotageGateway` (poste isolé).
- Tests : `PilotageTests` (application), `PilotageApiTests` (droits de bout en bout), `PilotageScreenTests` (écran),
  `KpiScrapeTests` (publication Prometheus sans indicateurs de gestion).
