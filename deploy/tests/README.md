# Tests de deploy/

| Fichier | Rôle | CI (`.github/workflows/deploy.yml`) |
|---|---|---|
| `valider.sh` | Règles d'alerte, routage, collecteur, charts Helm, OpenTofu (sans identifiant cloud) | `valider` |
| `k6/fumee.js` | Charge légère de la démonstration (~1 min) : santé, enveloppe PWA, lectures authentifiées, limite de débit | `demo-charge` |
| `kind/essai-keycloak.sh`, `kind/valeurs-ci.yaml` | Keycloak de la cellule dans un cluster kind jetable (valeurs CI seulement) : découverte, émetteur, connexion BFF, MFA, ajout d'un cabinet fictif | `keycloak-kind` |
| `zap/rules.tsv`, `zap/verifier.sh` | Scan de sécurité ZAP de base de la démonstration et classement des constatations | `demo-zap` |
| `k6/fumee.js` (`LETTRA_PROFIL=activite`), `grafana/verifier.py` | Activité de démonstration (2 min en CI), puis données de chaque panneau des tableaux de bord Grafana, liens exemplaire → trace → journaux, alertes | `demo-grafana` |

Les trois derniers visent la pile de `deploy/demo` (Keycloak + API qui sert la PWA, `http://localhost:8080/`), montée
comme pour le job « Démo navigateur ». Données et secrets fictifs uniquement.

## k6 : profil de fumée

Trois scénarios, environ une minute :

- **anonyme** (4 VU, 50 s) : `/health`, `index.html`, manifeste, service worker, `pwa.js`, `blazor.webassembly.js` et
  quelques fichiers du framework lus dans `service-worker-assets.js` ; l'API sans session doit répondre 401 et
  `Cache-Control: no-store`.
- **lectures** (1,5 itération/s, 50 s) : `/bff/user`, `/api/v1/customers`, `/api/v1/invoices` sur le dossier de
  démonstration, avec le cookie du BFF et `X-Lettra-Csrf: 1`. 270 requêtes par minute pour une seule session :
  sous la limite par utilisateur (`Lettra:RateLimiting:PermitsPerUser`, 300 par minute glissante).
- **rafale** (après les lectures) : la même session enchaîne les requêtes jusqu'au premier 429, qui doit porter
  `Retry-After` et le code `Api.TooManyRequests`. Ces 429 sont déclarés attendus (`http.expectedStatuses`) : ils ne
  comptent pas comme erreurs.

Seuils (l'essai échoue s'ils sont dépassés) :

| Mesure | Seuil |
|---|---|
| `http_req_failed` (hors 401 anonymes et 429 de la rafale) | < 1 % ; 0 sur les lectures |
| `http_req_duration` p95, anonyme | < 300 ms |
| `http_req_duration` p95, lectures authentifiées | < 500 ms |
| `checks` | > 99 % |
| `lettra_429_rafale` | au moins un 429 pendant la rafale |

### Session authentifiée : pourquoi le cookie exporté par Playwright

L'API exige une authentification qui **prouve le second facteur** (`amr`), par le cookie du BFF ou par un jeton porteur.
Un *direct grant* Keycloak (mot de passe seul) ne prouve pas la MFA, et l'ouvrir sur un client, même réservé à la CI,
affaiblirait le realm versionné qui sert aussi la production. Le parcours Playwright de la démonstration se connecte
donc comme un utilisateur (mot de passe + TOTP, enrôlement compris) et exporte le cookie `__Host-lettra` :

```bash
docker compose -f deploy/demo/compose.yaml up -d --build --wait
cd deploy/demo/e2e && npm ci && npx playwright install chromium
set -a; . ../.env; set +a
SORTIE=/tmp/lettra-k6 node parcours.mjs session       # écrit /tmp/lettra-k6/session-cookie.txt
cd ../../..
LETTRA_SESSION_COOKIE="$(cat /tmp/lettra-k6/session-cookie.txt)" k6 run deploy/tests/k6/fumee.js
```

Sans `LETTRA_SESSION_COOKIE`, seul le scénario anonyme s'exécute. En CI, la session reste hors des artefacts ; le
rapport est publié dans l'artefact `demo-charge-k6` : `resume.txt`, `resume.json` et le tableau de bord
`rapport.html` (`K6_WEB_DASHBOARD_EXPORT`).

## ZAP : scan de base

`zap-baseline.py` (image `zaproxy/zap-stable`, version figée dans le workflow) explore `http://localhost:8080/` sans
authentification et applique les règles passives. Le job échoue :

- sur toute règle classée `FAIL` dans `zap/rules.tsv` : les durcissements en place (CSP, cadres, isolation
  d'origine, en-tête `Server`, attributs des cookies…) ne doivent pas régresser ;
- sur toute constatation de risque faible ou plus dont la règle n'est **pas classée** dans `rules.tsv`
  (`zap/verifier.sh`), car `zap-baseline.py` laisse en simple avertissement les règles absentes du fichier.

Une nouvelle constatation se corrige dans l'API (`src/Hosts/Lettra.Api/Security/SecurityHeaders.cs`) ; si elle est
acceptée, elle est classée `WARN` ou `IGNORE` dans `rules.tsv` avec sa justification en commentaire. Rapports
HTML, JSON et Markdown : artefact `demo-zap`.

En local :

```bash
mkdir -p /tmp/zap && chmod 777 /tmp/zap && cp deploy/tests/zap/rules.tsv /tmp/zap/
docker run --rm --network host -v /tmp/zap:/zap/wrk:rw zaproxy/zap-stable:2.16.1 \
  zap-baseline.py -t http://localhost:8080/ -c rules.tsv -I -r rapport.html -J rapport.json -w rapport.md
deploy/tests/zap/verifier.sh /tmp/zap/rapport.json
```

(`--network host` : sous Docker Desktop, viser `http://host.docker.internal:8080/`.)
