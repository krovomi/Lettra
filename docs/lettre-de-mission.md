# Lettre de mission et honoraires

Un cabinet d'expertise comptable signe une lettre de mission avec chaque client, puis se demande en cours d'année si
le forfait couvre le temps réellement passé. Cet écran réunit les deux.

**Où** : menu Comptabilité, « Lettre de mission » (`/mission`), dans le dossier du client.

## La mission

Nature (présentation des comptes annuels, tenue de la comptabilité, examen limité), date de début, travaux compris
(tenue, déclarations de TVA, comptes annuels, liasse fiscale, paie, juridique), forfait annuel HT, rythme de
facturation (mensuel, trimestriel, annuel), taux horaire visé (facultatif), signataire pour le client, expert-comptable
responsable, date de signature. Sans mission saisie, le formulaire s'ouvre prérempli avec la mission la plus courante.

La date de signature ne s'enregistre pas tant que la fiche [anti-blanchiment](anti-blanchiment.md) du dossier n'est pas
à jour (identification et bénéficiaires effectifs complets, revue non échue). Une lettre déjà signée le reste.

## La lettre en PDF

Objet, travaux, durée et tacite reconduction, engagements du client et du cabinet, honoraires, données personnelles,
acceptation, signatures. Le texte suit la langue de l'écran et reprend la fiche de l'entreprise (nom, adresse, SIREN).
Le cabinet la relit et la complète avant de l'envoyer pour signature ; elle ne remplace pas les conditions générales du
cabinet.

## Le temps passé

Date (jamais dans le futur), durée par quarts d'heure (12 h au plus par saisie), nature du travail, note facultative.
La liste se trie et se filtre (recherche, nature) ; chacun ne supprime que ses propres saisies.

## Ce que rapporte le dossier

Sur l'année de mission en cours (de la date anniversaire du début à la veille de la suivante) :

| Indicateur | Calcul |
|---|---|
| Honoraires acquis | Forfait annuel × jours écoulés de l'année de mission ÷ jours de l'année |
| Temps passé | Somme des durées saisies sur l'année de mission, et par nature |
| Taux horaire réalisé | Honoraires acquis ÷ heures passées ; signalé quand il passe sous le taux visé |
| Budget d'heures | Forfait annuel ÷ taux visé ; signalé quand le temps passé le dépasse |

## Droits

Lire l'écran demande le droit de lire le dossier ; créer ou modifier la mission, le droit de valider les factures ;
saisir ou supprimer son temps, le droit de saisir les factures. Pas de nouvelle permission. Les modifications
concurrentes de la mission sont refusées (version lue).

## Données

Deux tables s'ajoutent à la base de chaque dossier (mission, temps passés), par une migration pour SQLite, SQL Server
et PostgreSQL. Elle ne touche aucune donnée existante. L'écran lit aussi le registre du cabinet pour le nom du cabinet
et des collaborateurs.

## Ce qui n'est pas fait

- La facturation automatique du forfait : une facture récurrente reste à créer à la main.
- La signature électronique de la lettre.
- Un dossier « cabinet » qui regrouperait les honoraires de tous les clients.

## Vérifié

Démo Docker, navigateur sur ordinateur et téléphone.
