# POC grille clavier Blazor

POC de la phase 1 du plan d'implémentation (annexe C) : la saisie experte au clavier est-elle fluide en
Blazor WebAssembly, et le même composant tient-il dans la disposition Confort et sur écran étroit ?
Approche testée : celle de l'annexe A §4.2 (un module JS par grille, .NET appelé seulement pour les commandes).

Critère de sortie (annexe C) : *1 000 lignes, Entrée/Tab/F-keys sans latence perceptible, mesurée sur un poste
standard du cabinet.*

## Résultat

**Go pour le WebAssembly, à confirmer sur un vrai poste de cabinet et en MAUI.** Mesuré le 14/10/2026 dans
Chromium (conteneur Linux sans GPU, build Release non AOT), facture de 1 000 lignes, une frappe toutes les 40 ms
(25 frappes/s, l'ordre de la répétition automatique d'une touche maintenue). Latence = de la frappe à la fin de
l'image qui affiche le résultat.

| Mesure (p95) | Processeur normal | Processeur ralenti ×4 (« poste modeste ») |
|---|---|---|
| Navigation Tab / Entrée / flèches (1 000 frappes) | **18 ms** | **38 ms** |
| Commande .NET (F9) | **36 ms** | **80 à 100 ms** |
| Mise à jour des montants après une saisie (asynchrone) | **34 ms** | **146 à 186 ms** |
| Appels .NET pendant 1 000 frappes de navigation | **0** | **0** |
| Rechargement de 10 000 lignes | 75 à 125 ms | |

Ce que le POC montre :

1. **L'approche de l'annexe A §4.2 tient.** Le module JS fait toute la navigation sans .NET (compteur à 0 sur
   1 000 frappes) ; .NET n'est appelé qu'une fois par commande ou par cellule modifiée. 100 ms étant le seuil
   usuel de perception d'un retard, la navigation reste nettement en dessous, même ralentie ×4.
2. **Le coût n'est pas .NET mais la peinture du navigateur.** Une trace Chromium montrait 60 % du temps en
   « Paint » au défilement. `contain: layout paint style` sur chaque ligne a divisé ce temps par 7 (p95 de
   navigation ×4 : 57 → 38 ms). La compilation AOT, essayée, ne change presque rien (p95 ×4 : 57 → 57 ms) pour
   un `_framework` de 30 Mo au lieu de 24 Mo : **pas d'AOT nécessaire pour la grille**.
3. **Points d'attention sur poste lent** : des tâches longues (jusqu'à 100 à 175 ms) apparaissent quand
   Virtualize rend de nouvelles lignes en défilant ; une frappe sur ~100 peut alors attendre (max observé
   ~250 ms). La mise à jour des montants peut prendre 0,15 à 0,2 s, sans bloquer la frappe (le curseur est déjà
   sur la cellule suivante). À mesurer sur un vrai poste avant J1.
4. **Sans cadence, Playwright envoie ~300 frappes/s** et les mesures explosent (p95 > 600 ms) : c'est la file
   d'attente qui est mesurée, pas la grille. Les tests imposent donc une cadence réaliste.
5. **Même composant partout** : densité Expert (24 px) et Confort (40 px) basculées à chaud par Ctrl+Shift+D,
   et disposition carte sous 720 px de large (container query), avec la même navigation clavier et la barre
   F-keys utilisable au doigt.

Écarts constatés avec l'annexe A, à reporter par le fil « Plan du projet » :

- **F2 ne passe pas par .NET** : c'est un pur changement de mode de saisie (curseur dans le texte), traité
  dans le module JS comme Échap et les flèches.
- **Entrée en fin de grille appelle .NET** (création de la ligne) : c'est le seul cas de navigation qui le fait.
- La validation de cellule ne passe pas par `@onchange` Blazor mais par le module JS (`OnCellCommitted`) :
  Blazor ne remet pas à jour un `<input>` dont la valeur rendue n'a pas changé, ce qui laissait une saisie
  refusée affichée. Le module remet lui-même le texte normalisé renvoyé par .NET.
- Les totaux sont tenus **par différence** (avant/après de la ligne) plutôt que recalculés en différé :
  recalculer 1 000 lignes coûte ~13 à 18 ms en WASM et provoquait des tâches longues sur poste lent.

Non couvert ici : **MAUI Hybrid**. Le conteneur Linux ne peut pas construire d'application MAUI Windows ;
la bibliothèque `Lettra.Poc.Grille.Ui` est prête à être hébergée dans un `BlazorWebView` (aucune dépendance
WASM), mais la mesure en MAUI reste à faire sur un poste Windows. Dans WebView2, désactiver
`AreBrowserAcceleratorKeysEnabled` pour que F3, F5, F7 ou Ctrl+F reviennent à l'application.

## Ce que contient le POC

```
poc/grille-clavier/
├── GrilleClavier.slnx
├── src/
│   ├── Lettra.Poc.Grille.Calculs/   calcul de ligne et totaux par taux (préfigure Lettra.Calculations)
│   ├── Lettra.Poc.Grille.Ui/        bibliothèque Razor partagée WASM / MAUI Hybrid
│   │   ├── Grille/DataEntryGrid     grille générique virtualisée, une ligne = un composant (ShouldRender)
│   │   ├── Grille/ChoiceListDialog  liste de choix au clavier (F4 article, F3 recherche)
│   │   ├── Clavier/ShortcutRegistry registre de raccourcis scopé (global > grille)
│   │   ├── Clavier/FunctionKeyBar   barre F1…F12 cliquable (souris, tactile)
│   │   └── wwwroot/grid-keyboard.js module clavier d'une grille + mesures de latence
│   └── Lettra.Poc.Grille.Wasm/      page de démonstration : facture de 1 000 lignes
└── tests/
    ├── Lettra.Poc.Grille.Tests/     xUnit + bUnit (registre, calculs, grille)
    └── e2e/                         Playwright : navigation, commandes, dispositions, performance
```

### Clavier

| Touche | Effet | Appel .NET |
|---|---|---|
| Entrée / Tab | Cellule saisissable suivante ; sur la dernière cellule, nouvelle ligne et curseur sur Article | non (oui en fin de grille) |
| Shift+Entrée / Shift+Tab | Cellule précédente | non |
| ↑ ↓ / Page↑ Page↓ / Ctrl+Début Ctrl+Fin | Même colonne, défilement des lignes virtualisées compris | non |
| ← → | Colonne voisine ; en mode édition (F2), curseur dans le texte | non |
| F2 | Mode édition (comme un tableur) | non |
| Échap | Annule la saisie de la cellule | non |
| F3, F4, F9, F10, Insert, Ctrl+Suppr | Rechercher, liste de choix, recalculer, valider la ligne, insérer, supprimer | oui, une fois |
| F1, Ctrl+Shift+D | Aide, bascule de densité Expert / Confort | oui, une fois |

Une cellule modifiée est validée par .NET **une fois, à la sortie de la cellule** (pas à chaque frappe) ;
.NET renvoie le texte normalisé (`1 234,50`) ou refuse la valeur (l'ancienne revient, la cellule clignote).
Après une saisie, seule la ligne modifiée est re-rendue, et les totaux sont tenus à jour par différence
(O(1) par saisie) puis affichés en différé.

## Lancer

Prérequis : SDK .NET 10, Node 20+, Chromium pour Playwright.

```bash
dotnet test tests/Lettra.Poc.Grille.Tests          # 19 tests xUnit + bUnit
dotnet run --project src/Lettra.Poc.Grille.Wasm    # démo sur http://localhost:5xxx
tests/e2e/run-e2e.sh                               # publie en Release puis lance Playwright
```

Variables des tests de bout en bout : `LETTRA_CPU_THROTTLE` (défaut `1,4`), `LETTRA_CADENCE_MS` (défaut 40),
`LETTRA_PUBLISH_DIR` (pour mesurer une publication AOT : `dotnet publish -p:RunAOTCompilation=true`,
workload `wasm-tools`), `CHROMIUM_PATH`.

La page affiche en continu, sous la grille, les latences mesurées (p50, p95, max par type de frappe) et le nombre
d'appels .NET : c'est l'outil à utiliser pour la mesure sur un vrai poste de cabinet.
