# Démonstration navigateur (Windows, macOS, Linux)

Lettra en mode 2 (réseau local) dans le navigateur : Keycloak (connexion, MFA) et l'API, qui sert l'application
Blazor installable (PWA). Seul **Docker** est nécessaire : Docker Desktop sous Windows et macOS, Docker Engine et
son extension Compose sous Linux. Pas de SDK .NET : l'image de l'API est construite depuis le `Dockerfile` du dépôt.

Tout est **fictif et réservé à la démonstration** : cabinet, utilisateur, secrets de `.env`. Ne jamais réutiliser
ces valeurs ni ce montage en production (Keycloak en `start-dev`, base H2, HTTP sur localhost).

## Démarrer et arrêter

Depuis la racine du dépôt, même commande dans PowerShell, l'invite de commandes, bash ou zsh :

```bash
docker compose -f deploy/demo/compose.yaml up -d --build --wait     # démarrer (premier build : quelques minutes)
docker compose -f deploy/demo/compose.yaml down                     # arrêter, les données restent
docker compose -f deploy/demo/compose.yaml down -v                  # arrêter et tout remettre à zéro
```

Puis ouvrir **http://localhost:8080/** dans Chrome, Edge ou Firefox (l'application peut mettre quelques secondes à
répondre après le démarrage).

| Service | Adresse | Usage |
|---|---|---|
| Lettra (API + application) | http://localhost:8080/ | Application, `/health` |
| Keycloak | http://localhost:8081/ | Connexion ; console d'administration : `admin` / `admin-demo` |

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
5. L'écran des clients s'affiche, sur le « Dossier de démonstration ». Les connexions suivantes : mot de passe + code.

## Installer l'application (PWA)

- **Chrome, Edge (Windows, macOS, Linux)** : icône d'installation dans la barre d'adresse (ou menu ⋮ → *Installer
  Lettra*). L'application s'ouvre ensuite dans sa propre fenêtre. `http://localhost` est un contexte sécurisé : le
  cookie de session (`__Host-`, `Secure`) et le service worker y fonctionnent sans certificat.
- Le service worker ne met en cache que l'enveloppe de l'application (fichiers statiques) : jamais les réponses de
  `/api/` ni de `/bff/`, donc aucune donnée de dossier.
- Firefox sur ordinateur : installation absente ou expérimentale selon la version ; Safari (macOS) non vérifié.

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

`down -v` (avec les mêmes fichiers `-f` qu'au démarrage) supprime ces volumes : au prochain démarrage, le realm est
réimporté et l'enrôlement TOTP est à refaire.

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
