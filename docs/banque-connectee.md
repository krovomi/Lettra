# Banque connectée

**But.** Rapprocher sans fichier : les opérations du compte arrivent d'elles-mêmes au rapprochement bancaire, où
Lettra propose les factures correspondantes comme pour un relevé importé.

## Parcours

1. Dans **Rapprochement bancaire**, section « Banque connectée », le cabinet choisit la banque ; Lettra crée une
   connexion « en attente ».
2. L'agrégateur renvoie la page de la banque où donner l'accord (lecture seule, 180 jours, révocable).
3. La banque renvoie vers `bank/connected?connection=…&code=…`.
4. Lettra enregistre l'accord (compte masqué, échéance) et lit 90 jours d'opérations.

## Synchronisation

- Le compte est relu à l'ouverture du rapprochement si la dernière lecture date de plus de 6 heures, ou à la demande
  (**Synchroniser**).
- Elle part de la dernière lecture moins 7 jours (une banque comptabilise parfois en retard), sans remonter avant 90
  jours.
- Les doublons sont écartés par l'empreinte de chaque opération. L'agrégateur doit fournir une référence bancaire
  stable.
- Une à la fois : la synchronisation est d'abord réservée par une écriture versionnée de la connexion. Une seconde,
  concurrente, est refusée (`SyncInProgress`, ou un conflit de version), même sur plusieurs serveurs. Une réservation
  abandonnée expire au bout de 2 minutes.

## Échéance

L'accord dure 180 jours. Il est à renouveler dans ses 15 derniers jours (bouton **Renouveler l'accord**) ; le
renouvellement crée une nouvelle connexion et déconnecte l'ancienne. Une fois échu, le compte ne se relit plus.
**Déconnecter** retire l'accès ; les opérations déjà lues restent.

## Données

- Lettra garde la banque, la référence de l'agrégateur et le compte masqué (`FR76 •••• 1234`), jamais l'IBAN en entier
  ni les identifiants bancaires.
- L'IBAN des payeurs suit la règle des relevés : masqué, plus une empreinte à clé du dossier.
- Les virements reçus ne deviennent des encaissements qu'après validation humaine.

## Droits

Lecture : `DossierLire`. Relier, synchroniser, renouveler, déconnecter : `FactureEmettre`.

## Agrégateur

- Port `IBankAggregator` (`Lettra.Exports.Application`) : banques, début et fin du consentement, lecture, retrait.
- `Lettra:BankConnections:Aggregator` vaut `Simulated` (par défaut) ou `None` (import de fichiers seul). Les charts
  Helm posent `None` tant qu'aucun agrégateur réel n'est choisi.
- L'agrégateur simulé (`SimulatedBankAggregator`) propose trois banques fictives, dont « Banque Lumière (simulée) »,
  et une page qui joue la banque (**Autoriser** ou **Refuser**). Il fabrique des virements à partir des factures dues
  (environ deux sur trois) et des frais mensuels. Aucune banque réelle n'est contactée.
- Un agrégateur réel (Bridge, Powens, GoCardless Bank Account Data…) est un adaptateur de plus ; sa page de
  consentement est sur son site. C'est un prestataire de services d'information sur les comptes, enregistré auprès de
  l'ACPR, et un sous-traitant au sens du RGPD, à inscrire au registre le jour où il est choisi.

## Limites connues

- Agrégateur simulé.
- Pas de lecture en tâche de fond : à l'ouverture de l'écran ou à la demande.
- Un seul compte par consentement.
- Pas de solde affiché.
- Le retour de la banque réelle vers une application installée n'est pas traité.
