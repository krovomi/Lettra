# Démonstration navigateur (Windows, macOS, Linux)

Lettra en mode 2 (réseau local) dans le navigateur : Keycloak (connexion, MFA), l'API, qui sert l'application
Blazor installable (PWA), le serveur whisper.cpp de la dictée (conteneur `whisper`) et MailPit, qui reçoit les
e-mails de la démo (conteneur `mailpit`). Seul **Docker** est nécessaire : Docker Desktop sous Windows et macOS, Docker Engine et
son extension Compose sous Linux. Pas de SDK .NET : l'image de l'API est construite depuis le `Dockerfile` du dépôt.

Tout est **fictif et réservé à la démonstration** : cabinet, utilisateur, secrets de `.env`. Ne jamais réutiliser
ces valeurs ni ce montage en production (Keycloak en `start-dev`, base H2, HTTP sur localhost).

## Démarrer et arrêter

Depuis la racine du dépôt, même commande dans PowerShell, l'invite de commandes, bash ou zsh :

```bash
docker compose -f deploy/demo/compose.yaml up -d --build --wait     # démarrer (premier build : quelques minutes)
docker compose -f deploy/demo/compose.yaml down                     # arrêter, les données restent
docker compose -f deploy/demo/compose.yaml down -v                  # arrêter et tout effacer (saisies comprises)
```

Mettre à jour : `git pull`, puis la même commande `up -d --build --wait`. Les données restent dans les volumes
Docker (ci-dessous) ; seul `down -v` les efface.

Puis ouvrir **http://localhost:8080/** dans Chrome, Edge ou Firefox (l'application peut mettre quelques secondes à
répondre après le démarrage).

| Service | Adresse | Usage |
|---|---|---|
| Lettra (API + application) | http://localhost:8080/ | Application, `/health` |
| Keycloak | http://localhost:8081/ | Connexion ; console d'administration : `admin` / `admin-demo` |
| Whisper (dictée) | aucune : seule l'API l'appelle (`http://whisper:8080/`) | Transcription de la voix, sur le poste |
| MailPit (e-mails) | http://localhost:8025/ | Boîte de réception locale : relances et invitations envoyées par la démo. Aucun e-mail ne sort du poste. |

Les ports ne sont ouverts que sur le poste (127.0.0.1). Pour en changer : `LETTRA_DEMO_PORT`,
`LETTRA_DEMO_KEYCLOAK_PORT` et `LETTRA_DEMO_MAILPIT_PORT` dans `.env`.

## Premier usage

1. Sur l'écran de session, **Se connecter** : Keycloak s'ouvre.
2. Identifiant **`demo`**, mot de passe **`demo-lettra-demo`** (Camille Démo, rôles manager et administration du « Cabinet de démonstration »).
3. **Enrôlement du second facteur**, imposé à la première connexion : scanner le QR code avec une application TOTP qui
   accepte SHA-256 (FreeOTP, Google Authenticator récent, Aegis, Bitwarden, 1Password… ; pas Microsoft Authenticator),
   ou saisir la clé affichée sous « Impossible de scanner ? », puis entrer le code à 6 chiffres.
4. Keycloak redemande **une fois** le mot de passe puis le code : le jeton de l'enrôlement ne prouve pas le second
   facteur, Lettra exige une authentification complète.
5. Le tableau de bord s'affiche, sur le « Dossier de démonstration », rempli d'un an d'activité fictive au premier démarrage (README racine). Deux autres dossiers du cabinet (« Bâti
   Rénov' Lemoine », « Négoce Méditerranée Distribution ») sont créés une seule fois, y compris dans une démo déjà
   remplie, pour la vue cabinet et le sélecteur de dossier. La vue cabinet est calculée 15 s après le démarrage
   (`Lettra__CabinetOverview__InitialDelay`) et les indicateurs sont recollectés chaque minute
   (`Lettra__Kpi__RefreshInterval`). Les connexions suivantes : mot de passe + code.

## Dictée et commandes vocales (conteneur whisper)

Le conteneur `whisper` est le serveur whisper.cpp v1.8.7, construit depuis `build/voix/whisper-server` (la même image
que le workflow « Voix » de la CI ; image de base Debian épinglée par empreinte, source vérifiée par son commit).
L'API lui envoie l'audio par le réseau Docker : rien ne sort du poste, rien n'est enregistré ni journalisé.

- **Premier démarrage** : le conteneur télécharge le modèle `ggml-base.bin` (142 Mio) depuis le dépôt public de
  whisper.cpp, vérifie son empreinte SHA-1 publiée, puis le charge. Compter une à quelques minutes selon la connexion.
  `up --wait` ne l'attend pas et Lettra s'ouvre tout de suite ; d'ici là, la transcription répond 503
  (`Speech.EngineUnavailable`). Sans Internet, le conteneur réessaie toutes les 30 secondes, sans gêner le reste.
- **Démarrages suivants** : le modèle est déjà dans le volume `lettra-demo_whisper-modeles`, le serveur est prêt en
  quelques secondes.
- **Vérifier** : `docker compose -f deploy/demo/compose.yaml exec whisper curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:8080/`
  affiche `200` quand le modèle est chargé ; `docker compose -f deploy/demo/compose.yaml logs whisper` montre le
  téléchargement. Côté Lettra, `GET /api/v1/voix/statut` (connecté, avec les en-têtes `X-Dossier` et
  `X-Lettra-Csrf: 1`) répond `"enabled": true, "engine": "whisper-serveur"` : la voix est configurée, même pendant le
  téléchargement.
- Réglages de l'API (`compose.yaml`) : `Lettra__Speech__Engine: WhisperServer` et
  `Lettra__Speech__WhisperServer__Endpoint: http://whisper:8080/`. Une adresse publique serait refusée au démarrage.

Vérifié à chaque CI : le job « Démo — charge k6 » attend le modèle, lit le statut avec une vraie session et envoie un
son à l'API, qui le fait transcrire par ce conteneur ; le job « Démo navigateur » vérifie qu'après une mise à jour le
modèle n'est pas retéléchargé. Dans les écrans, la voix s'utilise par le bouton micro en haut, le micro des champs
ou F8 maintenue (voir `docs/aide-a-la-saisie.md`).

## Installer l'application (PWA)

- **Chrome, Edge (Windows, macOS, Linux)** : icône d'installation dans la barre d'adresse (ou menu ⋮ → *Installer
  Lettra*). L'application s'ouvre ensuite dans sa propre fenêtre. `http://localhost` est un contexte sécurisé : le
  cookie de session (`__Host-`, `Secure`) et le service worker y fonctionnent sans certificat.
- Le service worker ne met en cache que l'enveloppe de l'application (fichiers statiques) : jamais les réponses de
  `/api/` ni de `/bff/`, donc aucune donnée de dossier.
- Firefox sur ordinateur : installation absente ou expérimentale selon la version ; Safari (macOS) non vérifié.
- **Console du navigateur** : elle doit rester vide d'erreurs sur tous les écrans, en ligne comme hors ligne. Le
  service worker ne répond qu'aux navigations et aux fichiers de l'enveloppe ; tout autre fichier passe par le réseau
  sans lui. Sans session, `/bff/user` répond 200 avec `isAuthenticated: false` et aucun écran ne lit le dossier avant
  la connexion. Hors ligne, l'application n'envoie pas de requête à l'API : les écrans affichent « serveur
  injoignable » et les devis et factures continuent dans les brouillons de l'appareil.
- **Mise à jour** : une nouvelle version publiée (`git pull` puis `up --build`) prend la main dès qu'elle est en cache,
  sans attendre la fermeture des onglets (`skipWaiting` et `clients.claim` dans `service-worker.published.js`).
  `pwa.js` recharge alors la page si elle est ouverte depuis moins de dix secondes et que rien n'a été saisi ; sinon il
  affiche « Nouvelle version de Lettra disponible » avec **Recharger**. Il cherche aussi une nouvelle version au retour
  sur l'onglet, au retour du réseau et tous les quarts d'heure. La CI le vérifie à chaque mise à jour de la démo
  (`e2e/mise-a-jour.mjs`, avec un onglet resté ouvert et un onglet ouvert juste après).
- Si une erreur « Uncaught (in promise) TypeError: Failed to fetch at fromShell » apparaît encore, c'est l'ancien
  service worker : fermer tous les onglets de Lettra puis rouvrir (ou DevTools → *Application* → *Service workers* →
  *Unregister*).

## Depuis un téléphone (même Wi-Fi) : profil HTTPS

Hors de `localhost`, le cookie de session et le service worker exigent HTTPS avec un certificat **approuvé** par le
téléphone. Le profil `compose.lan.yaml` ajoute Caddy, qui sert Lettra en `https://<hôte>:8443` et Keycloak en
`https://<hôte>:8444` avec sa propre autorité de certification (locale à la démonstration).

1. Trouver l'adresse IP du poste sur le Wi-Fi (`ipconfig` sous Windows, `ipconfig getifaddr en0` sous macOS,
   `hostname -I` sous Linux), par exemple `192.168.1.20`, et la reporter dans `.env` : `LETTRA_DEMO_HOST=192.168.1.20`.
2. Démarrer avec les deux fichiers :

   ```bash
   docker compose -f deploy/demo/compose.yaml -f deploy/demo/compose.lan.yaml up -d --build --wait
   ```

3. Autoriser les ports 8443, 8444 et 8079 dans le pare-feu du poste si nécessaire (Windows le demande au premier lancement).
4. Sur le téléphone, télécharger le certificat racine : **http://192.168.1.20:8079/lettra-demo-ca.crt**, puis l'approuver :
   - **Android** : Paramètres → Sécurité → Chiffrement et identifiants → Installer un certificat → *Certificat CA*
     (libellé variable selon le constructeur). Chrome approuve alors la démonstration, ainsi que l'APK de
     démonstration de Lettra ; une version construite avec `-p:LettraDemo=false` n'accepte que les autorités
     d'Android et refuse ce certificat.
   - **iOS / iPadOS** : ouvrir le lien dans Safari, *Autoriser* le profil ; Réglages → Général → VPN et gestion de
     l'appareil → installer le profil ; puis Réglages → Général → Informations → **Réglages des certificats** →
     activer la confiance totale pour « Caddy Local Authority ».
5. Ouvrir **https://192.168.1.20:8443/** (aussi sur le poste : dans ce profil, c'est la seule adresse de retour de
   Keycloak). Installer la PWA : Chrome Android → menu ⋮ → *Installer l'application* ; Safari iOS → Partager →
   *Sur l'écran d'accueil*.

En HTTPS, l'API envoie l'en-tête HSTS (un an) : ignoré pour une adresse IP ; avec un nom de poste
(`LETTRA_DEMO_HOST=mon-pc.local`), ce nom reste ensuite en HTTPS dans le navigateur. `http://localhost:8080` ne le
reçoit jamais.

Retirer le certificat du téléphone après la démonstration. Changer d'adresse (autre Wi-Fi, nouvelle IP) ou revenir à
`localhost` ne demande aucune remise à zéro : Keycloak garde une base par adresse publique (l'enrôlement TOTP est
donc à refaire la première fois sur chaque adresse) ; les données Lettra sont communes.

## Données et remise à zéro

| Volume Docker | Contenu |
|---|---|
| `lettra-demo_lettra-donnees` | Catalogue et dossier SQLite, clés de session de l'API |
| `lettra-demo_keycloak-donnees` | Base Keycloak (utilisateur, enrôlement TOTP), une par adresse publique |
| `lettra-demo_caddy-donnees` | Autorité de certification et certificats du profil HTTPS |
| `lettra-demo_whisper-modeles` | Modèle de la dictée (`ggml-base.bin`, 142 Mio) ; aucune donnée de dossier |

Les noms viennent du projet Compose `lettra-demo` (`name:` de `compose.yaml`) : lancer la démo avec `-p` ou
`COMPOSE_PROJECT_NAME` en change le nom, donc les volumes, et l'application repart d'un dossier neuf.

- **Mise à jour** (`git pull`, puis `up -d --build --wait`) : l'API est reconstruite et recréée, les volumes restent ;
  le catalogue et le dossier sont migrés sur place, et le jeu de démonstration n'est pas rechargé dans un dossier qui a
  déjà des pièces. Vérifié à chaque CI (job « Démo navigateur » : nouvelle image de l'API, mêmes volumes).
- **Tout effacer** : `down -v` (avec les mêmes fichiers `-f` qu'au démarrage) supprime ces volumes : saisies, jeu de
  démonstration, enrôlement TOTP. Au prochain démarrage, le realm est réimporté et le dossier rechargé.
- **Remettre à zéro le modèle de la voix seulement** : `down`, `docker volume rm lettra-demo_whisper-modeles`, puis
  `up -d --wait`. Cela ne fait que retélécharger le modèle (142 Mio) ; aucune saisie n'est touchée. `down -v` le
  supprime aussi.
- **Réenrôler le second facteur seulement** : `down`, `docker volume rm lettra-demo_keycloak-donnees`, puis `up -d --wait`.
  Les données Lettra restent (l'identifiant de l'utilisateur `demo` est fixe).
- **Anciennes bases Keycloak** : une mise à jour qui corrige le realm (`deploy/keycloak/realm-lettra.json`) le fait
  réimporter dans une base neuve (`base-<adresse>-<empreinte du realm>`, voir `keycloak/start.sh`) ; l'enrôlement TOTP
  est alors à refaire une fois, les données Lettra restent. Les anciennes bases (des dossiers) restent dans le volume,
  sans effet. Pour les voir, la plus récente en premier (démo arrêtée) :
  `docker run --rm -v lettra-demo_keycloak-donnees:/d busybox ls -lt /d`, puis supprimer une ancienne avec
  `docker run --rm -v lettra-demo_keycloak-donnees:/d busybox rm -r /d/base-http---localhost-8080-<ancienne empreinte>`.
  Aucune suppression automatique. La remise à zéro du second facteur ci-dessus les supprime aussi.
- Docker Desktop (« Clean / Purge data », nettoyage des volumes) et `docker volume prune -a` suppriment aussi les volumes
  d'une démo arrêtée. Pour libérer de la place, `docker builder prune` suffit (cache de construction de l'image).
- **Sauvegarder** (démo arrêtée) :
  `docker run --rm -v lettra-demo_lettra-donnees:/donnees -v "${PWD}:/sauvegarde" busybox tar czf /sauvegarde/lettra-donnees.tgz -C /donnees .`

## Option : télémétrie OpenTelemetry (collecteur seul)

La démonstration seule n'exporte rien. Pour voir ce que l'API enverrait au collecteur d'une cellule, ajouter
`compose.otel.yaml` aux mêmes commandes (pour les tableaux de bord, voir plutôt « Voir la télémétrie dans Grafana »
ci-dessous) :

```bash
docker compose -f deploy/demo/compose.yaml -f deploy/demo/compose.otel.yaml up -d --build --wait
docker compose -f deploy/demo/compose.yaml -f deploy/demo/compose.otel.yaml logs -f otel-collector
curl -s http://localhost:8889/metrics | grep lettra_use_case
```

- L'API exporte traces, métriques et journaux en OTLP (gRPC) vers `otel-collector`, qui applique la configuration de
  base d'une cellule (`deploy/helm/lettra-observability/files/collector.yaml`) : attributs interdits supprimés,
  exceptions réduites à leur type, e-mail, IBAN, TVA, SIREN et SIRET masqués.
- `otel/collecteur-demo.yaml` y ajoute seulement deux sorties locales : les métriques au format Prometheus sur
  `http://localhost:8889/metrics`, et le détail de chaque signal filtré dans le journal du collecteur. Ce détail est
  réservé à la démonstration (données fictives) ; une cellule n'en écrit qu'un résumé.
- Ports publiés sur `127.0.0.1` seulement : 4317 (OTLP gRPC), 4318 (OTLP HTTP), 8889 (métriques). Variables
  `LETTRA_DEMO_OTLP_GRPC_PORT`, `LETTRA_DEMO_OTLP_HTTP_PORT` et `LETTRA_DEMO_METRIQUES_PORT` en cas de conflit.

## Voir la télémétrie dans Grafana

Pour voir ce que fait Lettra pendant la démonstration (requêtes, latence, erreurs, ventes, transmissions, alertes),
ajouter `compose.observabilite.yaml` aux commandes de la démonstration :

```bash
docker compose -f deploy/demo/compose.yaml -f deploy/demo/compose.observabilite.yaml up -d --build --wait
```

Puis ouvrir **http://localhost:3000/** : Grafana s'ouvre sans connexion, en lecture seule, sur « Lettra · Activité ».
Les six tableaux de bord sont dans le dossier **Lettra** (menu *Dashboards*), et le bandeau de chacun mène aux autres.

Les tableaux « Pilotage du cabinet », « Pilotage d'un dossier » et « Administration » lisent les indicateurs que l'API
expose sur `/internal/kpi/metrics` (tâche Prometheus `lettra-kpi`, jeton fictif `demo-indicateurs-fictif-2026` présent
dans `observabilite/prometheus.yml` et dans `compose.observabilite.yaml`). Sans jeton réglé, la route n'existe pas.
Détails et limites (droits par dossier, comptes Grafana) : [docs/indicateurs-grafana.md](../../docs/indicateurs-grafana.md).

| Tableau de bord | Ce qu'il montre |
|---|---|
| **Lettra · Activité** | Requêtes par route, latence p50/p95/p99, réponses en erreur par code (401, 403, 429, 5xx), cas d'usage par issue et leur durée, file de l'outbox (en attente, mis de côté, âge du plus ancien) et ses envois, synchronisations (durée, issue), connexions réussies et refusées, appels à l'assistant par issue, traces récentes (Tempo) et journaux (Loki) de l'API. |
| **Lettra · Métier** | Devis créés, validés, acceptés ou refusés, factures créées, validées et émises, avoirs, transmissions à la plateforme agréée par statut, exports FEC. **Des nombres seulement** : aucun montant, client, dossier ni personne. |
| **Lettra · Objectifs de service** | Le tableau de bord de la cellule : disponibilité et budget d'erreur sur 30 jours, latence p95 des lectures, cas d'usage, et la liste des alertes de la cellule. Les deux panneaux PostgreSQL restent vides (la démonstration utilise SQLite). |

**D'un pic à sa cause** : sur une courbe de latence, les points sont des *exemplaires* ; survoler l'un d'eux puis
**Voir la trace** ouvre la requête mesurée dans Tempo. Dans la trace, le bouton des journaux d'un span ouvre les lignes de
Loki de la même requête ; dans Loki, **Voir la trace** fait le chemin inverse. Explore (menu de gauche) permet aussi de
chercher librement dans les trois sources.

**Alertes** : Prometheus évalue les règles de la cellule (`deploy/helm/lettra-observability/files/rules`), visibles dans
Grafana (*Alerting* → *Alert rules*, et en bas de « Objectifs de service »). Aucune notification ne part (pas
d'Alertmanager). Dans la démonstration, `Watchdog` est toujours active (la chaîne tourne) et
`LettraWorkerIndisponible` aussi : la démonstration n'a pas de Worker, l'API reprend son rôle.

### Remplir les courbes : activité de démonstration (k6)

Sans trafic, les courbes restent plates. Le scénario k6 de la démonstration a un profil « activité » : trois
utilisateurs virtuels partagent la session de démonstration pendant 10 minutes et enchaînent ventes complètes (devis,
validation, acceptation, facture, validation, transmission à la plateforme simulée, parfois un avoir), devis refusés,
consultations, synchronisations, questions à l'assistant (désactivé : refus attendu) et exports FEC ; un visiteur
anonyme charge l'application et une connexion invalide par minute alimente les « connexions refusées ». Données
fictives uniquement ; le rythme reste sous la limite de débit.

Prérequis : [k6](https://grafana.com/docs/k6/latest/set-up/install-k6/) et Node.js 22 (pour ouvrir la session avec
Playwright, voir [deploy/tests/README.md](../tests/README.md)). Depuis la racine du dépôt, la pile démarrée :

```bash
cd deploy/demo/e2e && npm ci && npx playwright install chromium && cd ../../..
(set -a; . deploy/demo/.env; set +a; SORTIE=/tmp/lettra-k6 node deploy/demo/e2e/parcours.mjs session)
LETTRA_SESSION_COOKIE="$(cat /tmp/lettra-k6/session-cookie.txt)" k6 run -e LETTRA_PROFIL=activite deploy/tests/k6/fumee.js
```

Réglages : `-e LETTRA_DUREE=3m` (10m par défaut), `-e LETTRA_UTILISATEURS=5` (3), `-e LETTRA_RYTHME=30` (actions par
minute, 24). Les métriques arrivent toutes les 10 s : les courbes bougent après une demi-minute environ.

### Comment c'est branché

```
API ─OTLP─► collecteur (base de la cellule, même filtrage) ─┬─ métriques et exemplaires ─► Prometheus (OTLP, règles de la cellule)
                                                            ├─ traces ─────────────────► Tempo
                                                            └─ journaux ───────────────► Loki ──► Grafana (sources et tableaux provisionnés)
API /internal/kpi/metrics ◄─ lecture chaque minute, jeton fictif ─ Prometheus (tâche lettra-kpi, hors OpenTelemetry)
```

- Le collecteur reprend la base de la cellule (`files/collector.yaml`) et ses destinations Tempo et Loki
  (`otel/collecteur-observabilite.yaml`, identique au rendu du chart : `deploy/tests/valider.sh` le vérifie). Le
  filtrage des données de dossier est donc celui d'une cellule.
- Les tableaux de bord sont ceux de la cellule (`deploy/helm/lettra-observability/dashboards`), montés tels quels :
  une modification dans le dépôt vaut pour les deux. Sources de données : `observabilite/grafana/sources.yaml`.
- Métriques métier : comptées à la sortie du pipeline des cas d'usage (`BusinessMetrics`, `LettraTelemetry`), avec des
  étiquettes fixes seulement ; un test vérifie sur le parcours du README qu'aucune ne porte de dossier, de personne,
  de client ou de montant (`DemoWalkthroughTests`).
- **Réservé à la démonstration** : Grafana ouvert en lecture sans connexion ; compte `admin` / `admin-demo` (`.env`,
  `GRAFANA_ADMIN_PASSWORD`) pour Explore et l'administration ; stockage local sans réplication ; rien ne sort vers
  Internet (statistiques et téléchargements de plugins désactivés).
- Ports sur `127.0.0.1` seulement : 3000 (Grafana), 9090 (Prometheus), 3200 (Tempo), 3100 (Loki). Variables
  `LETTRA_DEMO_GRAFANA_PORT`, `LETTRA_DEMO_PROMETHEUS_PORT`, `LETTRA_DEMO_TEMPO_PORT`, `LETTRA_DEMO_LOKI_PORT` en cas
  de conflit.
- Arrêt et remise à zéro, avec les mêmes fichiers : `down` garde les métriques, traces et journaux ; `down -v` les
  efface (volumes `prometheus-donnees`, `tempo-donnees`, `loki-donnees`, `grafana-donnees`).
- `compose.observabilite.yaml` remplace `compose.otel.yaml` (collecteur seul, ci-dessus) : ne pas combiner les deux.
- Vérifié à chaque CI par le job « Démo — télémétrie dans Grafana » de `.github/workflows/deploy.yml` : la pile démarre,
  Grafana répond et a ses sources et ses tableaux de bord, puis après deux minutes d'activité k6, chaque panneau a des
  données (sauf ceux que `SANS_DONNEES_EN_DEMO` de `verifier.py` laisse vides, avec leur raison : les deux panneaux
  PostgreSQL et quatre panneaux d'« Administration » sans erreur 5xx, connexion refusée ni poste relié garantis), un exemplaire mène à une trace qui a ses journaux, et les règles
  d'alerte sont visibles (`deploy/tests/grafana/verifier.py`). L'affichage dans un navigateur n'est pas vérifié par la CI.

## Comment c'est monté

- `keycloak/start.sh` complète le realm versionné (`deploy/keycloak/realm-lettra.json`) du cabinet et de
  l'utilisateur de `keycloak/demo-additions.json` (identifiants fixes, repris par la configuration de l'API), puis
  lance Keycloak avec import au premier démarrage.
- Émetteur des jetons : `KC_HOSTNAME` est l'adresse vue par le navigateur ; l'API appelle Keycloak par le réseau
  Docker (`http://keycloak:8080`) et `KC_HOSTNAME_BACKCHANNEL_DYNAMIC` y renvoie les points d'accès serveur à
  serveur, avec le même émetteur. Navigateur et API valident donc les mêmes jetons.
- L'API reçoit sa configuration par variables d'environnement (`compose.yaml`) : mode réseau local, facturation
  électronique simulée, utilisateur `demo` manager et administrateur du cabinet, compte de service fictif des factures
  récurrentes, e-mails vers MailPit.
- Vérifié à chaque CI par le job « Démo navigateur » de `.github/workflows/deploy.yml` (Playwright, `e2e/parcours.mjs`), y compris le profil HTTPS
  avec un nom d'hôte hors localhost.
- La même pile sert aussi un test de charge léger (k6, job « Démo — charge k6 ») et un scan de sécurité ZAP de base
  (« Démo — scan de sécurité ZAP ») : voir [deploy/tests/README.md](../tests/README.md). `node parcours.mjs session`
  se connecte (mot de passe + TOTP) et exporte le cookie de session pour k6.
- L'API pose ses en-têtes de sécurité sur toutes les réponses (CSP compatible Blazor WebAssembly, refus des cadres,
  isolation d'origine) : l'inscription du service worker est donc dans `pwa.js`, pas dans un script en ligne.
