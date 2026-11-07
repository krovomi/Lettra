# Plateforme : arrivée et départ des cabinets

Dans la cellule en ligne (mode 5), l'**administrateur de la plateforme** (une personne de l'éditeur) inscrit les
cabinets et organise leur départ, sans script ni redémarrage. Il ne voit jamais les données d'un cabinet.

## L'écran « Plateforme »

- **Entrée du menu** « Plateforme » (icône nuage), section « Pilotage », visible seulement de l'administrateur de la
  plateforme (droit « Administrer la plateforme »). Mots de la recherche du menu : plateforme, cabinets, inscrire,
  arrivée, départ, cellule, saas, hébergement.
- **Sans dossier**, l'administrateur de la plateforme arrive directement sur cet écran à l'ouverture de Lettra, au
  lieu d'un accueil de dossier qu'il ne peut pas ouvrir.
- **Compteurs** : cabinets actifs, départs en cours, dossiers actifs, créations en échec.
- **Liste des cabinets** : nom (et sa cellule quand il y en a plusieurs), état (Actif, Départ demandé avec sa date de
  suppression, Parti avec sa date), dossiers actifs et à effacer, créations en échec, date d'arrivée. Recherche, filtre
  d'état et tri (nom, état, dossiers, arrivée), à l'écran. Sur téléphone, chaque cabinet est une carte.
- **Inscrire un cabinet** : nom du cabinet, nom et e-mail de son administrateur, cellule s'il y en a plusieurs. Le
  toaster dit que l'administrateur a reçu l'e-mail pour choisir son mot de passe, ou que l'e-mail n'est pas parti.
- **Départ** : demandé dans la ligne (confirmation dans la ligne, comme les autres suppressions), puis annulable depuis
  le toaster ou par le bouton « Annuler le départ » de la ligne pendant 30 jours. Un cabinet déclaré dans la
  configuration du serveur ne part pas d'ici : la ligne le dit.
- **Relancer les créations en échec** : bouton de l'en-tête, seulement s'il y en a.
- Encart « Comment ça marche » : les trois règles (inscription rejouable, départ annulable 30 jours, seuls restent
  l'identifiant, le nom et les dates du cabinet).
- Français et anglais ; ordinateur, tablette et téléphone vérifiés dans un vrai navigateur contre une cellule locale
  (sans défilement horizontal à 390 px).

## L'arrivée d'un cabinet

Lettra crée l'organisation du cabinet dans Keycloak, l'inscrit au catalogue de la cellule, fait tirer sa clé par le
KMS (voir [Clés et chiffrement des dossiers](cles-et-chiffrement.md)), crée le compte de son administrateur (qui
choisit son mot de passe et son second facteur à la première connexion) et lui donne le rôle « administrateur du
cabinet ». L'inscription est rejouable : une seconde demande retrouve le même cabinet. L'invitation part par le
serveur d'e-mail du realm ; sans lui (essais), le compte est créé quand même et la réponse dit « invitation non
envoyée ».

## Le départ d'un cabinet

Demandé par la plateforme, il reste annulable pendant 30 jours : le cabinet travaille et exporte ses données pendant
ce temps, mais ne peut plus créer de dossier. À l'échéance, la passe d'effacement (Worker, ou API sans Worker)
supprime tout : bases des dossiers, clé racine dans le KMS, organisation et comptes dans Keycloak, attributions,
préférences et réglages au registre. Le catalogue ne garde que l'identifiant, le nom et les dates du cabinet. Un
**cabinet déclaré** dans la configuration du serveur ne part pas depuis l'API.

## Routes

Toutes réservées à l'administrateur de la plateforme (droit « Administrer la plateforme », portée Plateforme) :

| Route | Effet |
|---|---|
| `GET /api/v1/plateforme/cabinets` | Les cabinets de la cellule : état (actif, en partance, parti), nombre de dossiers actifs, en attente d'effacement ou en échec, dates. Jamais une donnée d'un dossier. |
| `POST /api/v1/plateforme/cabinets` | Arrivée d'un cabinet : nom, nom et e-mail de son administrateur, cellule (facultative s'il n'y en a qu'une). 201. Rejouable. 409 `CabinetLifecycle.NameTaken` si le nom technique est déjà pris par un autre cabinet, `CabinetLifecycle.Unavailable` sans KMS ou sans compte de service. |
| `POST /api/v1/plateforme/cabinets/{cabinet}/depart` | Demande le départ : 202, avec la date d'échéance. 409 `CabinetLifecycle.CabinetDeclared` pour un cabinet déclaré dans la configuration. |
| `DELETE /api/v1/plateforme/cabinets/{cabinet}/depart` | Annule le départ tant que l'échéance n'est pas passée : 200. 409 `CabinetLifecycle.DepartureNotCancellable` ensuite. |
| `POST /api/v1/plateforme/dossiers/relance` | Relance tout de suite les créations de dossiers en échec (le Worker le fait aussi à chaque passe). |

Pendant le délai de départ, un cabinet ne peut plus créer de dossier (409 `CabinetLifecycle.CabinetLeaving`).

## Réglages

| Réglage | Défaut | Rôle |
|---|---|---|
| `Lettra:Identity:Provisioning:ClientId` | (vide) | Client du compte de service de la plateforme dans Keycloak (`lettra-provisioning`). Vide : pas d'inscription de cabinet (la plateforme répond « inscription indisponible »). |
| `Lettra:Identity:Provisioning:ClientSecret` | (vide) | Son secret, dans le coffre de la cellule (secrets `lettra-api` et `lettra-worker`), jamais dans Git. |
| `Lettra:Identity:Provisioning:Authority` | `Lettra:Identity:Oidc:Authority` | Adresse du realm vue du serveur, si elle diffère de celle des jetons. |
| `Lettra:Identity:Provisioning:InvitationLifetime` | `7.00:00:00` | Durée de validité du lien d'invitation envoyé à l'administrateur du cabinet. |
| `Lettra:Catalog:MaxDossiersPerCabinet` | `500` | Nombre de dossiers qu'un cabinet peut tenir dans la cellule (créés ou importés, effacements en attente compris, dossiers effacés exclus). Au-delà, « Nouveau dossier » et « Importer un dossier » répondent : « Le cabinet a atteint le nombre de dossiers prévu pour lui. Effacez un dossier qu'il ne suit plus, ou contactez le support pour en ouvrir davantage. » |
| `Lettra:Catalog:CabinetDepartureDelay` | `30.00:00:00` | Délai entre la demande de départ d'un cabinet et la suppression de tout ce qui le concerne. |

L'installation du compte de service dans Keycloak est décrite dans
[deploy/keycloak/README.md](../deploy/keycloak/README.md#ajouter-un-cabinet).

## Limites par cabinet

Pour qu'un cabinet ne gêne pas les autres dans une cellule partagée : 500 dossiers au plus (réglage ci-dessus), débit
de l'API borné (2 000 requêtes par minute par cabinet, 300 par utilisateur), taille des envois bornée route par route.
Un cabinet ne voit jamais rien d'un autre : un test appelle chaque route de l'API au nom d'un autre cabinet et exige la
même réponse que pour un identifiant inconnu.

## RGPD

- Fin de contrat : tout ce qui concerne le cabinet est supprimé après un délai de 30 jours, pendant lequel il garde
  l'accès pour exporter ses données (portabilité) ; ses sauvegardes deviennent illisibles avec sa clé détruite.
- Comptes des personnes : supprimés de Keycloak et du registre (attributions, préférences, réglages de l'assistant).
- Trace gardée : identifiant, nom du cabinet et dates (demande, échéance, départ), plus l'identifiant de qui l'a
  demandé : la preuve de la suppression, sans donnée personnelle des collaborateurs ni donnée des dossiers.
- Séparation des rôles : l'administrateur de la plateforme gère le cycle de vie des cabinets et ne voit jamais leurs
  données ; le compte de service n'a que des droits d'administration des comptes du realm `lettra`.

## Vérifié

Sur SQLite, PostgreSQL, SQL Server et un vrai Keycloak 26 par les tests ; de bout en bout par l'essai du mode 5 dans
kind (`deploy/tests/kind/essai-mode5.sh`, job `mode5-kind`), qui inscrit le cabinet par l'API et finit par son
départ.

## Ce qui n'est pas encore fait

- Un serveur d'e-mail réel pour les invitations dans une vraie cellule (envoi d'e-mails réels).
- L'export automatique de tous les dossiers d'un cabinet à son départ : pendant le délai de 30 jours, son
  administrateur exporte chaque dossier depuis « Dossiers du cabinet » ([réversibilité](reversibilite.md)).
- Abonnements et facturation des cabinets.
- Pour un utilisateur qui n'a aucun dossier (l'administrateur de la plateforme), la barre du haut demande encore
  l'état de la dictée et de l'assistant du dossier, et reçoit un refus : deux erreurs dans la console du navigateur,
  sans effet à l'écran. Il faudra que la barre du haut ne pose pas ces questions sans dossier ouvert.
