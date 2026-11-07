# Clés et chiffrement des dossiers

Un dossier de Lettra contient des données qu'un cabinet doit protéger : coordonnées bancaires, adresses e-mail,
pièces. Sur un poste (modes 1 et 4), la base entière est chiffrée. Sur un serveur (modes 2, 3 et 5), la base est un
fichier SQLite ou une base PostgreSQL ou SQL Server que le serveur de bases sait lire : Lettra chiffre donc lui-même
les **champs sensibles**, avec une clé propre à chaque dossier.

## Trois étages de clés

| Clé | Une par | Où elle est gardée | À quoi elle sert |
|---|---|---|---|
| Clé racine | cabinet | dans le KMS de la cellule (moteur Transit d'OpenBao), jamais exportée | envelopper la clé du cabinet |
| Clé du cabinet (KEK) | cabinet | enveloppée par la clé racine, dans le registre Catalog | envelopper les clés des dossiers, les sceaux anti-fraude, les empreintes d'IBAN |
| Clé du dossier (DEK) | dossier | enveloppée par la clé du cabinet, dans le registre Catalog | chiffrer les champs sensibles du dossier |

Le serveur de bases ne voit jamais une clé en clair. L'API et le Worker ouvrent les clés en mémoire, quelques
minutes au plus, dans un cache borné qui les efface à l'éviction. Hors cellule (serveur du cabinet, démonstration),
la clé du cabinet est déclarée dans la configuration au lieu du KMS ; le reste est identique.

## Les champs chiffrés

L'IBAN du profil de l'entreprise, l'IBAN et l'e-mail de chaque fournisseur, l'e-mail de chaque client. En base, la
valeur commence par `lettra:enc:v1:` suivi du texte chiffré (AES-256-GCM). Le chiffrement est **déterministe** : deux
valeurs égales donnent le même texte chiffré, ce qui garde les recherches par égalité ; aucun tri ni recherche
partielle n'est fait sur ces champs. Chaque dossier a sa clé : la même adresse donne deux textes différents dans deux
dossiers. Une valeur écrite avant le chiffrement se relit telle quelle et sera chiffrée à sa prochaine écriture.

## Rotation et effacement

- **Rotation de la clé racine** : le KMS en crée une nouvelle version chaque année ; le Worker réenveloppe alors la
  clé de chaque cabinet. La clé du cabinet, les clés des dossiers et les données ne changent pas : rien n'est
  réécrit, les sceaux et les empreintes restent vérifiables.
- **Effacement d'un dossier** : demandé par l'administrateur du cabinet, il ferme le dossier aussitôt et reste
  annulable pendant le délai de rétractation (30 jours). Ensuite, la passe d'effacement détruit sa clé (son
  enveloppe dans le catalogue), ce qui rend ses champs chiffrés illisibles, y compris dans les sauvegardes, puis
  supprime sa base. Le catalogue garde la trace de l'effacement (nom, dates, qui l'a demandé), sans aucune donnée.
- **Effacement d'un cabinet** (son départ, voir [Plateforme](plateforme.md)) : détruire sa clé racine dans le KMS
  rend tous ses dossiers illisibles, sauvegardes de la cellule comprises. C'est irréversible.
- **KMS injoignable** : les dossiers déjà ouverts restent lisibles le temps du cache (quelques minutes), puis plus
  aucun dossier ne s'ouvre. Une panne du KMS n'est jamais prise pour une clé détruite.

## Ce qui n'est pas encore fait

- Le chiffrement des **volumes** de la cellule (option B) dépend de l'hébergeur : à confirmer avec l'offre retenue.
  Les sauvegardes vers le stockage objet sont déjà chiffrées (AES-256).
- Les **pièces jointes et exports** ne sont pas encore chiffrés par la clé du dossier.
- OpenBao en **haute disponibilité**, scellé par un HSM ou le KMS de l'hébergeur, et joint par TLS avec
  l'authentification Kubernetes (au lieu d'un jeton) : à installer avec la cellule réelle.
- L'**export automatique** de tous les dossiers d'un cabinet à son départ : chaque dossier s'exporte aujourd'hui un par
  un (voir [Réversibilité d'un dossier](reversibilite.md)).

## Effacer un dossier

L'administrateur du cabinet demande l'effacement d'un dossier : le dossier est fermé à tous aussitôt, la demande reste
annulable pendant le délai de rétractation (30 jours par défaut), puis la passe d'effacement (Worker, ou API sans
Worker) détruit la clé du dossier, supprime sa base et note le dossier « effacé » dans le catalogue. Seules les bases
créées par Lettra s'effacent ainsi : une **base déclarée** dans la configuration (serveur du cabinet, démonstration)
appartient à qui l'a déclarée, et la demande est refusée avec un message clair.

### L'écran « Dossiers du cabinet »

- **Entrée du menu** « Dossiers du cabinet » (icône dossier), section « Pilotage », visible seulement de qui
  administre le cabinet (comme « Équipe et accès »). Mots de la recherche du menu : dossiers, cabinet, créer, nouveau
  dossier, effacer, supprimer, effacement, rétractation, rgpd.
- **Compteurs** : dossiers ouverts, effacements demandés.
- **Liste des dossiers** : nom (le dossier ouvert est signalé), état (Ouvert, Effacement demandé, En création), date
  d'effacement. Recherche, filtre d'état et tri (nom, état, date d'effacement), à l'écran. Sur téléphone, chaque
  dossier est une carte.
- **Exporter** et **Importer un dossier** : un dossier part dans un paquet chiffré par une phrase secrète et arrive
  entier sur un autre hôte Lettra (voir [Réversibilité d'un dossier](reversibilite.md)).
- **Nouveau dossier** : un nom suffit (500 dossiers au plus par cabinet dans une cellule en ligne, voir
  [Plateforme](plateforme.md#limites-par-cabinet)). Le dossier reçoit sa base et sa clé, s'ouvre aussitôt aux personnes qui ont
  accès à tout le cabinet, et apparaît dans le sélecteur de dossier. Le toaster propose « Ouvrir ».
- **Effacer** : demandé dans la ligne, avec une confirmation courte (« Fermer ce dossier ? Il sera effacé dans
  30 jours, sauf annulation. »). Le dossier est fermé à tous aussitôt ; s'il était le dossier ouvert, un autre dossier
  prend sa place. Le toaster donne la date d'effacement et propose « Annuler l'effacement », tout comme le bouton de
  la ligne tant que le délai court. À l'échéance, la ligne disparaît : clé détruite, base supprimée.
- Un dossier déclaré dans la configuration du serveur (serveur du cabinet, démonstration), ou tenu sur un poste isolé,
  garde son bouton « Effacer », qui explique pourquoi il ne s'efface pas depuis Lettra.
- Encart « Comment ça marche » : une base et une clé par dossier ; effacement annulable 30 jours puis définitif,
  sauvegardes comprises ; exporter avant ce que le cabinet doit conserver (dix ans pour les pièces comptables) ; les
  bases déclarées ne s'effacent pas d'ici.
- Sur le poste isolé (application de bureau), l'écran liste et crée les dossiers du poste ; aucun ne s'y efface.
- Français et anglais ; ordinateur, tablette et téléphone vérifiés dans un vrai navigateur contre une cellule locale
  (création, ouverture depuis le toaster, effacement demandé puis annulé, effacement réel par le Worker à l'échéance,
  sans erreur dans la console ni défilement horizontal à 390 px).
- Limite connue : un dossier neuf n'a pas encore de fiche entreprise ; les « premiers pas » de l'accueil la demandent
  et reçoivent un refus attendu (une ligne dans la console du navigateur), jusqu'à ce que la fiche soit remplie.

### Les routes

Routes, toutes réservées à l'administrateur du cabinet (« Administrer le cabinet ») :

| Route | Effet |
|---|---|
| `GET /api/v1/cabinets/{cabinet}/dossiers` | Les dossiers du cabinet (hors dossiers effacés), leur état, s'ils s'effacent depuis Lettra et la date d'effacement demandée. |
| `POST /api/v1/cabinets/{cabinet}/dossiers/{dossier}/effacement` | Demande l'effacement : 202, avec la fin du délai de rétractation. 409 `Dossier.Declared` pour une base déclarée dans la configuration. |
| `DELETE /api/v1/cabinets/{cabinet}/dossiers/{dossier}/effacement` | Annule la demande tant que le délai court : 200, le dossier rouvre tel quel. 409 `Dossier.ErasureNotCancellable` ensuite. |
| `GET /api/v1/cabinets/{cabinet}/dossiers/effacements` | Les dossiers en attente d'effacement, la fin du délai la plus proche d'abord. |

**RGPD** : le cabinet exerce lui-même le droit à l'effacement d'un dossier, depuis l'écran, avec les 30 jours de
rétractation et le rappel de la conservation légale des pièces comptables avant d'effacer. Un dossier quitte Lettra pour de bon, sauvegardes comprises (sa clé détruite les rend illisibles), après un
délai qui protège d'une erreur ou d'une malveillance. Le catalogue ne garde que la trace de l'effacement
(identifiant, nom du dossier, dates, identifiant de qui l'a demandé) : la preuve de l'effacement, sans aucune donnée
du dossier. Le cabinet reste responsable de ses durées de conservation légales.

## Réglages

**Clés des cabinets (`Lettra:Kms`).** `Provider` vaut `Configuration` (par défaut : clé de chaque cabinet déclarée
dans `Lettra:Organisations:n:Kek`, serveur du cabinet, démonstration) ou `Transit` (cellule en ligne : une clé racine
par cabinet dans le moteur Transit d'OpenBao, ou de Vault). Avec `Transit` : `Transit:Address` (adresse d'OpenBao),
`Transit:Token` (jeton, toujours par variable d'environnement ou coffre, jamais dans un fichier versionné),
`Transit:Mount` (`transit`), `Transit:KeyPrefix` (`lettra-cabinet-`), `Transit:AutoRotatePeriod` (rotation
automatique de la clé racine par le KMS, `8760h` : un an) et `Transit:CacheLifetime` (durée de vie d'une clé ouverte
en mémoire, 5 min). Le Worker réenveloppe chaque jour les clés des cabinets après les rotations
(`Lettra:Worker:KeyRewrap` : `Interval` 1 jour, `InitialDelay` 5 min, `Enabled`).

| Réglage | Défaut | Rôle |
|---|---|---|
| `Lettra:Catalog:DossierErasureDelay` | `30.00:00:00` | Délai de rétractation entre la demande d'effacement et l'effacement réel. Lu par l'API au moment de la demande. |
| `Lettra:DossierErasure:Enabled` | `true` | Passe d'effacement (Worker, ou API sans Worker). |
| `Lettra:DossierErasure:Interval` | `01:00:00` | Fréquence de la passe. |
| `Lettra:DossierErasure:InitialDelay` | `00:02:00` | Attente avant la première passe. |

Dans la cellule, le chart `lettra-cell` pose `Lettra__Kms__Provider: Transit`, l'adresse d'OpenBao et le jeton lu dans
le secret `lettra-kms`.

## Dans la démo Docker

Rien ne change à l'écran. La démo garde la clé de son cabinet dans la configuration (`LETTRA_DEMO_KEK`, fichier
`deploy/demo/.env`) ; ses dossiers reçoivent une clé au démarrage et leurs champs sensibles sont chiffrés à leur
prochaine écriture. **Changer `LETTRA_DEMO_KEK` rend illisibles les IBAN et e-mails déjà chiffrés**, comme il rendait
déjà invérifiables les sceaux anti-fraude.

## Vérifié

Les tests chiffrent, relisent et effacent sur SQLite, PostgreSQL et SQL Server. L'essai de bout en bout du mode 5
dans kind (`deploy/tests/kind/essai-mode5.sh`, job `mode5-kind`) vérifie le chiffrement des champs sensibles, la
rotation et la destruction de la clé du cabinet, et l'effacement d'un dossier : demande, dossier fermé, annulation,
dossier rouvert, nouvelle demande, puis clé détruite et base supprimée par le Worker, l'autre dossier intact.
