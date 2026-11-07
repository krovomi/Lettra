# Réversibilité d'un dossier

Un dossier quitte un hôte Lettra et arrive sur un autre dans un **paquet de réversibilité** : un fichier
`.lettrapaquet`, chiffré par une phrase secrète choisie par l'administrateur du cabinet au moment de l'export.

## Qui peut le faire

Seul l'administrateur du cabinet (permission « Administrer le cabinet »), pour les dossiers de son cabinet. Un
collaborateur, un manager, l'administrateur d'un autre cabinet ou un compte suspendu reçoivent un refus.

## À l'écran

Sur l'écran **Dossiers du cabinet** (menu Pilotage, voir [Clés et chiffrement des dossiers](cles-et-chiffrement.md#effacer-un-dossier)) :

- **Exporter** (bouton de chaque dossier ouvert) : un encart demande la phrase secrète, puis de la retaper (une faute
  de frappe rendrait le paquet illisible pour toujours), avec le rappel « Lettra ne la garde pas : notez-la en lieu
  sûr, à part du fichier ». Le navigateur enregistre ensuite le fichier `<nom-du-dossier>-AAAAMMJJ.lettrapaquet`, et
  le toaster donne son nom.
- **Importer un dossier** (bouton en haut de l'écran, à côté de « Nouveau dossier ») : choisir le fichier du paquet,
  taper sa phrase secrète. Une phrase fausse ou un fichier abîmé est dit sous le formulaire ; un dossier déjà présent
  (ou déjà effacé ici) est refusé avec un message clair. Réussi, le dossier apparaît dans la liste et dans le
  sélecteur de dossier, et le toaster propose « Ouvrir ».
- Un dossier dont l'effacement est demandé n'a pas de bouton « Exporter » : annuler l'effacement d'abord.
- L'encart « Comment ça marche » explique l'export, l'import et la portabilité RGPD.
- Sur le poste isolé (application de bureau), les mêmes boutons exportent et importent les dossiers du poste.
- Français et anglais ; ordinateur, tablette et téléphone vérifiés dans un vrai navigateur contre une cellule locale
  (PostgreSQL derrière PgBouncer, Keycloak, OpenBao) : export, réimport refusé tant que le dossier est là, dossier
  retiré de la cellule, phrase fausse refusée, import réussi sous l'identifiant d'origine, ouverture depuis le
  toaster, sans défilement horizontal à 390 px.

## Ce que contient le paquet

Une fois déchiffré, le paquet est une archive zip ordinaire :

| Fichier | Contenu |
|---|---|
| `dossier.db` | Toute la base du dossier au format SQLite, **en clair** : un format ouvert, lisible sans Lettra (DB Browser for SQLite, Python, Excel via ODBC). |
| `manifeste.json` | Identifiant et nom du dossier, date de l'export, dernière migration du schéma, nombre de lignes par table. |
| `cles-integrite.json` | Les clés de la chaîne anti-fraude du dossier (toutes leurs versions), pour la vérifier après import. |
| `LISEZMOI.txt` | Ce qu'est le paquet et comment le lire. |

Les champs que Lettra chiffre en base (IBAN, notes) sont déchiffrés à l'export et rechiffrés à l'import avec la clé
du dossier sur l'hôte d'arrivée : le paquet ne dépend d'aucune clé de l'hôte de départ.

## Le chiffrement du paquet

- La phrase secrète fait au moins 12 caractères. Elle n'est jamais enregistrée : la perdre, c'est perdre le paquet.
- La clé est dérivée de la phrase par PBKDF2-SHA256 (600 000 itérations, sel aléatoire de 16 octets), comme le
  recommande l'OWASP ; un en-tête qui demande moins de 100 000 itérations est refusé.
- Le contenu est chiffré par AES-256-GCM, par morceaux de 1 Mio. Chaque morceau est authentifié avec l'en-tête du
  paquet, son numéro et l'indication « dernier morceau » : un paquet tronqué, prolongé, aux morceaux déplacés ou
  modifiés d'un seul bit ne s'ouvre pas.
- Deux exports du même dossier donnent deux fichiers différents (sel et préfixe de nonce tirés à chaque fois).
- Une phrase fausse et un paquet altéré donnent le même message : « Phrase secrète incorrecte ou paquet altéré. »

## L'import

- Le paquet est déchiffré et entièrement vérifié **avant** de créer quoi que ce soit.
- Le dossier garde son identifiant d'origine (le début de sa chaîne anti-fraude en dépend). Si l'hôte connaît déjà
  cet identifiant, même pour un dossier effacé, l'import est refusé (« Ce dossier existe déjà ici »).
- Un paquet produit par une version de Lettra plus récente que l'hôte est refusé avec un message clair ; un paquet
  plus ancien est mis à niveau à l'import.
- Les clés de la chaîne anti-fraude sont réenveloppées par la clé du cabinet d'arrivée. La chaîne se vérifie sans
  écart après import, et les nouvelles écritures la continuent.
- Si l'import échoue en cours de route, le dossier à moitié créé est retiré.
- Le dossier importé s'ouvre aux personnes qui ont accès à tout le cabinet d'arrivée.

## Où cela marche

| Mode | Export | Import |
|---|---|---|
| 1. Poste isolé | oui | oui |
| 2. Serveur du cabinet | oui | oui |
| 3. Serveur distant du cabinet | oui | oui |
| 4. Poste synchronisé | non (le serveur en ligne fait foi) | non |
| 5. En ligne | oui | oui |

Testé sur SQLite, PostgreSQL et SQL Server, dans les deux sens.

## Limites connues

- Pendant l'export et l'import, la base en clair et l'archive passent quelques instants dans le dossier temporaire
  de l'hôte, puis sont effacées.
- Un dossier dont l'effacement est demandé ne s'exporte pas : annuler l'effacement d'abord, exporter, puis le
  redemander.
- Les dossiers créés avant le versionnage des clés de la chaîne anti-fraude partagent une clé dérivée de celle du
  cabinet : le paquet emporte cette clé, qui sert donc aussi aux autres anciens dossiers du même cabinet.
- Pendant le remplissage d'un dossier importé, il apparaît déjà dans la liste des dossiers.
- Taille maximale d'un paquet importé : 512 Mio. La route d'import admet 513 Mio par requête (le paquet plus
  l'enveloppe du formulaire : délimiteurs, phrase secrète). Une passerelle placée devant l'API doit accepter 513 Mio de
  corps sur cette route et lui laisser plusieurs minutes avant de couper la requête. Le profil HTTPS de la démo (Caddy)
  ne borne pas la taille des requêtes.
- Dans le navigateur, le paquet choisi est envoyé par le navigateur lui-même, depuis le fichier sur le disque, sans
  être copié dans la mémoire de l'application ; sur le poste (application de bureau), le fichier part en flux.
  Vérifié dans Chromium contre une cellule locale : un dossier de 493 Mio exporté en 43 secondes, refusé à l'import
  tant qu'il est là, puis importé une fois retiré de la cellule, avec toutes ses données.

## Routes

Toutes réservées à l'administrateur du cabinet (« Administrer le cabinet ») :

| Route | Effet |
|---|---|
| `POST /api/v1/cabinets/{cabinet}/dossiers/{dossier}/export` | Corps JSON `{ "passphrase": "…" }` ; rend le paquet en pièce jointe (`<nom-du-dossier>-AAAAMMJJ.lettrapaquet`). 400 `Dossier.PassphraseTooShort`, 404 pour un dossier inconnu ou d'un autre cabinet. |
| `POST /api/v1/cabinets/{cabinet}/dossiers/import` | Formulaire multipart : le fichier `paquet` et le champ `phrase`. 201 avec le dossier créé ; 400 `Dossier.PackageUnreadable` (phrase fausse, paquet altéré ou pas un paquet), 409 `Dossier.AlreadyExists`, 409 `Dossier.PackageTooNew`. |

Un hôte dont la composition n'active pas le transfert (le poste synchronisé, mode 4) répond 409
`Dossier.TransferUnavailable`.

## RGPD

- **Portabilité** (art. 20) : le cabinet récupère tout le dossier dans un format ouvert (SQLite), lisible sans Lettra,
  et peut le reprendre sur son propre serveur.
- **Sécurité du paquet** (art. 32) : chiffré de bout en bout par une phrase que Lettra ne garde pas ; seul
  l'administrateur du cabinet peut exporter ; l'export et l'import sont journalisés (sans la phrase ni les données).
- **Réversibilité** promise au cabinet en sortie du contrat en ligne : une sortie sans dépendance à l'hôte de départ.
