# Données de démonstration : le « Cabinet Démo »

Vague 5, lane A. Ce module remplit un dossier vide avec un an d'activité **fictive** et croisée, pour que les listes,
le tableau de bord, le FEC et le centre d'alertes aient de quoi montrer. Rien n'est écrit directement en base : chaque
pièce passe par les vrais cas d'usage, par les mêmes passerelles que les écrans (`AddInProcessGateways`). Droits,
validation, conformité, numérotation, chaîne anti-fraude, écritures, FEC et plateforme agréée simulée sont donc ceux de
l'application.

## Activer le jeu

Section `Lettra:Demo` :

| Réglage | Défaut | Rôle |
|---|---|---|
| `Seed` | `false` | Sans `true`, l'appel ne fait rien. |
| `UserId` | utilisateur passé par l'hôte | Au nom de qui les pièces sont créées (serveur : l'utilisateur de démonstration de Keycloak). |
| `DossierId` | dossier ouvert (poste) ou premier dossier accessible (serveur) | Dossier rempli. |
| `RandomSeed` | `2026` | Graine du tirage : même graine, même jeu. |
| `Months` | `12` | Profondeur de l'historique (2 à 36 mois). |
| `AdditionalDossiers` | `2` | Dossiers supplémentaires créés dans le cabinet (0 à 2). |

`LettraDemoData.SeedAsync` refuse en environnement `Production`, quelle que soit la configuration, et ne touche pas un
dossier qui a déjà des devis ou des factures (`DossierNotEmpty`). L'API l'appelle au démarrage (`Program.cs`, la démo
Docker tourne en environnement `Demo`) ; l'application installée l'appelle en arrière-plan après l'ouverture du compte.

## Ce qui est généré

Trois entreprises fictives, une par dossier :

| Activité | Entreprise | TVA |
|---|---|---|
| Services (dossier ouvert) | Atelier Numérique Conseil, Lyon | option pour les débits |
| Bâtiment | Bâti Rénov' Lemoine | sur les encaissements |
| Négoce | Négoce Méditerranée Distribution | à la livraison, réel normal |

Pour chacune : fiche entreprise, 30 à 50 clients avec leurs articles habituels (popularité de Zipf), 10 à 15
fournisseurs, catalogue (deux articles archivés), deux grilles tarifaires (−10 % et −15 %), devis à tous les stades,
acomptes et soldes, abonnements mensuels, ventes au comptoir (négoce), transmissions à la plateforme simulée, avoirs
partiels ou totaux, encaissements à l'heure, en retard, en deux fois ou jamais. Les dates rejouent l'année écoulée en
jours ouvrés et heures de bureau (horloge `DemoClock`, propre au chargement). Quelques alertes s'ajoutent au centre
d'alertes : deux sur le dossier (une résolue), une sur le cabinet.

Identifiants fictifs mais valides : SIREN commençant par 9 (clé de Luhn), numéro de TVA intracommunautaire calculé,
IBAN avec un code banque 99xxx.

## Dossiers supplémentaires

Ils sont créés par `CreateDossierCommand`, au nom de l'utilisateur de démonstration :

- **Sur le poste (mode 1)**, son propriétaire administre le cabinet du registre local (`ILocalCabinet`), il crée donc
  les dossiers « bâtiment » et « négoce » ; le dossier ouvert reste celui de l'utilisateur.
- **Sur le serveur de démonstration**, l'utilisateur est manager et ne crée pas de dossier : le jeu remplit le dossier
  configuré et le résultat porte une note `Security.Forbidden` (compose : `AdditionalDossiers=0`).

## Ce qui est vérifié et où

`tests/Lettra.Api.Tests/DemoDataTests.cs` : tirage reproductible et dans le passé, identifiants acceptés par le
domaine, données croisées, chargement des trois dossiers sur le poste (statuts relus par les écrans, FEC valide,
chaîne intacte, mention « débits » sur les avoirs, alertes du dossier et du cabinet, second appel sans effet),
chargement côté serveur, refus en production. Durée : environ 25 s pour le dossier principal, 50 s pour les trois.
