# Devis à relancer et carnet de commandes

Un devis envoyé qui reste sans réponse se perd souvent par simple oubli : il expire sans que personne ait rappelé le client. Lettra montre les devis dont la fin de validité approche, prépare la relance, et garde la trace de chaque relance. Le **carnet de commandes pondéré** estime en plus le chiffre d'affaires que les devis en attente devraient rapporter, d'après les taux d'acceptation passés du dossier. Ce sont des **règles fixes et une statistique simple**, écrites et testées : aucune IA ne choisit, n'estime ni n'écrit.

**Où** : menu **Ventes**, sous **Devis** : **Devis à relancer** (`/quote-follow-ups`) et **Carnet de commandes** (`/order-book`). Les trois écrans des devis ont les mêmes onglets en haut (Tous les devis, À relancer avec leur nombre, Carnet de commandes). L'**Accueil** a un panneau **Carnet de commandes** ; l'écran d'un devis envoyé a un bouton **Relancer** et l'historique de ses relances. « Aller à… » et Ctrl+K trouvent les deux écrans aussi par « relance », « sans réponse », « fin de validité », « prévision », « chiffre d'affaires à venir », « taux d'acceptation », « pipeline ».

## Les devis à relancer

Un devis est **en attente** quand il est envoyé (validé), sans réponse du client (ni accepté, ni refusé) et encore valable. Il est **à relancer** quand, en plus :

- sa fin de validité tombe dans la **fenêtre de relance** : aujourd'hui ou dans les N jours qui viennent (cinq par défaut) ;
- et il n'a pas déjà été relancé depuis qu'il est entré dans cette fenêtre (une relance faite plus tôt ne compte pas ; une relance dont l'envoi a échoué non plus).

La liste montre chaque devis de la fenêtre : numéro, client, jours restants, montant HT, probabilité d'acceptation (celle du carnet de commandes), nombre de relances et date de la dernière. En haut, trois chiffres : devis à relancer, leur montant HT, et leur montant pondéré. Par défaut, seuls les devis à relancer sont montrés, les plus proches de leur fin d'abord ; quand tout est relancé, l'écran le dit (« Aucun devis à relancer pour l'instant ») et les devis déjà relancés restent visibles en décochant « À relancer seulement ».

- **Filtres** : recherche (numéro, client), « À relancer seulement » (coché par défaut), « Avec une adresse e-mail ».
- **Tri** : numéro, client, jours restants (par défaut), montant HT, probabilité.

**Dans la démo** : le jeu de démonstration a peu de devis en attente, et ceux-ci arrivent rarement à cinq jours de leur fin le jour où l'on regarde. Pour voir la liste remplie, porter la fenêtre à 30 jours (bloc **Fenêtre de relance**), ou valider un nouveau devis puis cliquer **Relancer** sur son écran. Les e-mails partent dans MailPit (http://localhost:8025).

## Relancer

**Relancer**, dans la liste ou sur l'écran d'un devis en attente (même hors de la fenêtre), ouvre la relance préparée :

- **dans la langue du client** (français par défaut, anglais pour un client anglophone) ;
- un **objet** (« Votre devis DEV-2026-0031 : valable jusqu'au 8 novembre 2026 ») et un **texte** cordial : le rappel du devis (numéro, date, objet, montant HT et TTC), sa fin de validité, une proposition d'en parler ou de l'adapter, et comment l'accepter ; signé du nom de l'entreprise ;
- l'**adresse** du client, si sa fiche en a une.

L'utilisateur relit et modifie l'objet et le texte, puis :

- **Envoyer la relance** : Lettra l'envoie par e-mail, au nom de l'entreprise, par le serveur d'envoi configuré (dans la démo, MailPit : http://localhost:8025). Le bouton n'apparaît que si le client a une adresse et qu'un serveur d'envoi est branché. Un envoi qui échoue est gardé dans l'historique « En échec », avec un message, et le devis reste à relancer.
- **Noter comme relancé** : pour une relance faite au téléphone, par courrier ou depuis sa propre messagerie ; Lettra garde la trace, sans rien envoyer.

Chaque action est confirmée dans le toaster. Rien ne part sans ce clic.

## L'historique des relances

Chaque relance est gardée telle qu'elle est partie : date, canal (e-mail ou à la main), résultat (faite ou en échec), destinataire, objet, texte, montant et fin de validité du devis à ce moment, et qui l'a faite. Rien n'est modifié ni supprimé.

- Sur l'écran **Devis à relancer** : tout l'historique du dossier. Filtres : recherche (numéro, client, objet), canal, résultat, période. Tri : date (par défaut, la plus récente d'abord), devis, client, montant. Pagination côté serveur.
- Sur l'écran d'un devis : ses propres relances (date, canal, résultat, objet), dès qu'il y en a une.

## Régler la fenêtre de relance

Dans **Devis à relancer**, bloc **Réglage** : le nombre de jours avant la fin de validité à partir duquel un devis est à relancer, de 1 à 30 (5 par défaut). Un réglage par dossier, modifiable par un manager (les autres le voient sans pouvoir le changer). Si un collègue l'a modifié entre-temps, l'écran le dit et relit la valeur.

## Le carnet de commandes pondéré

Le montant des devis en attente surestime ce qui sera facturé : tous ne seront pas acceptés. Le carnet de commandes pondère chaque devis en attente par sa **probabilité d'acceptation**, calculée sur l'historique du dossier :

1. **Les devis pris en compte** : ceux émis dans les 24 derniers mois et **conclus** : acceptés (puis facturés ou non), refusés, ou arrivés à la fin de leur validité sans réponse (comptés comme non acceptés).
2. **Trois taux d'acceptation** pour chaque devis en attente :
   - le **taux du client** : la part acceptée de ses devis conclus ;
   - le **taux de la tranche de montant** : la part acceptée des devis conclus de la même tranche (moins de 1 000 €, de 1 000 à 5 000 €, de 5 000 à 20 000 €, 20 000 € et plus, HT) ;
   - le **taux de l'ancienneté** : parmi les devis restés sans réponse au moins aussi longtemps que celui-ci (première semaine, deuxième semaine, jusqu'à 30 jours, au-delà), la part acceptée ensuite. Plus un devis attend, moins il a de chances, et ce taux le dit.
3. **Peu de devis** : un taux qui repose sur moins de 3 devis conclus est remplacé par le **taux global** du dossier (et l'écran le dit : « trop peu de devis : taux global »). Tant que le dossier a moins de 3 devis conclus, le taux global vaut une chance sur deux.
4. **La probabilité** du devis est la moyenne des trois taux ; son **montant pondéré** est son montant multiplié par cette probabilité, arrondi au centime. Le carnet pondéré est la somme des montants pondérés, en HT et en TTC.

L'écran **Carnet de commandes** montre :

- en haut : le montant des devis en attente, le montant pondéré HT et TTC, le taux global (« acceptés : x devis conclus sur y ») ;
- un encadré quand l'historique est trop court ;
- **Comment ce montant est calculé** (repliable) : la règle en clair, les taux par tranche de montant et par ancienneté, et la mention d'information sur le taux par client ;
- la liste des devis en attente : numéro, client, ancienneté, montant HT, probabilité, montant pondéré ; sous chaque probabilité, **Pourquoi** déplie les trois taux et leur moyenne. Filtres : recherche (numéro, client), tranche de montant, ancienneté. Tri : numéro, client, ancienneté, montant HT, probabilité, montant pondéré (par défaut, le plus grand d'abord).

Le même calcul est exposé par une requête de l'application (`GetOrderBookQuery`), que la future trésorerie reprendra telle quelle.

## Données et confidentialité

- Le taux par client est un **profilage léger** : il est calculé sur les seuls devis du dossier, n'est montré qu'aux utilisateurs du dossier, n'est jamais montré au client ni transmis, et ne sert qu'à cette estimation. L'écran le dit sous la règle.
- Les relances sont gardées dans la base du dossier (table `QuoteFollowUps`), le réglage aussi (`QuoteFollowUpSettings`). Rien ne va dans la base Catalog, Grafana ni les journaux (ni nom, ni adresse, ni montant).
- Le carnet n'est pas gardé : il est recalculé à chaque lecture.

## Droits

| Action | Droit | Rôles |
|---|---|---|
| Voir les devis à relancer, l'historique, le carnet de commandes, la relance préparée | Lire le dossier | collaborateur, collaborateur étendu, manager, auditeur |
| Envoyer ou noter une relance | Créer des devis | collaborateur, collaborateur étendu, manager |
| Régler la fenêtre de relance | Gérer le catalogue et les réglages du dossier | manager |

## Sur chaque appareil

Navigateur et PWA, application installée reliée au serveur, poste isolé : mêmes règles, mêmes écrans. Du grand écran au téléphone : les chiffres passent sur deux colonnes puis une, les tableaux défilent dans leur cadre, la relance s'écrit dans un champ pleine largeur. Dates et montants suivent la langue de l'écran.

## API

- `GET /api/v1/quotes/follow-ups` : les devis de la fenêtre, le réglage, les chiffres du haut.
- `GET /api/v1/quotes/follow-ups/history` : l'historique, filtré (`quoteId`, `customerId`, `channel`, `outcome`, `from`, `to`, `search`), trié (`sort`, `descending`) et paginé.
- `PUT /api/v1/quotes/follow-ups/settings` : la fenêtre de relance (1 à 30 jours).
- `GET /api/v1/quotes/{id}/follow-up/preview` : la relance préparée.
- `POST /api/v1/quotes/{id}/follow-up` : envoyer (`channel: Email`) ou noter (`channel: Manual`) la relance, avec l'objet et le texte relus.
- `GET /api/v1/quotes/order-book` : le carnet de commandes pondéré et ses taux.

Refus attendus, avec un message : devis introuvable, devis plus en attente (accepté, refusé ou expiré entre-temps), fiche entreprise à compléter, client sans adresse e-mail, aucun serveur d'envoi, objet ou texte vide ou trop long, fenêtre hors de 1 à 30 jours.

## Architecture

- Domaine (facturation) : `QuoteFollowUp` (une relance, jamais modifiée), `QuoteFollowUpSettings` (la fenêtre), `QuoteFollowUpPolicy` (la règle « à relancer »), `OrderBookCalculator` (la statistique, pure, sans dépendance).
- Application : les ports `IQuotePipelineReader` (les devis et leurs clients, en lecture), `IQuoteFollowUpRepository`, `IQuoteFollowUpSettingsRepository` ; le service `QuoteFollowUpService` ; les modèles de texte FR et EN ; les cas d'usage : vue des devis à relancer, relance préparée, envoi, historique, réglage, carnet de commandes. L'envoi passe par le port `IEmailSender`.
- Infrastructure : les magasins EF Core et une migration par moteur.
- Présentation : les écrans partagés (navigateur, PWA, MAUI), les passerelles HTTP et en processus.

## Limites

- Pas d'envoi automatique : chaque relance part d'un clic.
- La liste ne propose qu'une relance par devis dans la fenêtre ; pour relancer encore (ou plus tôt), le bouton **Relancer** de l'écran du devis reste disponible tant que le devis est en attente.
- Pas encore dans « Décisions à prendre » : la règle de décision « devis à relancer » s'y branchera par un adaptateur sur la vue des devis à relancer.
- Le carnet ne tient compte ni de la saison ni de la date probable de signature : il dit combien, pas quand.
