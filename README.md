# Lettra

Logiciel de devis et de factures pour les cabinets d'experts-comptables : une base par dossier client, la facture
électronique (Factur-X, UBL), la transmission à une plateforme agréée et le travail hors ligne.

> **Prototype interne.** Lettra se présente aujourd'hui en interne, sur **données fictives uniquement**. La plateforme
> agréée est **simulée** : rien ne quitte l'ordinateur. Ne saisissez aucune donnée réelle de client.

- [Choisir sa façon d'essayer](#choisir-sa-façon-dessayer) : navigateur, Windows, Android, iPhone, Mac
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
| Simulateur iOS (Mac avec puce Apple et Xcode) | [Application iOS pour le simulateur](#simulateur-ios-mac) | **Vérifiée** : lancement, application toujours ouverte après 30 s, premier dossier chiffré créé. |
| iPhone ou iPad réel | Application native | **Pas encore possible** : il faut un Mac et un compte Apple Developer pour la signer. Utiliser le navigateur. |
| Mac (macOS) | [Application macOS](#macos-mac-catalyst) | Construite, mais **s'arrête à la création du premier dossier** tant qu'elle n'est pas signée par Apple. Utiliser le navigateur. |
| Linux, en application native | Pas de version native (.NET MAUI ne vise pas Linux) | Utiliser le navigateur. |

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

1. Ouvrir **http://localhost:8080/** dans Chrome, Edge ou Firefox, puis **Se connecter**.
2. Identifiant **`demo`**, mot de passe **`demo-lettra-demo`** (comptes et secrets fictifs, réservés à la démonstration).
3. À la première connexion, enregistrer le second facteur : scanner le QR code avec une application TOTP qui accepte
   SHA-256 (FreeOTP, Aegis, Google Authenticator récent, Bitwarden, 1Password ; **pas Microsoft Authenticator**), puis
   saisir le code. Keycloak redemande une fois le mot de passe et le code, c'est normal.
4. Lettra s'ouvre sur le « Dossier de démonstration ». Suivre ensuite le [pas à pas](#premier-usage-pas-à-pas) à
   partir de l'étape 1 : la connexion remplace la phrase secrète du poste.

`docker compose -f deploy/demo/compose.yaml down` arrête la démonstration (les données restent) ; ajouter `-v` remet
tout à zéro. Détails, ports et télémétrie : [deploy/demo/README.md](deploy/demo/README.md).

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

#### Voir la télémétrie dans Grafana

La même démonstration, avec ses traces, métriques et journaux dans Grafana (collecteur filtrant, Prometheus, Tempo et
Loki, comme dans une cellule) :

```bash
docker compose -f deploy/demo/compose.yaml -f deploy/demo/compose.observabilite.yaml up -d --build --wait
```

Puis **http://localhost:3000/**, sans connexion : trois tableaux de bord dans le dossier « Lettra », **Activité**
(requêtes, latence, erreurs, outbox, synchronisations, connexions, assistant), **Métier** (devis, factures, avoirs,
transmissions, FEC, en nombres seulement) et **Objectifs de service** (disponibilité, latence, alertes). Pour remplir
les courbes, dix minutes d'activité fictive avec k6, une fois la session ouverte
([commandes complètes](deploy/demo/README.md#remplir-les-courbes--activité-de-démonstration-k6)) :

```bash
LETTRA_SESSION_COOKIE="$(cat /tmp/lettra-k6/session-cookie.txt)" k6 run -e LETTRA_PROFIL=activite deploy/tests/k6/fumee.js
```

Détails : [Voir la télémétrie dans Grafana](deploy/demo/README.md#voir-la-télémétrie-dans-grafana).

> La CI (job « Démo — télémétrie dans Grafana ») démarre cette pile, vérifie par l'API de Grafana les sources et les
> tableaux de bord, puis, après deux minutes d'activité k6, que chaque panneau a des données (sauf les deux panneaux
> PostgreSQL, vides avec SQLite), qu'un exemplaire mène à une trace et à ses journaux, et que les alertes de la cellule
> sont visibles. L'affichage dans un navigateur n'est pas vérifié.

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

C'est prêt. Lettra a créé un cabinet local et un premier dossier chiffré, « Dossier principal ».

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

> Vérifié par la CI (job « MAUI iOS (simulateur) et macOS (Mac Catalyst) ») : l'application s'installe, est
> toujours ouverte après 30 secondes et a créé son premier dossier chiffré. Captures et journaux dans l'artefact
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

## Premier usage, pas à pas

Le parcours va de la fiche de l'entreprise au FEC, en une quinzaine de minutes, dans l'application installée ou dans
le navigateur : les écrans sont les mêmes. Les noms et numéros proposés sont **fictifs** et acceptés par les contrôles
de Lettra ; copiez-les tels quels.

Le menu, à gauche : **Devis**, **Factures**, **Avoirs**, **Clients**, **Fournisseurs**, **Catalogue**, **FEC**,
**Alertes**, **Entreprise**. Au clavier, **Ctrl+K** ouvre la palette de commandes (« Nouveau devis », « Nouvelle facture »).

### 1. La fiche de votre entreprise

Menu **Entreprise**. Ces mentions figurent sur chaque facture ; sans elles, Lettra refuse de valider.

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

Menu **Clients**. Le formulaire « Nouveau client » est en haut de la page. Saisissez tout à la main (la recherche
dans l'annuaire SIRENE ne sert qu'aux vraies entreprises) :

- Type de client : **Entreprise**
- Nom ou raison sociale : **Boulangerie Exemple**
- SIRET : **90000002700012**
- Adresse : **3 place du Marché**, **69002**, **Lyon**

Cliquez sur **Créer le client**.

### 3. Un devis

Menu **Devis**, **Nouveau devis**.

1. Client : **Boulangerie Exemple** ; Objet : *Accompagnement comptable*.
2. Dans la grille **Lignes**, une ligne : Désignation *Tenue comptable mensuelle*, Qté **3**, PU HT **250**.
   **Inser** ajoute une ligne, **Entrée** ou **Tab** passe à la cellule suivante, **F4** ouvre une liste de choix.
   Le brouillon s'enregistre tout seul.
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

1. **Exporter le FEC** : le fichier `900000019FEC20261231.txt` (SIREN, « FEC », date de clôture) est téléchargé avec
   les écritures de vente de la facture et de l'avoir.
2. Le **Rapport du validateur** s'affiche : « Fichier conforme. », le nombre de lignes et d'écritures, les totaux
   débit et crédit, égaux.
3. **Contrôler un fichier** passe au même validateur un FEC venu d'un autre logiciel ; chaque constat indique la
   ligne, la colonne et l'écriture concernées.

Sans SIREN dans la fiche entreprise, l'export le demande au lieu de produire un fichier.

### Et ensuite

- **Catalogue** : les articles et prestations, saisis au clavier dans une grille ; facultatif, une ligne de facture
  peut se saisir librement.
- **Alertes** : le centre d'alertes techniques (chaîne anti-fraude d'un dossier, trou de numérotation). Il ne montre
  aucune donnée de client.
- **Verrouiller** (en haut) : ferme la session ; la phrase secrète la rouvre.
- **Langue** : l'écran suit la langue de l'appareil ou du navigateur (français ou anglais). Chaque client et chaque PDF peuvent avoir la
  leur (« Langue des devis et factures imprimés », « Langue du PDF »).

---

## Ce que la démo ne fait pas encore

| Fonction | État |
|---|---|
| Assistant IA | Désactivé par défaut, sans écran ; il ne valide ni n'émet jamais rien. |
| Vraie plateforme agréée | En attente d'un partenaire ; la démo utilise la plateforme simulée. |
| Serveur du cabinet en production | La démonstration navigateur tourne sur un poste avec Docker ; un vrai serveur du cabinet ou une cellule SaaS demande Keycloak, des certificats et une infrastructure (voir plus bas). |
| Poste relié à un serveur, synchronisation | Prête dans l'application (fichier `lettra.json`), mais demande un serveur Lettra et Keycloak. Sur un poste isolé, la page Synchronisation indique « Ce poste ne synchronise pas ». |
| Envoi des devis et factures par e-mail | Non : on télécharge le PDF ou le Factur-X. |
| Applications signées | Non : avertissement de Windows au premier lancement, APK Android installé à la main, application macOS arrêtée par le Trousseau, pas de version pour iPhone réel. |

---

## En cas de problème

| Symptôme | Que faire |
|---|---|
| Navigateur : la page ne répond pas | Attendre quelques secondes après `up --wait`, puis recharger ; `docker compose -f deploy/demo/compose.yaml ps` doit montrer les services « healthy ». |
| Navigateur : le code du second facteur est refusé | L'application TOTP doit accepter SHA-256 (pas Microsoft Authenticator) et l'heure du téléphone être à l'heure. Pour réenrôler : `down -v`, puis redémarrer. |
| « Windows a protégé votre ordinateur » | Normal pour un prototype non signé : **Informations complémentaires**, puis **Exécuter quand même**. |
| Rien ne s'ouvre, ou une erreur sur des fichiers bloqués | Le zip n'a pas été débloqué : supprimez le dossier extrait, faites l'étape 2 (Propriétés, **Débloquer**), puis extrayez à nouveau. |
| Fenêtre blanche | Le composant WebView2 manque (vieux Windows 10) : installez « Microsoft Edge WebView2 Runtime » depuis le site de Microsoft, puis relancez. |
| « Phrase secrète incorrecte. » / « Trop d'essais » | Après plusieurs erreurs, le poste se verrouille quelques minutes. Une phrase oubliée ne se récupère pas : voir la ligne suivante. |
| Repartir de zéro (phrase oubliée, nouvelle démo) | Fermez Lettra et supprimez son dossier de données (ci-dessous). Les données de démonstration sont perdues ; le prochain lancement recrée tout. |
| « Remplissez d'abord la fiche de l'entreprise émettrice. » | Étape 1 : menu **Entreprise**. |
| Une mention reste « Bloquant » | Cliquez dessus dans **Conformité de la pièce** : Lettra ouvre l'écran à compléter (fiche entreprise ou client). |

**Où sont les données.** Sous Windows, tout est sur le PC, dans le dossier de données de l'application :
`%LOCALAPPDATA%\Lettra\Lettra.App.Maui\Data` (à coller dans la barre d'adresse de l'Explorateur). On y trouve le compte du poste (`compte-local.json`) et, dans `dossiers`, une base chiffrée par
dossier. La clé du cabinet est dans le coffre de Windows : copier ces fichiers sur un autre PC ne les rend pas
lisibles. Supprimer ce dossier remet Lettra à zéro. Sous Android et iOS, les données sont dans l'application :
la désinstaller (ou, sous Android, **Effacer les données**) la remet à zéro. Dans le navigateur, elles sont sur le
serveur de démonstration (`down -v` les efface).

**Mettre à jour la démo.** Sous Windows, fermez Lettra, téléchargez le nouvel artefact et remplacez le dossier
extrait : vos données, rangées ailleurs, sont conservées. Sous Android, `adb install -r` (ou réinstaller l'APK) garde
les données. Dans le navigateur, `git pull` puis la même commande `up -d --build --wait`.

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
  Core/            Calculations (arrondi unique, conformité), Contracts (API publique), Domain, Application (cas d'usage, ports)
  Infrastructure/  Persistence (EF Core) et un projet de migrations par provider, Catalog, Identity.Local, Identity.Oidc,
                   External (SIRENE, VIES), Observability
  Modules/         Supervision (alertes), Exports (FEC, Factur-X, UBL, CII, plateforme agréée), Sync (Client, Server)
  Composition/     Bootstrap : AddLettra(), la composition selon le mode
  Presentation/    UI.Abstractions (ports), UI.Shared (toute l'UI Blazor), UI.Gateway.Http, UI.Gateway.InProcess, Printing (PDF)
  Hosts/           Api (Minimal APIs, BFF, sert le WASM), Web (WASM), App.Maui (Blazor Hybrid), Worker (migrations, escalade)
  Ai/              Couche IA (Paperasse épinglé, pseudonymisation, orchestrateur, moteurs Claude UE et local)
tests/             un projet par couche ou module, dont Api (bout en bout, parité HTTP / in-process) et Architecture
deploy/            Helm, OpenTofu, Argo CD, Keycloak, observabilité, essai de panne local (deploy/local/essai-panne.sh)
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
- Avertissements bloquants, paquets centralisés (`Directory.Packages.props`), identifiants Guid v7, montants
  `decimal(18,4)`, dates UTC.
- Migrations : même identifiant dans les trois providers ; la PR fusionnée en second régénère la sienne.
- Tests nommés en français (`Une_entreprise_francaise_sans_siren_est_refusee`).

### CI

`.github/workflows/ci.yml` : build et tests Linux (Testcontainers PostgreSQL, SQL Server, Keycloak), conformité
Factur-X, applications MAUI construites et lancées (Windows ; Android sur émulateur ; simulateur iOS ; macOS, non
bloquant tant qu'il n'est pas signé) avec leurs artefacts, gitleaks, SBOM CycloneDX,
CodeQL, avis de tiers, images signées (cosign) sur `main`. `.github/workflows/deploy.yml` valide l'IaC, les règles
d'alerte et l'essai de panne (API arrêtée, astreinte appelée en S1).

### Assistant IA

Désactivé par défaut. Il explique et rédige, ne valide ni n'émet rien, ne voit que des données pseudonymisées et
passe toujours par l'orchestrateur du serveur. Le moteur « Claude UE » exige un point d'accès en région UE (Amazon
Bedrock, Google Vertex AI) et refuse de démarrer sinon. Réglages : section `Lettra:Ai` ; la clé ne va jamais dans un
fichier versionné (`Lettra__Ai__Claude__ApiKey`).

## Licence

MIT, voir [LICENSE](LICENSE). Composants tiers : [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
