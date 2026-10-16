# Démonstration navigateur (Windows, macOS, Linux)

Lettra en mode 2 (réseau local) dans le navigateur : Keycloak (connexion, MFA), l'API, qui sert l'application
Blazor installable (PWA), et le serveur whisper.cpp de la dictée (conteneur `whisper`). Seul **Docker** est nécessaire : Docker Desktop sous Windows et macOS, Docker Engine et
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

Les ports ne sont ouverts que sur le poste (127.0.0.1). Pour en changer : `LETTRA_DEMO_PORT` et
`LETTRA_DEMO_KEYCLOAK_PORT` dans `.env`.

## Premier usage

1. Sur l'écran de session, **Se connecter** : Keycloak s'ouvre.
2. Identifiant **`demo`**, mot de passe **`demo-lettra-demo`** (Camille Démo, manager du « Cabinet de démonstration »).
3. **Enrôlement du second facteur**, imposé à la première connexion : scanner le QR code avec une application TOTP qui
   accepte SHA-256 (FreeOTP, Google Authenticator récent, Aegis, Bitwarden, 1Password… ; pas Microsoft Authenticator),
   ou saisir la clé affichée sous « Impossible de scanner ? », puis entrer le code à 6 chiffres.
4. Keycloak redemande **une fois** le mot de passe puis le code : le jeton de l'enrôlement ne prouve pas le second
   facteur, Lettra exige une authentification complète.
5. Le tableau de bord s'affiche, sur le « Dossier de démonstration », rempli d'un an d'activité fictive au premier démarrage (README racine). Les connexions suivantes : mot de passe + code.

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
modèle n'est pas retéléchargé. Le bouton micro dans les écrans arrive avec le tutoriel et l'aide à la saisie (PR en
cours).

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
     (libellé variable selon le constructeur). Chrome approuve alors la démonstration.
   - **iOS / iPadOS** : ouvrir le lien dans Safari, *Autoriser* le profil ; Réglages → Général → VPN et gestion de
     l'appareil → installer le profil ; puis Réglages → Général → Informations → **Réglages des certificats** →
     activer la confiance totale pour « Caddy Local Authority ».
5. Ouvrir **https://192.168.1.20:8443/** (aussi sur le poste : dans ce profil, c'est la seule adresse de retour de
   Keycloak). Installer la PWA : Chrome Android → menu ⋮ → *Installer l'application* ; Safari iOS → Partager →
   *Sur l'écran d'accueil*.

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
- Docker Desktop (« Clean / Purge data », nettoyage des volumes) et `docker volume prune -a` suppriment aussi les volumes
  d'une démo arrêtée. Pour libérer de la place, `docker builder prune` suffit (cache de construction de l'image).
- **Sauvegarder** (démo arrêtée) :
  `docker run --rm -v lettra-demo_lettra-donnees:/donnees -v "${PWD}:/sauvegarde" busybox tar czf /sauvegarde/lettra-donnees.tgz -C /donnees .`

## Option : télémétrie OpenTelemetry

La démonstration seule n'exporte rien. Pour voir ce que l'API enverrait au collecteur d'une cellule, ajouter
`compose.otel.yaml` aux mêmes commandes :

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

## Comment c'est monté

- `keycloak/start.sh` complète le realm versionné (`deploy/keycloak/realm-lettra.json`) du cabinet et de
  l'utilisateur de `keycloak/demo-additions.json` (identifiants fixes, repris par la configuration de l'API), puis
  lance Keycloak avec import au premier démarrage.
- Émetteur des jetons : `KC_HOSTNAME` est l'adresse vue par le navigateur ; l'API appelle Keycloak par le réseau
  Docker (`http://keycloak:8080`) et `KC_HOSTNAME_BACKCHANNEL_DYNAMIC` y renvoie les points d'accès serveur à
  serveur, avec le même émetteur. Navigateur et API valident donc les mêmes jetons.
- L'API reçoit sa configuration par variables d'environnement (`compose.yaml`) : mode réseau local, facturation
  électronique simulée, utilisateur `demo` manager du cabinet.
- Vérifié à chaque CI par le job « Démo navigateur » de `.github/workflows/deploy.yml` (Playwright, `e2e/parcours.mjs`), y compris le profil HTTPS
  avec un nom d'hôte hors localhost.
- La même pile sert aussi un test de charge léger (k6, job « Démo — charge k6 ») et un scan de sécurité ZAP de base
  (« Démo — scan de sécurité ZAP ») : voir [deploy/tests/README.md](../tests/README.md). `node parcours.mjs session`
  se connecte (mot de passe + TOTP) et exporte le cookie de session pour k6.
- L'API pose ses en-têtes de sécurité sur toutes les réponses (CSP compatible Blazor WebAssembly, refus des cadres,
  isolation d'origine) : l'inscription du service worker est donc dans `pwa.js`, pas dans un script en ligne.
