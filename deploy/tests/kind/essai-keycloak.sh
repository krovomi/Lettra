#!/usr/bin/env bash
# Essai de la cellule dans un cluster kind jetable (CI : job « keycloak-kind » de .github/workflows/deploy.yml ;
# en local : même commande, avec docker, kind, kubectl, helm, curl, jq et openssl). Rien n'est appliqué à un cloud.
#
# Installe l'opérateur CloudNativePG puis le chart lettra-cell (valeurs CI seulement : valeurs-ci.yaml), attend
# Keycloak et sa base, puis vérifie :
#   - la découverte du realm (/.well-known/openid-configuration) publie l'émetteur attendu ;
#   - l'API pointe sur le Keycloak de la cellule, répond sur /health et sa connexion BFF mène à la page de Keycloak ;
#   - le client lettra-maui accepte la boucle locale sur un port quelconque et exige PKCE ;
#   - le flux navigateur impose le second facteur, les organisations sont actives, un cabinet s'ajoute (ajouter-cabinet.sh).
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../../.." && pwd)
CLUSTER=${KIND_CLUSTER:-lettra}
NS=lettra
IMAGE=ghcr.io/krovomi/lettra-api:ci-kind
SORTIE=${SORTIE:-$ROOT/deploy/tests/kind/.run}
EMETTEUR=http://localhost:8080/realms/lettra
AUTORITE_INTERNE=http://keycloak:8080/realms/lettra
# Même version de l'opérateur que la cellule (deploy/argocd/lettra-racine/values.yaml).
CNPG_VERSION=$(sed -n 's/.*cloudnativePg:.*version: *\([0-9.]*\).*/\1/p' "$ROOT/deploy/argocd/lettra-racine/values.yaml")
mkdir -p "$SORTIE"
debut=$(date +%s)
step() { printf '\n== [%3ss] %s\n' "$(( $(date +%s) - debut ))" "$*"; }
echec() { echo "ÉCHEC : $*" >&2; exit 1; }

diagnostic() {
  set +e
  kubectl -n "$NS" get all,cluster,pvc,networkpolicy -o wide > "$SORTIE/ressources.txt" 2>&1
  kubectl -n "$NS" describe pods > "$SORTIE/pods.txt" 2>&1
  kubectl -n "$NS" get events --sort-by=.lastTimestamp > "$SORTIE/evenements.txt" 2>&1
  kubectl -n "$NS" logs deploy/keycloak --all-containers --tail=-1 > "$SORTIE/keycloak.log" 2>&1
  kubectl -n "$NS" logs deploy/lettra-api --tail=-1 > "$SORTIE/api.log" 2>&1
  kubectl -n cnpg-system logs deploy/cnpg-cloudnative-pg --tail=300 > "$SORTIE/cnpg.log" 2>&1
  tail -n 40 "$SORTIE/keycloak.log" "$SORTIE/api.log" 2>/dev/null
}
pids=()
fin() {
  code=$?
  [[ $code -eq 0 ]] || diagnostic
  for pid in "${pids[@]}"; do kill "$pid" 2>/dev/null || true; done
  exit $code
}
trap fin EXIT

step "Image de l'API (en arrière-plan)"
docker build -t "$IMAGE" "$ROOT" > "$SORTIE/docker-build.log" 2>&1 &
build=$!

step "Cluster kind « $CLUSTER »"
kind get clusters 2>/dev/null | grep -qx "$CLUSTER" || kind create cluster --name "$CLUSTER" --wait 120s
kubectl config use-context "kind-$CLUSTER" >/dev/null

step "Opérateur CloudNativePG $CNPG_VERSION"
helm upgrade --install cnpg cloudnative-pg --repo https://cloudnative-pg.github.io/charts --version "$CNPG_VERSION" \
  --namespace cnpg-system --create-namespace --wait --timeout 5m >/dev/null

step "Espace de noms (Pod Security « restricted », comme OpenTofu) et secrets tirés au hasard"
kubectl create namespace "$NS" --dry-run=client -o yaml | kubectl apply -f - >/dev/null
kubectl label namespace "$NS" pod-security.kubernetes.io/enforce=restricted --overwrite >/dev/null
admin_password=$(openssl rand -hex 24)
bff_secret=$(openssl rand -hex 32)
kubectl -n "$NS" create secret generic lettra-keycloak --dry-run=client -o yaml \
  --from-literal=admin-password="$admin_password" --from-literal=bff-client-secret="$bff_secret" | kubectl apply -f - >/dev/null
kubectl -n "$NS" create secret generic lettra-api --dry-run=client -o yaml \
  --from-literal=Lettra__Identity__Oidc__BffClientSecret="$bff_secret" | kubectl apply -f - >/dev/null

step "Chart lettra-cell (Keycloak, sa base, l'API)"
helm dependency build "$ROOT/deploy/helm/lettra-cell" >/dev/null
helm upgrade --install cellule "$ROOT/deploy/helm/lettra-cell" --namespace "$NS" \
  -f "$ROOT/deploy/tests/kind/valeurs-ci.yaml" >/dev/null

step "Image de l'API : fin de la construction et chargement dans kind"
wait "$build" || { tail -n 60 "$SORTIE/docker-build.log"; echec "construction de l'image de l'API"; }
kind load docker-image "$IMAGE" --name "$CLUSTER" >/dev/null

step "Attente : base de Keycloak, Keycloak (import du realm), API"
kubectl -n "$NS" wait cluster/keycloak-db --for=condition=Ready --timeout=6m
kubectl -n "$NS" rollout status deploy/keycloak --timeout=8m
kubectl -n "$NS" rollout status deploy/lettra-api --timeout=4m

step "Redirections de port (Keycloak :8080, API :8081)"
kubectl -n "$NS" port-forward svc/keycloak 8080:8080 > "$SORTIE/port-forward-keycloak.log" 2>&1 & pids+=($!)
kubectl -n "$NS" port-forward svc/lettra-api 8081:80 > "$SORTIE/port-forward-api.log" 2>&1 & pids+=($!)
timeout 60 bash -c 'until curl -sf -o /dev/null http://localhost:8080/realms/lettra; do sleep 1; done'
timeout 60 bash -c 'until curl -sf -o /dev/null http://localhost:8081/health; do sleep 1; done'

step "Découverte du realm : émetteur attendu"
curl -fsS http://localhost:8080/realms/lettra/.well-known/openid-configuration > "$SORTIE/decouverte.json"
emetteur=$(jq -r .issuer "$SORTIE/decouverte.json")
[[ "$emetteur" == "$EMETTEUR" ]] || echec "émetteur $emetteur, attendu $EMETTEUR"
jq -e '.code_challenge_methods_supported | index("S256")' "$SORTIE/decouverte.json" >/dev/null || echec "S256 absent de la découverte"
echo "   issuer = $emetteur"

step "API : pointée sur le Keycloak de la cellule, /health"
autorite=$(kubectl -n "$NS" get deploy/lettra-api \
  -o jsonpath='{.spec.template.spec.containers[0].env[?(@.name=="Lettra__Identity__Oidc__Authority")].value}')
[[ "$autorite" == "$AUTORITE_INTERNE" ]] || echec "Lettra__Identity__Oidc__Authority = $autorite, attendu $AUTORITE_INTERNE"
sante=$(curl -fsS http://localhost:8081/health)
[[ "$sante" == "Healthy" ]] || echec "/health a répondu « $sante »"
echo "   Authority = $autorite ; /health = $sante"

step "Connexion BFF : l'API lit la découverte de Keycloak (dans la cellule) et redirige vers sa page"
read -r statut redirection < <(curl -sS -o /dev/null -w '%{http_code} %{redirect_url}\n' http://localhost:8081/bff/login)
[[ "$statut" == 302 ]] || echec "/bff/login a répondu $statut"
[[ "$redirection" == "$EMETTEUR/protocol/openid-connect/auth?"* ]] || echec "redirection inattendue : $redirection"
[[ "$redirection" == *client_id=lettra-bff* ]] || echec "la redirection ne porte pas le client lettra-bff : $redirection"
# Keycloak publie un point PAR (RFC 9126) : l'API y pousse d'abord sa demande (PKCE S256 compris), authentifiée par le
# secret du BFF, par le Service interne. Le navigateur ne reçoit qu'un request_uri : preuve de l'appel serveur à serveur.
if [[ "$redirection" == *request_uri=urn%3Aietf%3Aparams%3Aoauth%3Arequest_uri%3A* ]]; then
  echo "   demande poussée (PAR) acceptée : secret du BFF partagé entre l'API et Keycloak"
else
  [[ "$redirection" == *code_challenge_method=S256* ]] || echec "ni PAR ni PKCE S256 dans la redirection : $redirection"
fi
page=$(curl -sS -o "$SORTIE/page-connexion.html" -w '%{http_code}' "$redirection")
[[ "$page" == 200 ]] || echec "Keycloak refuse la demande du BFF ($page)"
grep -q 'kc-form-login' "$SORTIE/page-connexion.html" || echec "pas de formulaire de connexion Keycloak"
echo "   /bff/login → $EMETTEUR/protocol/openid-connect/auth → formulaire de connexion"

step "Client lettra-maui : boucle locale (port quelconque) et PKCE S256 exigé"
autoriser() { # redirect_uri, avec PKCE (oui|non) -> code HTTP
  local pkce=""
  [[ $2 == oui ]] && pkce="&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM&code_challenge_method=S256"
  curl -sS -o /dev/null -w '%{http_code}' \
    "http://localhost:8080/realms/lettra/protocol/openid-connect/auth?client_id=lettra-maui&response_type=code&scope=openid&redirect_uri=$(jq -rn --arg u "$1" '$u|@uri')$pkce"
}
port=$(( 49152 + RANDOM % 16000 ))
[[ $(autoriser "http://127.0.0.1:$port/callback" oui) == 200 ]] || echec "boucle locale :$port refusée"
[[ $(autoriser "http://127.0.0.2:$port/callback" oui) == 400 ]] || echec "une adresse hors boucle locale enregistrée est acceptée"
[[ $(autoriser "http://127.0.0.1:$port/callback" non) != 200 ]] || echec "connexion sans PKCE acceptée"
echo "   http://127.0.0.1:$port/callback accepté ; 127.0.0.2 refusé ; sans code_challenge refusé"

step "Realm importé : MFA obligatoire, organisations, ajout d'un cabinet fictif"
jeton=$(curl -fsS http://localhost:8080/realms/master/protocol/openid-connect/token \
  -d grant_type=password -d client_id=admin-cli -d username=lettra-amorcage --data-urlencode "password=$admin_password" | jq -er .access_token)
admin() { curl -fsS -H "Authorization: Bearer $jeton" "http://localhost:8080/admin/realms/lettra$1"; }
admin "" | jq -e '.browserFlow == "lettra-browser-mfa" and .organizationsEnabled == true' >/dev/null \
  || echec "realm sans flux MFA ou sans organisations"
admin /authentication/required-actions/CONFIGURE_TOTP | jq -e '.enabled and .defaultAction' >/dev/null \
  || echec "enrôlement TOTP non imposé"
organisation=$(KEYCLOAK_URL=http://localhost:8080 KEYCLOAK_TOKEN=$jeton \
  "$ROOT/deploy/keycloak/ajouter-cabinet.sh" cabinet-essai "Cabinet d'essai (fictif)" cabinet-essai.example)
admin "/organizations/$organisation" | jq -e '.alias == "cabinet-essai" and .enabled' >/dev/null \
  || echec "organisation $organisation introuvable"
echo "   flux lettra-browser-mfa, CONFIGURE_TOTP par défaut ; cabinet-essai = organisation $organisation"

step "Cellule kind : Keycloak et l'API vérifiés"
