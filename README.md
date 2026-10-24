# Lettra

Logiciel de devis et de factures pour les cabinets d'experts-comptables : une base par dossier client, la facture
électronique (Factur-X, UBL), la transmission à une plateforme agréée et le travail hors ligne.

> **Prototype interne.** Lettra se présente aujourd'hui en interne, sur **données fictives uniquement**. La plateforme
> agréée est **simulée** : rien ne quitte l'ordinateur. Ne saisissez aucune donnée réelle de client.

- [Choisir sa façon d'essayer](#choisir-sa-façon-dessayer) : Navigateur, Windows, Android, iPhone, Mac
- [Premier usage, pas à pas](#premier-usage-pas-à-pas)
- [Ce que la démo ne fait pas encore](#ce-que-la-démo-ne-fait-pas-encore)
- [En cas de problème](#en-cas-de-problème)
- [Pour les développeurs](#pour-les-développeurs)

---

## Choisir sa façon d'essayer

Lettra est une seule application, en deux formes :

- **dans le navigateur**, installable comme une application (PWA), sur n'importe quel système : un poste lance le
  serveur de démonstration (Docker), les autres appareils s'y connectent ;
- **installée sur l'appareil** (application native), qui fonctionne seule, sans serveur ni Internet : Windows,
  Android, iOS et macOS.

« **Vérifié** » signifie que la CI le fait à chaque modification de `main` : construire, lancer et contrôler le
résultat. « **Non vérifié** » signifie que la méthode est décrite mais que personne ne l'a encore faite de bout en bout.

| Appareil | Comment | État |
|---|---|---|
| Ordinateur Windows, macOS ou Linux | [Navigateur](#dans-le-navigateur-windows-macos-linux) (Chrome, Edge, Firefox), avec Docker | **Vérifié** dans Chromium : connexion avec second facteur, application, rechargement hors ligne. Firefox et Safari non vérifiés. |
| Le même ordinateur, en application installée | [PWA](#installer-lapplication-depuis-le-navigateur-pwa) depuis Chrome ou Edge | Manifeste et service worker **vérifiés** ; le clic « Installer » lui-même non vérifié. |
| Téléphone ou tablette Android, iPhone, iPad | [Navigateur sur le même Wi-Fi](#téléphone-ou-tablette-sur-le-même-wi-fi-navigateur), en HTTPS | Profil HTTPS **vérifié** dans Chromium avec une adresse autre que localhost ; **non vérifié sur un vrai téléphone**. |
| PC Windows 10 ou 11 | [Application Windows](#application-windows) | **Vérifiée** : lancement, cabinet et premier dossier chiffré créés ; le parcours pas à pas est rejoué par les tests. |
| Émulateur Android (Android Studio) | [Application Android](#android-émulateur-ou-téléphone) | **Vérifiée** sur un émulateur Android 14 (API 34) : lancement sans plantage, premier dossier chiffré créé. |
| Téléphone Android (7.0 ou plus récent) | Le même APK, [installé à la main](#sur-un-téléphone-android) | **Non vérifié** sur un vrai téléphone. |
| Simulateur iOS (Mac avec puce Apple et Xcode) | [Application iOS pour le simulateur](#simulateur-ios-mac) | **Vérifiée** : lancement, premier dossier chiffré créé, application toujours ouverte 10 s plus tard. |
| iPhone ou iPad réel | Application native | **Pas encore possible** : il faut un Mac et un compte Apple Developer pour la signer. Utiliser le navigateur. |
| Mac (macOS) | [Application macOS](#macos-mac-catalyst) | Construite, mais **s'arrête à la création du premier dossier** tant qu'elle n'est pas signée par Apple. Utiliser le navigateur. |
| Linux, en application native | Pas de version native (.NET MAUI ne vise pas Linux) | Utiliser le navigateur. |
| Tous les appareils sur les mêmes données | [Serveur commun](#serveur-commun) : **Pilotage** > **Serveur du cabinet**, adresse `http://localhost:8080` (Android par câble : `adb reverse tcp:8080 tcp:8080` et `adb reverse tcp:8081 tcp:8081`) | Navigateur, harnais de l'application installée et émulateur Android **vérifiés** de bout en bout ; Windows, vrai téléphone Android et simulateur iOS **non vérifiés** ; iPhone et Mac : signature Apple nécessaire. |

Les applications natives se téléchargent depuis la CI. Sur GitHub, ouvrez l'onglet
[Actions](https://github.com/krovomi/Lettra/actions/workflows/ci.yml?query=branch%3Amain+is%3Asuccess), cliquez sur
l'exécution **CI** la plus récente de la branche `main` (coche verte ; pas « Déploiement »), puis, tout en bas de sa
page, dans la section **Artifacts**, sur l'artefact voulu. Il faut être connecté à GitHub. Les artefacts restent
disponibles 90 jours.

| Artefact | Contenu |
|---|---|
| `lettra-windows` | Application Windows, dossier à extraire (environ 160 Mo) |
| `lettra-android` | `fr.lettra.app-Signed.apk` |
| `lettra-ios-simulator` | `Lettra.App.Maui.app`, pour le simulateur iOS seulement |
| `lettra-macos` | `Lettra-macos.zip` (Mac à puce Apple, non signé) |

### Dans le navigateur (Windows, macOS, Linux)

Un ordinateur fait tourner le serveur de démonstration : Keycloak (connexion et second facteur) et Lettra. **Il faut
seulement Docker** (et Git) : Docker Desktop sous Windows et macOS, Docker Engine avec Compose sous Linux. Depuis la
racine du dépôt cloné (`git clone https://github.com/krovomi/Lettra`), la même commande dans PowerShell, bash ou zsh :

```bash
docker compose -f deploy/demo/compose.yaml up -d --build --wait     # premier démarrage : quelques minutes
```

Au tout premier démarrage, l'API remplit le dossier de démonstration avant de répondre : la page met encore une
trentaine de secondes à s'ouvrir après la commande. Les démarrages suivants sont immédiats.

1. Ouvrir **http://localhost:8080/** dans Chrome, Edge ou Firefox, puis **Se connecter**.
2. Identifiant **`demo`**, mot de passe **`demo-lettra-demo`** (comptes et secrets fictifs, réservés à la démonstration).
3. À la première connexion, enregistrer le second facteur : scanner le QR code avec une application TOTP qui accepte
   SHA-256 (FreeOTP, Aegis, Google Authenticator récent, Bitwarden, 1Password ; **pas Microsoft Authenticator**), puis
   saisir le code. Keycloak redemande une fois le mot de passe et le code, c'est normal.
4. Lettra s'ouvre sur le tableau de bord du « Dossier de démonstration », déjà rempli d'un an d'activité fictive
   ([ce que contient la démo](#ce-que-vous-trouvez-en-arrivant)). Suivre ensuite le
   [pas à pas](#premier-usage-pas-à-pas) à partir de l'étape 1 : la connexion remplace la phrase secrète du poste.

`docker compose -f deploy/demo/compose.yaml down` arrête la démonstration (les données restent) ; ajouter `-v` remet
tout à zéro. Détails, ports et télémétrie : [deploy/demo/README.md](deploy/demo/README.md).

Les e-mails envoyés par la démo (relances, invitations) se lisent dans MailPit : http://localhost:8025.

> La CI rejoue ce chemin à chaque modification (job « Démo navigateur » de `deploy.yml`, Playwright dans Chromium) :
> connexion, enregistrement du second facteur, écran des clients, service worker, rechargement hors ligne et
> brouillon gardé dans le navigateur.

#### Installer l'application depuis le navigateur (PWA)

Dans Chrome ou Edge, l'icône d'installation de la barre d'adresse (ou menu ⋮, **Installer Lettra**) ouvre ensuite
Lettra dans sa propre fenêtre, avec son icône. L'application installée a besoin du serveur de démonstration ; hors
connexion, elle s'ouvre et garde les brouillons dans le navigateur, sans données de dossier en cache. Sur ordinateur,
l'installation dans Firefox est absente ou expérimentale selon la version ; Safari sur Mac n'est pas vérifié.

#### Téléphone ou tablette sur le même Wi-Fi (navigateur)

Hors de `localhost`, le navigateur exige HTTPS avec un certificat approuvé. Le profil `compose.lan.yaml` sert Lettra
en `https://<adresse-du-poste>:8443` avec une autorité de certification propre à la démonstration :

```bash
docker compose -f deploy/demo/compose.yaml -f deploy/demo/compose.lan.yaml up -d --build --wait
```

Il faut auparavant reporter l'adresse IP du poste dans `deploy/demo/.env` (`LETTRA_DEMO_HOST=192.168.1.20`), puis,
sur le téléphone, télécharger et approuver le certificat `http://<adresse>:8079/lettra-demo-ca.crt` et ouvrir
`https://<adresse>:8443/`. Ensuite : Chrome Android, menu ⋮, **Installer l'application** ; Safari iOS, **Partager**,
**Sur l'écran d'accueil**. Les réglages exacts d'Android et d'iOS sont dans
[deploy/demo/README.md](deploy/demo/README.md#depuis-un-téléphone-même-wi-fi--profil-https). Retirez le certificat du
téléphone après la démonstration.

> Non vérifié sur un vrai téléphone : la CI vérifie ce profil HTTPS dans Chromium, avec une adresse autre que
> localhost, mais pas l'installation du certificat ni la PWA sur Android ou iOS.

En HTTPS, Lettra demande au navigateur de n'y revenir qu'en HTTPS pendant un an (en-tête HSTS). Le navigateur
l'ignore pour une adresse IP, comme `192.168.1.20` ; avec un nom de poste, ce nom reste ensuite en HTTPS dans ce
navigateur. Rien ne change pour `http://localhost:8080`, qui ne reçoit jamais cet en-tête.

#### Voir la télémétrie dans Grafana

La même démonstration, avec ses traces, métriques et journaux dans Grafana (collecteur filtrant, Prometheus, Tempo et
Loki, comme dans une cellule) :

```bash
docker compose -f deploy/demo/compose.yaml -f deploy/demo/compose.observabilite.yaml up -d --build --wait
```

Puis **http://localhost:3000/**, sans connexion : six tableaux de bord dans le dossier « Lettra ». Trois pour piloter :
**Pilotage du cabinet** (tous les dossiers d'un coup d'œil : délai de paiement des clients, créances en retard, TVA à
déclarer, banque à rapprocher ; un clic sur un dossier ouvre son tableau), **Pilotage d'un dossier** (clients et
encaissements, activité mois par mois, devis, ce qui reste à faire) et **Administration** (erreurs et lenteurs,
connexions refusées, alertes graves, postes qui ne se synchronisent plus, comptes, dossiers, place occupée). Trois pour
la technique : **Activité** (requêtes, latence, erreurs, outbox, synchronisations, connexions, assistant), **Métier**
(devis, factures, avoirs, transmissions, FEC, en nombres seulement) et **Objectifs de service** (disponibilité, latence,
alertes). Les tableaux de pilotage se remplissent seuls à partir des dossiers de démonstration (une collecte par minute
dans la démo, toutes les 5 minutes par défaut ; la vue cabinet est calculée 15 s après le démarrage) ; ils ne
publient que des ratios, des délais, des indices et des compteurs, jamais un montant ni un nom de client
([Indicateurs de pilotage dans Grafana](docs/indicateurs-grafana.md)). Tableaux de pilotage **vérifiés** dans la démo
Docker à 390 px (téléphone), 820 px (tablette) et 1440 px (ordinateur) : aucun panneau vide, aucun défilement de
côté. Pour remplir les courbes techniques, dix minutes d'activité fictive avec k6, une fois la session ouverte
([commandes complètes](deploy/demo/README.md#remplir-les-courbes--activité-de-démonstration-k6)) :

```bash
LETTRA_SESSION_COOKIE="$(cat /tmp/lettra-k6/session-cookie.txt)" k6 run -e LETTRA_PROFIL=activite deploy/tests/k6/fumee.js
```

Détails : [Voir la télémétrie dans Grafana](deploy/demo/README.md#voir-la-télémétrie-dans-grafana).

Sans Grafana, les mêmes indicateurs se lisent dans Lettra, menu **Pilotage** > **Tableaux de bord** : dans la démo
Docker, les trois onglets sont remplis pour les trois dossiers ([Tableaux de bord](docs/tableaux-de-bord.md)).

> La CI (job « Démo — télémétrie dans Grafana ») démarre cette pile, vérifie par l'API de Grafana les sources et les
> tableaux de bord, puis, après deux minutes d'activité k6, que chaque panneau a des données (sauf les deux panneaux
> PostgreSQL, vides avec SQLite, et quatre panneaux d'administration qui dépendent d'erreurs, de connexions ou de postes
> installés que la démo ne garantit pas), qu'un exemplaire mène à une trace et à ses journaux, et que les alertes de
> la cellule sont visibles. L'affichage dans un navigateur n'est pas vérifié.

### Application Windows

L'**application de bureau** fonctionne seule sur un PC, sans serveur, sans base de données à installer et sans
connexion Internet.

**Il faut** un PC sous Windows 10 (version 1809 ou plus récente) ou Windows 11, en 64 bits. Le composant WebView2,
présent d'office sous Windows 11 et sur un Windows 10 à jour, suffit ; rien d'autre n'est à installer.

1. **Télécharger** l'artefact **`lettra-windows`** (voir plus haut). Vous obtenez `lettra-windows.zip`.
2. **Débloquer** (une seule fois). Clic droit sur le fichier zip, **Propriétés**, cochez **Débloquer** en bas de
   l'onglet Général, puis **OK**. Sans cela, Windows bloque les fichiers extraits un par un.
3. **Extraire.** Clic droit sur le zip, **Extraire tout…**, vers un dossier de votre choix (par exemple
   `Documents\Lettra`).
4. **Lancer.** Dans le dossier extrait, double-cliquez sur **`Lettra.App.Maui.exe`**. Le prototype n'est pas signé :
   si Windows affiche « Windows a protégé votre ordinateur », cliquez sur **Informations complémentaires**, puis
   **Exécuter quand même**. Pour la suite, clic droit sur l'exécutable, **Envoyer vers**, **Bureau (créer un raccourci)**.
5. **Créer le compte du poste.** Au premier lancement, Lettra demande votre nom et une **phrase secrète** d'au moins
   12 caractères. Elle protège les dossiers chiffrés de ce PC et **ne peut pas être récupérée** : notez-la pour la
   durée de la démo.

C'est prêt. Lettra a créé un cabinet local et un premier dossier chiffré, « Dossier principal ». Dès le compte créé,
il le remplit en arrière-plan d'un an d'activité fictive, en une cinquantaine de secondes : vous pouvez travailler
pendant ce temps, les écrans montrent les données quand on les rouvre
([ce que contient la démo](#ce-que-vous-trouvez-en-arrivant)).

> La CI construit cet exécutable et le lance à chaque modification de `main` (job « Build MAUI (Windows) ») : il
> s'ouvre, crée le cabinet et le premier dossier sans erreur. Le parcours ci-dessous, avec ses données fictives, est
> rejoué à chaque modification par le test `DemoWalkthroughTests`.

### Android (émulateur ou téléphone)

La même application de bureau, pour Android 7.0 ou plus récent. L'APK est signé avec une clé de développement : il
s'installe à la main, hors Play Store.

#### Dans l'émulateur d'Android Studio (Windows, macOS, Linux)

1. Dans Android Studio, **Device Manager**, créer et démarrer un appareil virtuel (image Android 14, API 34, x86_64
   sur un PC, arm64 sur un Mac à puce Apple).
2. Télécharger l'artefact **`lettra-android`** et l'extraire : `fr.lettra.app-Signed.apk`.
3. Faire glisser l'APK sur la fenêtre de l'émulateur, ou, dans un terminal :

   ```bash
   adb install -r fr.lettra.app-Signed.apk
   adb shell am start -n fr.lettra.app/.MainActivity
   ```

4. Lettra apparaît dans la liste des applications. Au premier lancement, créer le compte du poste comme sous Windows
   (étape 5).

> Vérifié par la CI (job « MAUI Android (APK et émulateur) ») sur un émulateur API 34 x86_64 : l'application
> démarre, ne plante pas et crée son premier dossier chiffré. Une capture d'écran et le journal sont dans l'artefact
> `lettra-android-capture`. Sur un émulateur arm64 (Mac), non vérifié.

#### Sur un téléphone Android

Copier l'APK sur le téléphone (câble, e-mail à soi-même, Drive), l'ouvrir, et autoriser l'installation
d'applications de cette source quand Android le demande. Avec le mode développeur et le débogage USB, `adb install`
ci-dessus fonctionne aussi. Désinstaller l'application après la démonstration.

L'APK de démonstration accepte les certificats que vous avez installés vous-même sur le téléphone (celui de la
démonstration en réseau local, par exemple). Une version construite sans démonstration (`-p:LettraDemo=false`)
n'accepte que les autorités de certification d'Android : un serveur du cabinet doit alors avoir un certificat
reconnu par Android.

> Non vérifié sur un vrai téléphone.

### Simulateur iOS (Mac)

**Il faut** un Mac à puce Apple avec Xcode (le simulateur iOS n'existe que sur Mac).

1. Télécharger l'artefact **`lettra-ios-simulator`** et l'extraire : `Lettra.App.Maui.app`.
2. Ouvrir le simulateur : `open -a Simulator` (ou Xcode, **Open Developer Tool**, **Simulator**), avec un iPhone.
3. Faire glisser `Lettra.App.Maui.app` sur le simulateur, ou :

   ```bash
   xcrun simctl install booted Lettra.App.Maui.app
   xcrun simctl launch booted fr.lettra.app
   ```

4. Créer le compte du poste comme sous Windows (étape 5).

Cette version ne s'installe que dans le simulateur, pas sur un iPhone : un iPhone réel exige une version signée avec
un compte Apple Developer, que le prototype n'a pas. Sur un iPhone, utiliser le navigateur.

> Vérifié par la CI (job « MAUI iOS (simulateur) et macOS (Mac Catalyst) ») : l'application s'installe, crée
> son premier dossier chiffré et est toujours ouverte 10 secondes plus tard. Captures et journaux dans l'artefact
> `lettra-apple-captures`.

### macOS (Mac Catalyst)

L'artefact **`lettra-macos`** contient l'application pour Mac à puce Apple. **Elle n'est pas encore utilisable** :
sur Mac, la clé du cabinet va dans le Trousseau, qui n'est accessible qu'à une application signée par un compte Apple
Developer. Non signée, elle s'ouvre puis s'arrête en créant le premier dossier. **Sur Mac, utilisez le navigateur.**

Pour constater ce point : extraire `Lettra-macos.zip`, puis `xattr -dr com.apple.quarantine Lettra.app` (ou clic
droit, **Ouvrir**, la première fois) et `open Lettra.app`. Rien n'a été essayé sur un Mac Intel.

> La CI construit l'application et la lance ; ce lancement est non bloquant et signale l'arrêt par un avertissement,
> jusqu'à ce qu'un certificat Apple soit configuré.

---

## Serveur commun

Le navigateur travaille toujours sur le serveur. Une application installée (Windows, macOS, Android, iOS) travaille
seule sur ses propres données, sauf si on la relie au serveur du cabinet : menu **Pilotage** > **Serveur du cabinet** (ou Ctrl+K),
saisir l'adresse (https obligatoire hors localhost), redémarrer l'application, se connecter. Elle travaille alors sur
le même dossier que le navigateur et les autres appareils reliés. Elle a besoin du réseau ; ses données d'appareil
seul restent sur l'appareil, intactes, et reviennent avec **Revenir à cet appareil seul**. Détails et état par
appareil : [docs/serveur-commun.md](docs/serveur-commun.md).

Vérifié à chaque CI (job « Démo navigateur ») : la composition de l'application reliée au serveur se connecte au
Keycloak de la démo (code + PKCE, mot de passe et code à usage unique), voit le client saisi dans le navigateur et en
crée un que le navigateur retrouve.

## Vue cabinet

Le sélecteur de dossier (barre du haut, ou Ctrl+K « Ouvrir le dossier … ») change de dossier en un clic. Il propose la
recherche, les favoris (étoile) et les récents, rangés dans votre compte : ils vous suivent d'un appareil à l'autre (sur
un poste isolé, ils restent sur l'appareil). La démo Docker contient trois dossiers : Dossier de démonstration, Bâti Rénov' Lemoine et Négoce Méditerranée Distribution (ajoutés aussi à une démo déjà remplie, sans rien
effacer).

La page **Vue cabinet** liste les dossiers que vous pouvez ouvrir, les plus urgents d'abord. Pour chacun, elle montre
les factures en retard, les rejets de la plateforme, les alertes ouvertes, la revue du mois et le relevé bancaire non
rapproché. La colonne relevé non rapproché devient à traiter après 15 jours et urgente après 30. Ces chiffres sont
calculés une fois par jour (par le Worker, ou par l'API dans la démo, où le premier calcul a lieu 15 s après le
démarrage), sur « Actualiser », ou à la première ouverture. Ce ne sont
que des compteurs : aucun montant ni nom de client ne quitte le dossier. Le bouton « Tableaux de bord » en haut de la Vue cabinet ouvre
l'onglet « Mes dossiers » des tableaux de bord : les mêmes dossiers en graphiques.

**Revue du dossier** (menu Comptabilité) est une liste de contrôle par mois ou par trimestre. Chaque coche garde qui
l'a posée et quand. Détails : [docs/vue-cabinet.md](docs/vue-cabinet.md).

État : navigateur contrôlé à la main dans la démo Docker à 1440, 768 et 375 px, et par les tests automatiques ;
application installée (seule ou reliée au serveur commun) : même écran, changement de dossier branché, **non lancée**
sur un appareil.

---

## Premier usage, pas à pas

Le parcours va de la fiche de l'entreprise au FEC, en une quinzaine de minutes, dans l'application installée ou dans
le navigateur : les écrans sont les mêmes. Les noms et numéros proposés sont **fictifs** et acceptés par les contrôles
de Lettra ; copiez-les tels quels.

### Ce que vous trouvez en arrivant

**Le tableau de bord.** L'**Accueil** montre les indicateurs du mois (chiffre d'affaires HT, factures, devis en
attente, brouillons), le chiffre d'affaires des six derniers mois, les dernières factures et les derniers devis, et
deux raccourcis : **Nouveau devis**, **Nouvelle facture**. Il montre aussi les factures en retard (les cinq plus
anciennes, avec leur reste dû) et les transmissions à la plateforme (non transmises, en cours, à traiter, encaissées).
Sur un dossier vide, il propose à la place de compléter la fiche entreprise et d'ajouter un client, et le bouton
**Charger les données de démonstration** remplit le dossier d'un an d'activité fictive en une demi-minute. Ce bouton
est absent en Production et pour qui n'a pas le droit d'émettre des factures.

**Les données de démonstration.** Le dossier ouvert arrive avec un an d'activité d'une entreprise fictive,
« Atelier Numérique Conseil » (SARL de conseil et services numériques, Lyon) : sa fiche entreprise, 30 à 50 clients
(entreprises et particuliers), des fournisseurs, un catalogue de prestations et de matériel, des grilles tarifaires,
des devis à tous les stades (brouillon, envoyé, accepté, refusé, expiré, facturé), des factures d'acompte et de solde,
des avoirs, les transmissions à la plateforme simulée, des encaissements (à l'heure, en retard, en deux fois, impayés)
et quelques alertes. Les données sont croisées : chaque client a ses articles habituels, certains reviennent souvent.
Tout est créé par les vrais cas d'usage : numéros, chaîne anti-fraude, écritures et FEC sont ceux de l'application.
Le prestataire de services a opté pour la TVA sur les débits : ses factures et ses avoirs portent la mention.

- **Dans le navigateur**, le « Dossier de démonstration » est rempli au premier démarrage du serveur.
- **Dans l'application installée**, le « Dossier principal » est rempli en arrière-plan juste après la création du
  compte, en une cinquantaine de secondes. Deux autres dossiers sont créés sur le poste (« Bâti Rénov' Lemoine »,
  artisan du bâtiment, et « Négoce Méditerranée Distribution », négoce) ; le sélecteur de dossier de la barre du haut
  (ou Ctrl+K) passe de l'un à l'autre.

Le jeu ne s'ajoute jamais à un dossier qui a déjà des devis ou des factures. Pour repartir d'un dossier rempli à neuf,
voir « Repartir de zéro » dans [En cas de problème](#en-cas-de-problème).

**La navigation.** **Accueil** et **Vue cabinet**, puis **Ventes** (**Devis**, **Factures**, **Avoirs**, **Factures
récurrentes**), **Tiers** (**Clients**, **Fournisseurs**, **Catalogue**), **Comptabilité** (**Encaissements**,
**Rapprochement bancaire**, **Relances**, **Journal et balance**, **FEC**, **Déclarations**, **Revue du dossier**) et
**Pilotage** (**Tableaux de bord**, **Alertes**, **Entreprise**, **Mes préférences**, **Équipe et accès** pour qui
administre le cabinet, **Serveur du cabinet**). Sur un
ordinateur, le menu est à gauche et se replie en icônes ; sur une tablette, c'est une colonne d'icônes ; sur un
téléphone, des onglets en bas (Accueil, Devis, Factures, Clients, **Plus** pour le reste). En haut, le champ
« Rechercher un écran, une pièce, un client… » ouvre la palette de commandes, aussi au clavier avec **Ctrl+K**
(« Nouveau devis », « Nouvelle facture »).

**Les thèmes.** Le bouton **Apparence**, en haut à droite (dans **Plus** sur un téléphone), propose cinq thèmes :
**Comme le système**, **Clair**, **Sombre**, **Ardoise** et **Contraste élevé**, ainsi que la densité : **Confort**
(au doigt) ou **Expert** (souris et clavier ; **Ctrl+Maj+D** bascule). Le choix s'applique tout de suite et vous suit
sur tous vos appareils (rangé dans votre compte ; sur un poste isolé, sur l'appareil). Thème, densité, langue, écran
d'accueil, valeurs de saisie et suggestions se règlent aussi dans **Pilotage** > **Mes préférences**.

**Les notifications.** Chaque création, modification et suppression s'annonce dans une notification en bas à droite
(au-dessus des onglets sur un téléphone). Une réussite disparaît après 6 secondes ; une erreur reste jusqu'à ce qu'on
la ferme.

**Les listes.** Pendant le chargement, une silhouette de la liste s'affiche ; une liste vide dit quoi faire ; sur un
téléphone, chaque ligne devient une carte.

*Vérifié : plus de 600 tests de l'interface ; galerie de 28 écrans, plus celle de l'administration, × 4 thèmes ×
3 largeurs (375, 768, 1440 px), sans défaut d'accessibilité (axe) ;
éditeurs contrôlés dans Chromium de 375 à 1920 px. Détails : [docs/design/systeme-de-design.md](docs/design/systeme-de-design.md).*

### 1. La fiche de votre entreprise

Menu **Entreprise**. Ces mentions figurent sur chaque facture ; sans elles, Lettra refuse de valider. Avec les
données de démonstration, la fiche est déjà remplie (« Atelier Numérique Conseil ») : vous pouvez la garder et passer
à l'étape 2, ou la remplacer par les valeurs ci-dessous.

| Champ | Valeur fictive |
|---|---|
| Nom ou raison sociale | Cabinet Démo Conseil |
| Forme juridique | SAS |
| Capital social (€) | 10000 |
| SIREN | 900000019 |
| Immatriculation (RCS ou RM) | RCS Paris 900 000 019 |
| N° de TVA intracommunautaire | FR25900000019 |
| Adresse, Code postal, Ville | 12 rue des Exemples, 75010, Paris |
| Taux des pénalités de retard (%) | 10 |
| Escompte pour paiement anticipé | Pas d'escompte pour paiement anticipé |

Cliquez sur **Enregistrer** : « Fiche entreprise enregistrée. » N'utilisez pas le bouton « Vérifier (VIES) » : il
interroge le service européen, qui ne connaît pas ces numéros fictifs.

### 2. Un client

Menu **Clients**. La liste est à gauche, la fiche à droite (sur téléphone et tablette, l'une ou l'autre, avec
« Retour à la liste »). Cliquez sur **Nouveau client** (touche **Inser**) et saisissez tout à la main (la recherche
dans l'annuaire SIRENE ne sert qu'aux vraies entreprises) :

- Type de client : **Entreprise**
- Nom ou raison sociale : **Boulangerie Exemple**
- SIRET : **90000002700012**
- Adresse : **3 place du Marché**, **69002**, **Lyon**

Cliquez sur **Créer** : la fiche s'affiche en lecture. **Modifier** (**F2**) repasse en saisie dans la même fiche,
**Échap** revient sans enregistrer (Lettra demande avant d'abandonner une saisie modifiée). Les blocs de la fiche
(Coordonnées, Dernières factures, Derniers devis) se replient d'un clic. Les **Fournisseurs** ont la même
présentation.

### 3. Un devis

Menu **Devis**, **Nouveau devis**. L'écran est découpé en blocs repliables (Informations, Livraison et paiement,
Lignes, Notes) ; un bloc replié montre un résumé d'une ligne, et l'état choisi est retenu sur l'appareil. Les factures
ont la même présentation.

1. Client : **Boulangerie Exemple** ; Objet : *Accompagnement comptable*.
2. Dans la grille **Lignes**, une ligne : Désignation *Tenue comptable mensuelle*, Qté **3**, PU HT **250**.
   **Inser** ajoute une ligne, **Entrée** ou **Tab** passe à la cellule suivante, **F4** ouvre une liste de choix.
   Le brouillon s'enregistre tout seul dès que le client et une ligne complète sont saisis ; **Enregistrer** (Ctrl+S) l'enregistre tout de suite et dit ce qui manque. **Valider** s'active une fois le brouillon enregistré et la checklist sans point bloquant.
3. Le panneau **Conformité de la pièce** liste chaque mention : « Bloquant », « À vérifier » ou « Présent ». Un point
   bloquant se corrige d'un clic, qui ouvre le bon écran.
4. **Valider** (Ctrl+Entrée). Le bouton **PDF** (Ctrl+P) produit le devis à remettre au client.
5. Le client accepte : **Accepté**, puis **Facturer**. Lettra crée la facture à partir du devis.
   (« Facture d'acompte » facture un pourcentage du devis.)

### 4. La facture

La facture s'ouvre en brouillon (menu **Factures** pour la retrouver).

1. **Valider** : « Facture validée sous le numéro FAC-… ». Le numéro est définitif, sans trou ni doublon ; la facture
   n'est plus modifiable.
2. **Télécharger le Factur-X** : le PDF lisible par tous, avec sa facture électronique intégrée. **XML UBL** donne
   l'autre format accepté par les plateformes.

### 5. La transmission à la plateforme agréée (simulée)

Sous la facture validée, le panneau **Plateforme agréée** :

1. Format **Factur-X (CII)**, puis **Transmettre à la plateforme**. La facture passe « Émise » et la plateforme
   l'indique « Déposée ».
2. **Actualiser les statuts** : « Émise », « Reçue », « Mise à disposition » apparaissent dans l'historique, comme
   avec une vraie plateforme.
3. **Déclarer un encaissement** : date du jour, Montant TTC **900**, puis **Déclarer**. « Encaissé : 900 sur 900 TTC ».

La plateforme simulée garde ses statuts en mémoire : après un redémarrage de Lettra, la facture reste émise et
transmise, mais l'historique des statuts repart de zéro.

### 6. Un avoir

Sur la facture émise, saisissez un **Motif de l'avoir** (*Geste commercial*), puis **Avoir**. L'avoir reprend toute
la facture et s'ouvre en brouillon : **Valider** lui donne son numéro. Il se retrouve dans le menu **Avoirs**.

### 7. Le FEC de l'exercice

Menu **FEC** : l'exercice est l'année civile en cours (modifiable, 24 mois au plus).

1. **Exporter le FEC** : le fichier `<SIREN>FEC<date de clôture>.txt` (par exemple `900000019FEC20261231.txt` avec
   la fiche de l'étape 1) est téléchargé avec les écritures de vente de l'exercice, dont celles de votre facture et de
   votre avoir.
2. Le **Rapport du validateur** s'affiche : « Fichier conforme. », le nombre de lignes et d'écritures, les totaux
   débit et crédit, égaux.
3. **Contrôler un fichier** passe au même validateur un FEC venu d'un autre logiciel ; chaque constat indique la
   ligne, la colonne et l'écriture concernées.

Sans SIREN dans la fiche entreprise, l'export le demande au lieu de produire un fichier.

### 8. Les déclarations : TVA, DES et calendrier fiscal

Menu **Déclarations** (groupe Comptabilité). Lettra prépare, le cabinet dépose : rien n'est télédéclaré.

1. Dans **Entreprise**, choisissez le « Régime de déclaration de la TVA » (réel normal, réel simplifié ou mini-réel).
   Sans régime, l'onglet **TVA** le demande.
2. Onglet **TVA** : le brouillon de la CA3 (ou de la CA12 au réel simplifié) du mois écoulé, partie ventes. Chaque case
   (A1, 08, E2, 16, 28…) s'ouvre sur les factures, acomptes et avoirs qui la composent. Les contrôles disent si la TVA
   correspond au journal, s'il reste des brouillons ou des factures non transmises. **Marquer comme déposée** quand
   c'est fait. La TVA déductible reste à 0 : les achats ne sont pas encore dans Lettra. Le brouillon de CA3 déclare la
   TVA des services à la date de chaque encaissement pour les factures validées à partir de cette version.
3. Onglet **DES** : la déclaration européenne de services du mois (clients de l'Union en autoliquidation) et son
   fichier CSV.
4. Onglet **Calendrier fiscal** : les échéances de l'année (CA3 ou CA12 et acomptes, DES, CFE, IS, liasse), proches, en
   retard ou déposées. Les échéances proches et dépassées arrivent aussi dans **Alertes** (passe toutes les 12 heures,
   par le Worker ou, dans la démo sans Worker, par l'API).
5. Onglet **Réglages fiscaux** : date de clôture, CA3 mensuelle ou trimestrielle, jour limite de la TVA (du 15 au 24), IS et
   CFE.

Les correspondances de cases et les dates sont à faire relire par un expert-comptable. *(Vérifié : navigateur,
Chromium Linux à 1440, 768 et 375 px, données conservées après une mise à jour de la démo ; non vérifié : application
native, Safari, Firefox.)* Détails : [docs/declarations-fiscales.md](docs/declarations-fiscales.md).

#### Kit contrôle fiscal

Menu **Comptabilité > Déclarations > Contrôle fiscal** (`/tax/audit-kit`). Choisissez la période (par défaut l'année
civile en cours) puis **Préparer le kit** : Lettra télécharge `kit-controle-fiscal-<SIREN>-<du>-<au>.zip` avec le FEC
et son rapport de validation, l'attestation d'intégrité de la chaîne anti-fraude (registre des clés, archives scellées
des clôtures), la piste d'audit fiable (qui a créé, validé, émis chaque pièce ; encaissements ; journal chaîné), le XML
CII et le PDF Factur-X de chaque pièce, et le journal des transmissions à la plateforme agréée. `manifeste.json` donne
l'empreinte SHA-256 de chaque fichier ; la sienne, affichée à l'écran, est celle du kit. Rien n'est envoyé à
l'administration. Permission : `ArchivesExporter` (rôles manager et auditeur). Détails et points à faire relire par un
expert-comptable : [docs/kit-controle-fiscal.md](docs/kit-controle-fiscal.md). *(Vérifié : démo Docker (navigateur,
1440, 768 et 375 px) ; application installée non vérifiée sur appareil.)*

### 9. Encaissements et rapprochement bancaire

Menu Comptabilité > **Encaissements** : saisissez un règlement reçu (virement, chèque, carte, espèces, prélèvement),
total, partiel ou réparti sur plusieurs factures. Chaque règlement passe son écriture au journal de banque (BQ,
512/411) et lettre le compte client (codes A, B, C…). Un règlement se contre-passe avec un motif ; il n'est jamais
supprimé.

Menu Comptabilité > **Rapprochement bancaire** : importez un relevé CAMT.053, OFX ou CSV (aucune connexion à la
banque). Lettra propose les factures correspondantes avec sa raison (numéro dans le libellé, montant et nom, montant et
IBAN connu, somme de plusieurs factures) ; vous validez une par une ou en masse. Les petits écarts, sous un seuil
réglable, sont soldés en 658/758. La TVA des prestations de services passe de 44574 à 44571 à l'encaissement, pour les
factures validées à partir de cette version. Dans la démo, le bouton **Importer un relevé de démonstration** fabrique
un relevé fictif à partir des factures dues.

Les comptes utilisés sont à faire relire par un expert-comptable. *(Vérifié : API, écrans et règles par les tests
automatiques ; non vérifié à la main dans la démo Docker ni dans les applications installées. Hors ligne : sur le
serveur ou dans la base locale du poste seul, pas encore de synchronisation de la réplique.)* Détails :
[docs/encaissements.md](docs/encaissements.md).

### 10. Factures récurrentes

Menu Ventes > **Factures récurrentes** > **Nouveau modèle** : un nom, le client, les lignes, la fréquence (mensuelle,
trimestrielle, annuelle), le début, une fin facultative, et une facturation à échoir ou échue. Vous pouvez ajouter une
révision annuelle des prix : un pourcentage fixe, ou l'indice Syntec que vous saisissez. Les factures dues sont créées
en brouillon, ou validées si vous cochez « Valider automatiquement les factures » ; le numéro est attribué à la
validation. Pour l'essayer, choisissez un début deux mois en arrière : le bandeau propose **Créer les factures dues**
et en crée trois. Chacune porte sa période (« Période facturée : du … au … »), reprise dans le Factur-X et l'UBL. Un
modèle se met en pause, se reprend et se supprime ; les factures déjà créées restent. Le Worker crée les factures dues
toutes les 6 heures, sous son compte de service (qui doit avoir FactureSaisir et FactureValider). Dans la démo, l'API
crée elle-même les factures dues : une minute après son démarrage puis toutes les 15 minutes, sous le compte fictif
« Facturation récurrente (automatique) ». Un modèle dont le début est passé a donc ses factures sans clic, au plus tard
15 minutes après l'enregistrement (le bouton « Créer les factures dues » reste disponible). Sans Worker ni compte de
service, rien ne se crée tout seul. *(Vérifié : démo Docker dans Chromium à 375, 768 et 1440 px, et tests
automatiques sur SQLite, PostgreSQL et SQL Server ; non vérifié dans les applications installées. Les modèles ne se
synchronisent pas entre appareils.)* Détails : [docs/factures-recurrentes.md](docs/factures-recurrentes.md).

### 11. Relances des factures échues

Menu Comptabilité > **Relances** : les factures échues non soldées (les encaissements sont déduits), avec leur retard,
les pénalités possibles et le prochain niveau de relance (rappel amical, relance, dernière relance, mise en demeure ;
J+3, J+15, J+30, J+45 par défaut, réglables). **Voir la relance** montre le texte exact, dans la langue du client ;
**Envoyer par e-mail** l'envoie, **Noter comme envoyée** garde la trace d'une relance faite par courrier ou téléphone.
**Envoyer les relances du jour** envoie tout ce qui est dû. L'historique se cherche, se filtre et se trie. Les deux
derniers niveaux citent les intérêts de retard et l'indemnité de 40 €. L'envoi automatique quotidien est coupé par
défaut (case dans « Scénario de relance ») ; une fois coché, le Worker le fait toutes les 12 heures, et l'API dans la
démo.

Dans la démo, aucun e-mail ne sort de la machine : ils arrivent dans **MailPit**, http://localhost:8025. Les
invitations de **Équipe et accès** y arrivent aussi. *(Vérifié : API, écrans et règles par les tests automatiques ;
envoi, relance notée, passe du jour, invitation et passe automatique vérifiés à la main dans la démo Docker avec
MailPit, à 1440 et 375 px. Non vérifié dans les applications installées.)* Détails :
[docs/relances-et-e-mails.md](docs/relances-et-e-mails.md).

### Et ensuite

- **Catalogue** : les articles et prestations, même présentation que les clients (liste, fiche, **Modifier**,
  **Archiver**) ; **Saisie en grille** reste disponible pour en saisir beaucoup au clavier. La bascule « Liste |
  Saisie en grille » en haut de page passe d'un affichage à l'autre dans les deux sens (`catalog`, `catalog/grid`).
  Une ligne de facture peut aussi se saisir librement.
- **Journal et balance** : les écritures de vente de la période (journal, balance, grand livre d'un compte ou d'un
  client), en lecture, les mêmes que dans le FEC. La fiche d'un client y renvoie par **Compte client**. Les
  encaissements y figurent au journal de banque (BQ) et lettrent le compte 411 (étape 9).
- **Supprimer une ligne** : chaque tableau a une corbeille en bout de ligne. Un clic, une confirmation dans la ligne,
  puis un message. Un devis ou une facture ne se supprime qu'en brouillon (sinon le message dit quoi faire : refuser,
  laisser expirer, émettre un avoir). Un client ou un fournisseur supprimé disparaît des listes, ses pièces restent
  consultables. Un article du catalogue s'archive et se réactive. Détails :
  [docs/suppression-des-lignes.md](docs/suppression-des-lignes.md).
- **Alertes** : le centre d'alertes techniques (chaîne anti-fraude d'un dossier, trou de numérotation). Il ne montre
  aucune donnée de client. La chaîne de chaque dossier est clôturée et contrôlée une fois par jour, par le Worker ou,
  dans la démo sans Worker, par l'API (30 s après le démarrage) ; une alerte non prise en charge est escaladée selon sa
  gravité, par une passe qui tourne chaque minute.
- **Verrouiller** (en haut) : ferme la session ; la phrase secrète la rouvre.
- **Langue** : l'écran suit la langue de l'appareil ou du navigateur (français ou anglais). Chaque client et chaque PDF peuvent avoir la
  leur (« Langue des devis et factures imprimés », « Langue du PDF »). Dates et montants suivent la langue de l'écran :
  jj/mm/aaaa et 15 128,00 en français, mm/dd/yyyy et 15,128.00 en anglais ; chaque champ de date a un calendrier
  (bouton à droite) et accepte aussi 19102026 ou 19/10. Détails : [docs/dates-et-langue.md](docs/dates-et-langue.md).
- **Tableaux de bord** (menu **Pilotage** > **Tableaux de bord**, `/pilotage`) : les indicateurs des tableaux Grafana dans
  Lettra, filtrés par les droits de chacun : « Ce dossier » (lecture du dossier), « Mes dossiers » (dossiers lisibles),
  « Administration » (dossiers administrés, onglet caché sinon) ; aucun montant ni nom de client. Dans la démo Docker,
  les trois onglets sont remplis pour les trois dossiers ; sur un poste isolé ou relié, seul « Ce dossier » est
  disponible. Voir [docs/tableaux-de-bord.md](docs/tableaux-de-bord.md).
- **Équipe et accès** (menu **Pilotage**) : qui travaille dans le cabinet et ce que chacun peut ouvrir. Onglet
  **Personnes** : ajoutez une personne (nom et adresse), puis donnez-lui un accès en trois questions (quel rôle, où,
  jusqu'à quand) ; sa fiche dit en clair ce qu'elle peut faire, page par page. Onglet **Équipes** : regroupez des
  personnes et des dossiers, un rôle donné à l'équipe vaut pour tous ses membres. Onglet **Rôles** : les rôles fournis,
  et vos rôles sur mesure composés page par page (Aucun, Voir, Modifier, Valider). Chaque retrait se défait depuis le
  message qui le confirme. Dans l'application installée sur un poste isolé, l'espace est en lecture seule : on y voit
  son propre compte et ses rôles. Dans la démo navigateur, l'utilisateur fictif (Camille Démo) est administrateur du
  cabinet. *(Vérifié : navigateur, rendu Chromium à 375, 768 et 1440 px et tests ; poste isolé par un test de bout en
  bout ; application native non lancée.)*

### Menu et aide

Le menu de gauche se règle à la façon de chacun. Cliquez sur le titre d'une section pour la replier (chevron), et sur
l'étoile d'un écran pour l'épingler dans « Favoris » ; ces réglages sont retenus par utilisateur sur l'appareil. Le
champ « Aller à… » en haut du menu trouve un écran par son nom ou par un mot courant (« tva », « impayé », « banque ») ;
Entrée l'ouvre, et « Chercher partout » passe à la palette Ctrl+K. « Aide et questions », en bas du menu, derrière le
bouton « ? » ou dans la feuille « Plus » du téléphone, répond aux questions fréquentes en langage courant (« comment
annuler une facture ? »), relance le tutoriel de l'écran et liste les raccourcis, sans IA. Détails :
[docs/menu-et-aide.md](docs/menu-et-aide.md).

### Mes préférences

Menu **Pilotage** > **Mes préférences** (`/settings/preferences`) : thème, densité, langue, écran d'accueil, valeurs de
saisie par dossier (TVA, unité, conditions de paiement, mentions, validité des devis), suggestions par usage (clients et
articles les plus choisis en tête), « Reprendre où j'en étais » à l'accueil et dans Ctrl+K, réinitialisation. Rangées
dans le compte (base Catalog) quand le serveur le permet, elles suivent l'utilisateur sur tous ses appareils ; en poste
isolé elles restent sur l'appareil. Aucun nom de client ni montant, jamais dans les journaux ni Grafana. Détails :
[docs/preferences-utilisateur.md](docs/preferences-utilisateur.md).

*(Vérifié : tests automatiques (domaine, cas d'usage, SQLite/PostgreSQL/SQL Server, passerelles, écrans bUnit) et démo
Docker de bout en bout sur ordinateur et téléphone (thème, langue après rechargement, valeurs par défaut sur un
dossier, « Retenir par défaut », « Reprendre où j'en étais », reprise sur un second appareil). Non vérifié dans les
applications installées.)*

### Trier et filtrer les listes

Toutes les listes de Lettra se trient et se filtrent, sur ordinateur, tablette et téléphone (navigateur, PWA et
applications MAUI).

- **Tableaux** (factures, avoirs, acomptes, devis, encaissements, rapprochement bancaire, alertes, Vue cabinet, soldes
  clients de la balance, DES) : un clic sur un en-tête de colonne trie, un second clic inverse le sens.
- **Listes en deux volets** (clients, fournisseurs, catalogue) et **Équipe et accès** : liste **Trier par** sous la
  recherche (nom ou ville ; référence, désignation ou prix ; nom, arrivée ou accès).
- **Filtres** : recherche, puis selon la liste nature, statut, période, fourchette de montant, gravité, état ou
  échéance. **Effacer les filtres** apparaît dès qu'un filtre est posé.
- **Téléphone** : les tableaux deviennent des cartes ; le bouton **Filtres** (avec le nombre de filtres posés) déplie
  les autres filtres et une liste **Trier par**.
- Journal des ventes et grand livre restent chronologiques (solde progressif) ; le calendrier fiscal reste par date et
  se filtre par échéance et par état.

Pour l'API : `sort` et `direction` sur `GET /api/v1/invoices`, `/quotes`, `/customers`, `/suppliers`, `/catalog/items`
et `/payments`, plus `minAmount` / `maxAmount` (pièces, encaissements) et `kind` (clients, articles). Détails :
[docs/tris-et-filtres.md](docs/tris-et-filtres.md).

| Test sur chaque appareil | Comment | Vérifié |
|---|---|---|
| Navigateur ordinateur (1440 px) | Démo Docker, Factures puis clic sur « Total TTC » | Oui |
| Navigateur tablette (768 px) | Démo Docker, fenêtre à 768 px ou mode appareil | Oui |
| Navigateur téléphone (375 px) | Démo Docker, mode appareil 375 px, bouton « Filtres » | Oui |
| Application Windows, macOS, Android, iOS | Mêmes écrans (composants partagés) | Non vérifié sur appareil |

### Tutoriel, aide à la saisie et voix

- **Tutoriel** : chaque écran se présente la première fois. Le bouton **?** en haut le rejoue, et le menu
  Apparence > « Réinitialiser les tutoriels » permet de tout revoir. Le tableau de bord affiche « Pour bien démarrer »,
  qui se coche tout seul. Rien ne démarre avant la connexion : l'accueil se présente juste après. *(Vérifié : Chromium
  Linux 1440/768/375 px ; non vérifié : Safari, Firefox, MAUI.)*
- **Aide à la saisie** : code postal ↔ ville dans la fiche client, « / » pour les modèles de texte (objet, notes,
  conditions de paiement), saisie rapide dans les lignes (`3h`, `1h30`, `1,5 j`, `2x45`), F4 sur la désignation,
  articles habituels du client. *(Vérifié : Chromium Linux.)*
- **Voix** (optionnelle) : bouton micro en haut, micro dans les champs de texte, F8 maintenue pour dicter ou donner une
  commande (« nouveau devis », « ouvrir les factures en retard », « ajoute trois heures de conseil à 120 euros »). Une
  commande est toujours affichée avant d'être exécutée. Sans moteur Whisper configuré, le micro explique que la
  dictée n'est pas activée. *(Vérifié : Chromium Linux avec micro simulé ; non vérifié : vrai Whisper dans
  l'interface, Safari, Firefox, MAUI.)* Le moteur est décrit juste après.

Détails : [docs/aide-a-la-saisie.md](docs/aide-a-la-saisie.md).

### Dictée et commandes vocales

La dictée et les commandes vocales utilisent Whisper, **toujours en local** : le serveur whisper.cpp de la
démonstration (navigateur) ou Whisper.net sur l'appareil (application installée). L'audio n'est envoyé à aucun
service tiers, et il n'est ni enregistré ni journalisé.

- **Dans la démo Docker**, le conteneur `whisper` télécharge le modèle (142 Mio) au premier lancement, puis le garde
  dans le volume `lettra-demo_whisper-modeles`. Lettra s'ouvre sans l'attendre. Tant que le modèle n'est pas chargé
  (une à quelques minutes selon la connexion), le panneau du micro affiche « La dictée se prépare » et les champs
  n'ont pas de micro. Le micro apparaît tout seul dès que le moteur est prêt, sans recharger la page.
- **Dans l'application installée**, le modèle est téléchargé au premier usage de la voix, dans le dossier de
  l'application (`voix`), puis vérifié par deux empreintes (SHA-1 publié par whisper.cpp et SHA-256 épinglé dans
  Lettra) : un fichier différent est supprimé, jamais chargé. Le système demande l'autorisation du micro la première
  fois.
- **Couper la dictée dans l'application installée** : elle est active par défaut, y compris dans la démonstration.
  Pour la couper sur un appareil, écrire dans le fichier `lettra.json` du dossier de données de l'application
  `{ "Lettra": { "Speech": { "Engine": "None" } } }` (avec `"OnDevice"`, elle revient), puis relancer Lettra. Le
  bouton micro disparaît, le micro n'est jamais ouvert et le modèle n'est pas téléchargé. Pour construire une version
  sans dictée par défaut : `-p:LettraDictee=false`.
- **Le micro demande `https://` ou `http://localhost`** : c'est une règle des navigateurs. Sur le poste, ouvrez
  `http://localhost:8080`. Depuis un téléphone ou un autre poste, utilisez le profil réseau local en HTTPS
  (`compose.lan.yaml`). En `http://192.168…`, le panneau explique pourquoi le micro manque.

Pour vérifier que la voix est active : `GET /api/v1/voix/statut` doit répondre `"enabled": true` et
`"engine": "whisper-serveur"`. Dans le navigateur, une fois connecté, ouvrir la console (F12, onglet **Console**) et
coller :

```js
fetch('/api/v1/voix/statut', { headers: { 'X-Dossier': '0199e3a0-0000-7000-8000-00000000d051', 'X-Lettra-Csrf': '1' } }).then(r => r.json()).then(console.log)
```

Le statut dit que la voix est configurée. Il répond aussi `"ready": true` quand le modèle est chargé (`false` pendant
son téléchargement). Depuis le poste, cette commande doit alors afficher `200` :

```bash
docker compose -f deploy/demo/compose.yaml exec whisper curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:8080/
```

On peut dire, par exemple : « ouvrir les factures en retard », « nouveau devis », « ajoute deux heures d'audit à
90 euros », « remise de dix pour cent ». En dictée, on peut dire la ponctuation : « virgule », « point », « à la
ligne ». Une commande qui valide ou émet une pièce demande toujours confirmation.

| Ce qui est vérifié | Où |
|---|---|
| Phrases lues par une voix de synthèse et transcrites par un vrai Whisper : Whisper.net sous Linux, Windows et macOS, serveur whisper.cpp en conteneur | **Vérifié** (workflow « Voix ») |
| Grammaire des commandes, ponctuation dite, montants, route de l'API | **Vérifié** (tests de la CI) |
| Démo Docker : le conteneur `whisper` télécharge et charge le modèle, l'API lui fait transcrire un son de test ; après une mise à jour, le modèle n'est pas retéléchargé | **Vérifié** (jobs « Démo — charge k6 » et « Démo navigateur » de `deploy.yml`) |
| Application Windows, Android, iOS et Mac Catalyst : Whisper.net et le micro sur l'appareil | **Non vérifié** : l'application est construite avec la voix, mais rien n'a été dicté sur un appareil |
| Une vraie voix humaine, du bruit de bureau, des accents | **Non vérifié** |

Dans les écrans, la voix s'utilise par le bouton micro, le micro des champs ou F8 maintenue (voir
[Tutoriel, aide à la saisie et voix](#tutoriel-aide-à-la-saisie-et-voix)). Détails techniques : [src/Core/Lettra.Speech/README.md](src/Core/Lettra.Speech/README.md).

---

## Ce que la démo ne fait pas encore

| Fonction | État |
|---|---|
| Assistant IA | Les écrans existent : le panneau « Assistant IA » (bouton en haut), le brouillon de devis proposé par l'assistant et le lien « Pourquoi ? » de la checklist de conformité. Ils restent masqués tant que l'IA est désactivée, ce qui est le réglage par défaut (section `Lettra:Ai`) et celui de la démo. L'assistant propose et explique ; il ne valide ni n'émet jamais rien. |
| Vraie plateforme agréée | En attente d'un partenaire ; la démo utilise la plateforme simulée. |
| Serveur du cabinet en production | La démonstration navigateur tourne sur un poste avec Docker ; un vrai serveur du cabinet ou une cellule SaaS demande Keycloak, des certificats et une infrastructure (voir plus bas). |
| Poste relié à un serveur, synchronisation | Prête dans l'application (fichier `lettra.json`), mais demande un serveur Lettra et Keycloak. Sur un poste isolé, la page Synchronisation indique « Ce poste ne synchronise pas ». |
| Hors ligne sur un appareil relié au serveur commun | Relié au serveur commun, l'appareil a besoin du réseau. Reste à étendre la synchronisation aux devis, factures et règlements, ou à y ouvrir les brouillons hors ligne. |
| Invitations | Une personne ajoutée dans **Équipe et accès** reçoit une invitation par e-mail (MailPit dans la démo) et des accès, mais Lettra ne crée pas encore son compte de connexion (Keycloak) : elle ne peut pas encore se connecter. |
| Télédéclaration de la TVA et de la DES | Non : Lettra prépare le brouillon et le fichier, le dépôt se fait sur impots.gouv ou le service en ligne des douanes. La TVA déductible (achats) n'est pas encore reprise. |
| Envoi des devis et factures par e-mail | Non : on télécharge le PDF ou le Factur-X, factures récurrentes comprises. Seules les relances et les invitations partent par e-mail. |
| Relances par courrier ou en PDF, facture des pénalités | Non : Lettra envoie la relance par e-mail ou la note comme faite à la main ; il cite les pénalités sans les facturer. |
| Tableaux de pilotage dans le Grafana d'une cellule | Dans la démo, Prometheus lit les indicateurs sur l'API (`/internal/kpi/metrics`). Les charts Helm ne posent pas encore ce jeton ni cette lecture : dans une cellule déployée, « Pilotage du cabinet », « Pilotage d'un dossier » et « Administration » restent vides dans Grafana. La page **Tableaux de bord** de Lettra, elle, fonctionne partout. |
| Applications signées | Non : avertissement de Windows au premier lancement, APK Android installé à la main, application macOS arrêtée par le Trousseau, pas de version pour iPhone réel. |

---

## En cas de problème

| Symptôme | Que faire |
|---|---|
| Navigateur : la page ne répond pas | Au premier démarrage, le remplissage du dossier de démonstration prend une trentaine de secondes après `up --wait` : attendre, puis recharger ; `docker compose -f deploy/demo/compose.yaml ps` doit montrer `keycloak`, `mailpit` et `api` « healthy » (`whisper`, sans contrôle de santé, est simplement « Up »). |
| Navigateur : pas de micro dans les champs | Ouvrez le micro de la barre du haut, son panneau dit pourquoi. « La dictée se prépare » : le modèle Whisper se télécharge, attendez. Message « https:// ou http://localhost » : changez d'adresse. |
| Navigateur : la voix répond « indisponible » (503) | Le conteneur `whisper` télécharge encore son modèle (142 Mio) ou n'a pas accès à Internet : `docker compose -f deploy/demo/compose.yaml logs whisper` montre le téléchargement (et, sans Internet, un nouvel essai toutes les 30 secondes) ; le serveur est prêt quand la commande de vérification de [Dictée et commandes vocales](#dictée-et-commandes-vocales) affiche `200`. Le reste de Lettra fonctionne pendant ce temps. |
| Navigateur : erreur « Uncaught (in promise) TypeError: Failed to fetch at fromShell » dans la console | C'est l'ancien service worker : fermer tous les onglets de Lettra puis rouvrir (ou DevTools → **Application** → **Service workers** → **Unregister**). La console doit rester vide d'erreurs sur tous les écrans, en ligne comme hors ligne ([détails](deploy/demo/README.md#installer-lapplication-pwa)). |
| Navigateur : le code du second facteur est refusé | L'application TOTP doit accepter SHA-256 (pas Microsoft Authenticator) et l'heure du téléphone être à l'heure. Pour réenrôler sans perdre vos saisies, ne remettez à zéro que Keycloak : `docker compose -f deploy/demo/compose.yaml down`, `docker volume rm lettra-demo_keycloak-donnees`, puis `up -d --wait`. Pas de `down -v` : il efface aussi les données Lettra. |
| Serveur du cabinet : « cet appareil ne fait pas confiance à son certificat » | Installer sur l'appareil le certificat de la démonstration ou l'autorité de certification du cabinet (voir [Téléphone ou tablette sur le même Wi-Fi](#téléphone-ou-tablette-sur-le-même-wi-fi-navigateur)). Sur Android, seul l'APK de démonstration accepte un certificat installé à la main ; une version construite avec `-p:LettraDemo=false` exige un certificat reconnu par Android. |
| Application installée : pas de bouton micro | La dictée est coupée par `"Engine": "None"` dans `lettra.json` (dossier de données de l'application) ou la version a été construite avec `-p:LettraDictee=false` : retirer la ligne ou mettre `"OnDevice"`, puis relancer ([Dictée et commandes vocales](#dictée-et-commandes-vocales)). |
| Navigateur : après la démo en réseau local avec un nom de poste, `http://<nom>:…` passe tout seul en `https://` | C'est l'en-tête HSTS du profil HTTPS : utiliser `http://localhost:8080` sur le poste, ou effacer le réglage du site dans le navigateur (Chrome : `chrome://net-internals/#hsts`, *Delete domain security policies*). Avec une adresse IP, cela n'arrive pas. |
| Serveur du cabinet : « Cette adresse doit commencer par https:// » | Seule la démo sur le même poste (`localhost`, ou Android relié par `adb reverse`) accepte `http` ; ailleurs, saisir l'adresse en `https://`. |
| « Windows a protégé votre ordinateur » | Normal pour un prototype non signé : **Informations complémentaires**, puis **Exécuter quand même**. |
| Rien ne s'ouvre, ou une erreur sur des fichiers bloqués | Le zip n'a pas été débloqué : supprimez le dossier extrait, faites l'étape 2 (Propriétés, **Débloquer**), puis extrayez à nouveau. |
| Fenêtre blanche | Le composant WebView2 manque (vieux Windows 10) : installez « Microsoft Edge WebView2 Runtime » depuis le site de Microsoft, puis relancez. |
| « Phrase secrète incorrecte. » / « Trop d'essais » | Après plusieurs erreurs, le poste se verrouille quelques minutes. Une phrase oubliée ne se récupère pas : voir la ligne suivante. |
| Repartir de zéro (phrase oubliée, nouvelle démo) | Fermez Lettra et supprimez son dossier de données (ci-dessous). Vos saisies sont perdues ; le prochain lancement recrée tout, données de démonstration comprises. Dans le navigateur : `down -v`, puis `up -d --wait` (efface clients, devis, factures **et** l'enrôlement du second facteur). |
| Navigateur : mes données ont disparu après une mise à jour | Une mise à jour ne les efface pas (ci-dessous) : vérifiez que la commande est lancée depuis la racine du dépôt avec `-f deploy/demo/compose.yaml`, sans `-p` ni `COMPOSE_PROJECT_NAME`, et que `docker volume ls` montre toujours `lettra-demo_lettra-donnees`. Un `down -v`, un `docker volume prune -a` ou le nettoyage des volumes de Docker Desktop pendant que la démo est arrêtée l'ont supprimé. |
| « Remplissez d'abord la fiche de l'entreprise émettrice. » | Étape 1 : menu **Entreprise**. |
| Une mention reste « Bloquant » | Cliquez dessus dans **Conformité de la pièce** : Lettra ouvre l'écran à compléter (fiche entreprise ou client). |

**Où sont les données.** Une mise à jour (nouvelle version, nouvelle image) ne les touche pas : elles sont rangées
hors de l'application, et la base est migrée sur place au démarrage.

| Où | Emplacement | Remettre à zéro exprès |
|---|---|---|
| Navigateur (Docker) | Volume `lettra-demo_lettra-donnees` (catalogue, dossier SQLite chiffré, clés de session) ; Keycloak (utilisateur, enrôlement TOTP) dans `lettra-demo_keycloak-donnees`. Le modèle de la voix (sans donnée de dossier) dans `lettra-demo_whisper-modeles`. `docker volume ls` les liste. | `docker compose -f deploy/demo/compose.yaml down -v` |
| Windows | `%LOCALAPPDATA%\Lettra\Lettra.App.Maui\Data` (à coller dans la barre d'adresse de l'Explorateur) : le compte du poste (`compte-local.json`) et, dans `dossiers`, une base chiffrée par dossier. La clé du cabinet est dans le coffre de Windows : copier ces fichiers sur un autre PC ne les rend pas lisibles. | Supprimer ce dossier, Lettra fermé |
| Android | Dans l'application (`/data/user/0/fr.lettra.app/files`), sans sauvegarde Android. | Désinstaller, ou **Effacer les données** |
| iOS, macOS | Dans le dossier de données de l'application, clé du cabinet dans le Trousseau. | Désinstaller l'application (macOS : supprimer aussi son dossier `fr.lettra.app` dans `~/Library`) |

**Mettre à jour la démo.** Dans le navigateur, `git pull` puis la même commande
`docker compose -f deploy/demo/compose.yaml up -d --build --wait` : l'image de l'API est reconstruite, les volumes
restent, et le jeu de démonstration n'est pas rechargé dans un dossier qui a déjà des pièces. N'utilisez `down -v` que
pour tout effacer ; pour libérer de la place, `docker builder prune` vide le cache de construction sans toucher aux
données. Sauvegarder les données du navigateur avant une manipulation risquée :
`docker run --rm -v lettra-demo_lettra-donnees:/donnees -v "${PWD}:/sauvegarde" busybox tar czf /sauvegarde/lettra-donnees.tgz -C /donnees .`
(démo arrêtée). La version qui apporte le serveur commun corrige le realm Keycloak : après cette mise à jour,
l'enrôlement du code à usage unique de l'utilisateur fictif est à refaire une fois. Les données Lettra sont conservées.
Le navigateur prend la nouvelle version tout seul, sans fermer ses onglets : un onglet ouvert dans les dix secondes qui
suivent se recharge de lui-même ; un onglet déjà ouvert affiche « Nouvelle version de Lettra disponible » avec un
bouton **Recharger**, pour ne jamais perdre une saisie en cours. Seule exception, la première fois après une version
d'avant le 19 octobre 2026 : appuyez une fois sur F5 (ou fermez et rouvrez la fenêtre installée).
Sous Windows, fermez Lettra, téléchargez le nouvel artefact et remplacez le dossier extrait : vos
données, rangées ailleurs, sont conservées. Sous Android, `adb install -r` installe la nouvelle version par-dessus
l'ancienne et garde les données : les APK de la CI sont signés avec la même clé de démonstration
(`Platforms/Android/lettra-demo.keystore`). Seuls les APK construits avant cette clé (signés avec une clé de
débogage différente à chaque construction) sont refusés (`INSTALL_FAILED_UPDATE_INCOMPATIBLE`) : il faut alors
désinstaller une dernière fois, ce qui efface les données du téléphone.

---

## Pour les développeurs

Prérequis : SDK .NET 10 (`global.json`), Docker pour les tests PostgreSQL, SQL Server et Keycloak.

```bash
dotnet build Lettra.Ci.slnf     # tout sauf l'application MAUI
dotnet test Lettra.Ci.slnf      # tous les tests (hors MAUI)
```

`Lettra.slnx` contient tout ; `Lettra.Ci.slnf` exclut le projet MAUI (`src/Hosts/Lettra.App.Maui`), dont les
plateformes dépendent du système qui le construit :

| Système | Plateformes | Charges de travail |
|---|---|---|
| Windows | Windows, et Android si sa charge de travail est installée | `dotnet workload install maui-windows maui-android` |
| macOS | Android, iOS, Mac Catalyst (Xcode exigé par le SDK iOS de .NET, 26.6 pour le jeu 10.0.401) | `dotnet workload install maui-ios maui-maccatalyst maui-android` |
| Linux | Android | `dotnet workload install maui-android` (plus un JDK 17 et le SDK Android) |

```bash
dotnet build src/Hosts/Lettra.App.Maui -c Release -f net10.0-windows10.0.19041.0                     # Windows
dotnet publish src/Hosts/Lettra.App.Maui -c Release -f net10.0-android -p:AndroidPackageFormat=apk   # APK
dotnet build src/Hosts/Lettra.App.Maui -c Release -f net10.0-ios -r iossimulator-arm64              # simulateur iOS
dotnet build src/Hosts/Lettra.App.Maui -c Release -f net10.0-maccatalyst -r maccatalyst-arm64       # macOS
```

Les jobs MAUI de la CI font exactement ces builds, puis lancent chaque application.

### Les cinq modes de déploiement

`Lettra:DeploymentMode` choisit les adapters dans `LettraBootstrap` (le seul endroit qui connaît le mode) :

| Mode | Hôte | Dossiers | Identité |
|---|---|---|---|
| 1. Poste isolé (la démo) | Application MAUI, gateway in-process | SQLite chiffré (SQLite3 Multiple Ciphers), clé du cabinet dans le coffre de l'OS | compte local, phrase secrète |
| 2. Serveur du cabinet | API + application web (WASM) | SQLite, PostgreSQL ou SQL Server, registre Catalog | Keycloak |
| 3 et 5. PostgreSQL dédié, SaaS | API + Worker, cellule Kubernetes | une base PostgreSQL par dossier | Keycloak |
| 4. Poste relié | Application MAUI avec `lettra.json` | réplique chiffrée du dossier du serveur, synchronisée | compte local, Keycloak (PKCE) pour la synchronisation |

**Serveur et version web (mode 2).** L'API exige un realm Keycloak (`Lettra:Identity:Oidc:Authority`, MFA
obligatoire) : démarrez d'abord Keycloak comme l'explique [deploy/keycloak/README.md](deploy/keycloak/README.md),
créez-y un utilisateur dans l'organisation du cabinet, puis :

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Hosts/Lettra.Api   # https://localhost:7180
```

En développement, l'API ouvre un dossier SQLite de démonstration et la plateforme agréée simulée
(`appsettings.Development.json`). Sans Keycloak, elle s'arrête au démarrage en nommant le réglage manquant.

**Poste relié (mode 4).** Un fichier `lettra.json` dans le dossier de données de l'application relie le poste :

```json
{ "Lettra": { "Server": { "BaseAddress": "https://lettra.cabinet.example/", "Authority": "https://sso.cabinet.example/realms/lettra", "DossierId": "…" } } }
```

Le poste crée alors la réplique du dossier du serveur, envoie ses pièces au retour du réseau (en demandant
« Maintenant / Plus tard / Détail » selon la préférence) et présente les conflits à trancher.

### Structure

```
src/
  Core/            Calculations (arrondi unique, conformité), Contracts (API publique), Domain, Application (cas d'usage, ports),
                   Speech (dictée et commandes vocales)
  Infrastructure/  Persistence (EF Core) et un projet de migrations par provider, Catalog, Identity.Local, Identity.Oidc,
                   External (SIRENE, VIES, e-mail), Observability, Speech (serveur whisper.cpp), Speech.OnDevice (Whisper.net)
  Modules/         Supervision (alertes), Exports (FEC, Factur-X, UBL, CII, plateforme agréée), Sync (Client, Server)
  Composition/     Bootstrap : AddLettra(), la composition selon le mode ; DemoData (jeu de démonstration « Cabinet Démo »)
  Presentation/    UI.Abstractions (ports), UI.Shared (toute l'UI Blazor), UI.Gateway.Http, UI.Gateway.InProcess, Printing (PDF)
  Hosts/           Api (Minimal APIs, BFF, sert le WASM), Web (WASM), App.Maui (Blazor Hybrid), Worker (migrations, escalade, intégrité,
                   export FEC, outbox, échéances fiscales, vue cabinet, factures récurrentes, relances ; sans Worker,
                   l'API fait toutes ces passes sauf les vagues de migration : chaque base y est migrée à sa première ouverture)
  Ai/              Couche IA (Paperasse épinglé, pseudonymisation, orchestrateur, moteurs Claude UE et local)
tests/             un projet par couche ou module, dont Api (bout en bout, parité HTTP / in-process) et Architecture
deploy/            démo Docker, Helm, OpenTofu, Argo CD, cellules, Keycloak, observabilité, tests de déploiement (k6, Grafana,
                   kind, ZAP), essai de panne local (deploy/local/essai-panne.sh)
```

### Règles tenues par les tests

| Règle | Test |
|---|---|
| Dépendances entre couches ; le domaine et l'UI ne voient jamais EF | `LayerDependencyTests` |
| Chaque cas d'usage déclare sa permission | `UseCaseRulesTests` |
| Une requête ne touche que la base du dossier ouvert (3 providers) | `Une_base_par_dossier` |
| Même résultat par HTTP et en in-process | `GatewayParityTests` |
| Le parcours de ce README fonctionne | `DemoWalkthroughTests` |
| Numérotation sans trou ni doublon sous concurrence | `Numerotation_continue_sous_concurrence` |
| Chaque code d'erreur est traduit en FR et EN | `ErrorCodeAndResourceTests` |
| Aucune donnée de dossier dans les journaux | `NoDossierDataInLogsTests` |
| Factur-X conforme PDF/A-3b et EN 16931 | CI : veraPDF et validateurs EN 16931 |

### Conventions

- Échecs métier en valeurs (`Result`, `Result<T>`), jamais en exceptions ; le serveur ne renvoie que des codes,
  traduits par module (`ErrorCodes.<Module>.cs`, `Localization/Modules/<Module>.resx`).
- Médiateur maison : journalisation, dossier, autorisation, validation.
- Style vérifié à la compilation : les règles IDE de `.editorconfig` sont des avertissements et `TreatWarningsAsErrors`
  (`Directory.Build.props`) en fait des erreurs ; `dotnet format Lettra.Ci.slnf --severity warn` corrige avant de
  pousser ([docs/qualite-du-code.md](docs/qualite-du-code.md)).
- Avertissements bloquants, paquets centralisés (`Directory.Packages.props`), identifiants Guid v7, montants
  `decimal(18,4)`, dates UTC.
- Migrations : même identifiant dans les trois providers ; la PR fusionnée en second régénère la sienne.
- Tests nommés en français (`Une_entreprise_francaise_sans_siren_est_refusee`).

### CI

`.github/workflows/ci.yml` : build et tests Linux (Testcontainers PostgreSQL, SQL Server, Keycloak) sous couverture
de code, qui échoue sous 88 % des lignes ou 78 % des branches (rapport par assembly dans le résumé du job ; détails et
commande locale : [docs/qualite-du-code.md](docs/qualite-du-code.md)), conformité Factur-X, applications MAUI construites et lancées (Windows ; Android sur émulateur ; simulateur iOS ; macOS, non
bloquant tant qu'il n'est pas signé) avec leurs artefacts, captures du design et audit d'accessibilité (Playwright,
axe-core), gitleaks, SBOM CycloneDX, CodeQL (dépôt public), avis de tiers, images signées (cosign) sur `main`. La
charge de travail MAUI est épinglée (`MAUI_WORKLOAD_VERSION`, 10.0.401) : un jeu plus récent publié sur nuget.org ne
casse pas la CI du jour au lendemain. `.github/workflows/deploy.yml` valide l'IaC, les règles et le routage des
alertes, l'essai de panne (API arrêtée, astreinte appelée en S1), Keycloak dans une cellule kind, puis la démo Docker :
parcours navigateur (Keycloak, API, PWA), charge k6, télémétrie dans Grafana (chaque vérification en échec devient une
annotation du job) et scan de sécurité ZAP. Autres workflows : `serveur-commun-android.yml` (navigateur et application
Android sur les mêmes données, émulateur), `voix.yml` (Whisper réel sur Linux, Windows et macOS),
`pr-environment.yml` (image d'une PR étiquetée « env:pr », déployée par Argo CD dans la cellule de recette). Sur une PR, un nouveau push annule l'exécution
en cours ; sur `main`, chaque fusion garde son exécution complète.

### Assistant IA

Désactivé par défaut. Il explique et rédige, ne valide ni n'émet rien, ne voit que des données pseudonymisées et
passe toujours par l'orchestrateur du serveur. Le moteur « Claude UE » exige un point d'accès en région UE (Amazon
Bedrock, Google Vertex AI) et refuse de démarrer sinon. Réglages : section `Lettra:Ai` ; la clé ne va jamais dans un
fichier versionné (`Lettra__Ai__Claude__ApiKey`).

## Licence

MIT, voir [LICENSE](LICENSE). Composants tiers : [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
