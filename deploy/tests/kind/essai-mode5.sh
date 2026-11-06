#!/usr/bin/env bash
# Essai d'une cellule en mode 5 (HostedSaaS) dans un cluster kind jetable (CI : job « mode5-kind » de
# .github/workflows/deploy.yml ; en local : même commande, avec docker, kind, kubectl, helm, curl, jq et openssl).
# Rien n'est appliqué à un cloud, aucune donnée réelle : un cabinet et un utilisateur fictifs, tirés au hasard.
#
# Installe l'opérateur CloudNativePG, un KMS OpenBao d'essai (openbao-essai.yaml) puis le chart lettra-cell
# (valeurs-mode5.yaml) : Keycloak, l'API et le Worker, un cluster PostgreSQL derrière PgBouncer. Puis vérifie, de bout
# en bout :
#   1. le Worker migre le catalogue (l'API ne migre rien en mode 5) ;
#   2. un cabinet et son administrateur existent dans Keycloak (organisation, compte) et dans le catalogue ;
#   3. un dossier créé par l'API passe à « actif », sa base d_… existe sur le cluster, la KEK du cabinet est tirée par
#      le KMS et gardée enveloppée dans le catalogue ;
#   4. un client et une facture s'y créent, la facture se valide et se relit, y compris après redémarrage de l'API ;
#      l'e-mail du client est chiffré dans la base du dossier ;
#   5. une création de dossier interrompue (serveur de bases indisponible, Worker arrêté) est reprise par le Worker ;
#   6. après une rotation de la clé racine du cabinet, le Worker réenveloppe sa KEK et tout reste lisible ;
#   7. la clé racine détruite dans le KMS, les dossiers du cabinet ne s'ouvrent plus (effacement cryptographique).
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../../.." && pwd)
CLUSTER=${KIND_CLUSTER:-lettra-mode5}
NS=lettra
CELLULE=ci-kind
API_IMAGE=ghcr.io/krovomi/lettra-api:ci-kind
WORKER_IMAGE=ghcr.io/krovomi/lettra-worker:ci-kind
SORTIE=${SORTIE:-$ROOT/deploy/tests/kind/.run-mode5}
KEYCLOAK=http://localhost:8080
API=http://localhost:8081
CNPG_VERSION=$(sed -n 's/.*cloudnativePg:.*version: *\([0-9.]*\).*/\1/p' "$ROOT/deploy/argocd/lettra-racine/values.yaml")
mkdir -p "$SORTIE"
debut=$(date +%s)
step() { printf '\n== [%3ss] %s\n' "$(( $(date +%s) - debut ))" "$*"; }
echec() { echo "ÉCHEC : $*" >&2; exit 1; }

diagnostic() {
  set +e
  kubectl -n "$NS" get all,cluster,pooler,pvc -o wide > "$SORTIE/ressources.txt" 2>&1
  kubectl -n "$NS" describe pods > "$SORTIE/pods.txt" 2>&1
  kubectl -n "$NS" get events --sort-by=.lastTimestamp > "$SORTIE/evenements.txt" 2>&1
  kubectl -n "$NS" logs deploy/keycloak --tail=-1 > "$SORTIE/keycloak.log" 2>&1
  kubectl -n "$NS" logs deploy/lettra-api --tail=-1 > "$SORTIE/api.log" 2>&1
  kubectl -n "$NS" logs deploy/lettra-api --previous --tail=-1 > "$SORTIE/api-precedent.log" 2>&1
  kubectl -n "$NS" logs deploy/lettra-worker --tail=-1 > "$SORTIE/worker.log" 2>&1
  kubectl -n "$NS" logs deploy/openbao --tail=300 > "$SORTIE/openbao.log" 2>&1
  kubectl -n cnpg-system logs deploy/cnpg-cloudnative-pg --tail=300 > "$SORTIE/cnpg.log" 2>&1
  catalogue 'SELECT "Id", "State", "LastError" FROM catalog."Dossiers"' > "$SORTIE/catalogue-dossiers.txt" 2>&1
  tail -n 40 "$SORTIE/api.log" "$SORTIE/worker.log" 2>/dev/null
}
pids=()
fin() {
  code=$?
  [[ $code -eq 0 ]] || diagnostic
  for pid in "${pids[@]}"; do kill "$pid" 2>/dev/null || true; done
  exit $code
}
trap fin EXIT

# SQL sur le primaire du cluster pg-a, en superutilisateur local (socket du pod) : vérifications de l'essai seulement.
sql() { kubectl -n "$NS" exec pg-a-1 -c postgres -- psql -v ON_ERROR_STOP=1 -U postgres -d "$1" -tAc "$2"; }
catalogue() { sql lettra "$1"; }
# KMS d'essai : commande bao dans son pod, avec le jeton racine tiré par l'essai.
bao() { kubectl -n "$NS" exec deploy/openbao -- env BAO_ADDR=http://127.0.0.1:8200 BAO_TOKEN="$kms_token" bao "$@"; }
enveloppe() { catalogue "SELECT \"WrappedKek\" FROM catalog.\"Organisations\" WHERE \"Id\" = '$cabinet'"; }
reenveloppee() { [[ $(enveloppe) == vault:v2:* ]]; }
actif() { [[ $(catalogue "SELECT \"State\" FROM catalog.\"Dossiers\" WHERE \"Id\" = '$1'") == 1 ]]; }
attendre() { # secondes, description, commande…
  local limite=$(( $(date +%s) + $1 )) description=$2; shift 2
  until "$@" > /dev/null 2>&1; do
    (( $(date +%s) < limite )) || echec "$description : délai dépassé"
    sleep 2
  done
}

step "Images de l'API et du Worker (en arrière-plan)"
docker build -t "$API_IMAGE" "$ROOT" > "$SORTIE/docker-build-api.log" 2>&1 &
build_api=$!
docker build -f "$ROOT/Dockerfile.worker" -t "$WORKER_IMAGE" "$ROOT" > "$SORTIE/docker-build-worker.log" 2>&1 &
build_worker=$!

step "Cluster kind « $CLUSTER »"
kind get clusters 2>/dev/null | grep -qx "$CLUSTER" || kind create cluster --name "$CLUSTER" --wait 120s
kubectl config use-context "kind-$CLUSTER" >/dev/null

step "Opérateur CloudNativePG $CNPG_VERSION"
helm upgrade --install cnpg cloudnative-pg --repo https://cloudnative-pg.github.io/charts --version "$CNPG_VERSION" \
  --namespace cnpg-system --create-namespace --wait --timeout 5m >/dev/null

step "Espace de noms (Pod Security « restricted ») et secrets tirés au hasard"
kubectl create namespace "$NS" --dry-run=client -o yaml | kubectl apply -f - >/dev/null
kubectl label namespace "$NS" pod-security.kubernetes.io/enforce=restricted --overwrite >/dev/null
admin_password=$(openssl rand -hex 24)
bff_secret=$(openssl rand -hex 32)
kubectl -n "$NS" create secret generic lettra-keycloak --dry-run=client -o yaml \
  --from-literal=admin-password="$admin_password" --from-literal=bff-client-secret="$bff_secret" | kubectl apply -f - >/dev/null
# Le cabinet n'est déclaré à l'API qu'une fois son organisation Keycloak créée (étape « cabinet » plus bas).
kubectl -n "$NS" create secret generic lettra-api --dry-run=client -o yaml \
  --from-literal=Lettra__Identity__Oidc__BffClientSecret="$bff_secret" | kubectl apply -f - >/dev/null
kubectl -n "$NS" create secret generic lettra-worker --dry-run=client -o yaml \
  --from-literal=Lettra__Observability__ServiceName=lettra-worker | kubectl apply -f - >/dev/null
kms_token=$(openssl rand -hex 24)
kubectl -n "$NS" create secret generic lettra-kms --dry-run=client -o yaml \
  --from-literal=token="$kms_token" | kubectl apply -f - >/dev/null

step "KMS d'essai : OpenBao, moteur Transit"
kubectl -n "$NS" apply -f "$ROOT/deploy/tests/kind/openbao-essai.yaml" >/dev/null
kubectl -n "$NS" rollout status deploy/openbao --timeout=3m
bao secrets enable transit >/dev/null

step "Chart lettra-cell en mode 5 (Keycloak, PostgreSQL et PgBouncer, API, Worker)"
helm dependency build "$ROOT/deploy/helm/lettra-cell" >/dev/null
helm upgrade --install cellule "$ROOT/deploy/helm/lettra-cell" --namespace "$NS" \
  -f "$ROOT/deploy/tests/kind/valeurs-mode5.yaml" >/dev/null

step "Images : fin de la construction et chargement dans kind"
wait "$build_api" || { tail -n 60 "$SORTIE/docker-build-api.log"; echec "construction de l'image de l'API"; }
wait "$build_worker" || { tail -n 60 "$SORTIE/docker-build-worker.log"; echec "construction de l'image du Worker"; }
kind load docker-image "$API_IMAGE" "$WORKER_IMAGE" --name "$CLUSTER" >/dev/null

step "Attente : cluster pg-a et son PgBouncer, Keycloak, Worker"
kubectl -n "$NS" wait cluster/pg-a --for=condition=Ready --timeout=6m
kubectl -n "$NS" rollout status deploy/pg-a-pooler --timeout=3m
kubectl -n "$NS" wait cluster/keycloak-db --for=condition=Ready --timeout=6m
kubectl -n "$NS" rollout status deploy/keycloak --timeout=8m
kubectl -n "$NS" rollout status deploy/lettra-worker --timeout=4m

step "1. Le Worker migre le catalogue (l'API attend)"
attendre 180 "migration du catalogue par le Worker" catalogue 'SELECT 1 FROM catalog."__EFMigrationsHistory" LIMIT 1'
echo "   catalogue migré : $(catalogue 'SELECT count(*) FROM catalog."__EFMigrationsHistory"') migrations"
kubectl -n "$NS" rollout status deploy/lettra-api --timeout=4m

step "Redirections de port (Keycloak :8080, API :8081)"
kubectl -n "$NS" port-forward svc/keycloak 8080:8080 > "$SORTIE/port-forward-keycloak.log" 2>&1 & pids+=($!)
attendre 60 "Keycloak joignable" curl -sf -o /dev/null "$KEYCLOAK/realms/lettra"

step "2. Cabinet fictif : organisation et administrateur dans Keycloak"
jeton_admin=$(curl -fsS "$KEYCLOAK/realms/master/protocol/openid-connect/token" \
  -d grant_type=password -d client_id=admin-cli -d username=lettra-amorcage --data-urlencode "password=$admin_password" | jq -er .access_token)
kc() { curl -fsS -H "Authorization: Bearer $jeton_admin" -H 'Content-Type: application/json' "$@"; }
cabinet=$(KEYCLOAK_URL=$KEYCLOAK KEYCLOAK_TOKEN=$jeton_admin \
  "$ROOT/deploy/keycloak/ajouter-cabinet.sh" cabinet-mode5 "Cabinet mode 5 (fictif)" cabinet-mode5.example)
mot_de_passe=$(openssl rand -hex 16)
kc -X POST "$KEYCLOAK/admin/realms/lettra/users" -d "$(jq -n --arg p "$mot_de_passe" '{
  username: "admin-mode5", email: "admin@cabinet-mode5.example", emailVerified: true, enabled: true,
  firstName: "Camille", lastName: "Essai", requiredActions: [],
  credentials: [{type: "password", value: $p, temporary: false}]}')"
utilisateur=$(kc "$KEYCLOAK/admin/realms/lettra/users?username=admin-mode5&exact=true" | jq -er '.[0].id')
# Le realm ajoute l'enrôlement TOTP à tout nouveau compte : retiré pour ce compte d'essai (jeton par mot de passe).
kc -X PUT "$KEYCLOAK/admin/realms/lettra/users/$utilisateur" -d '{"requiredActions": []}'
kc -X POST "$KEYCLOAK/admin/realms/lettra/organizations/$cabinet/members" -d "\"$utilisateur\""
# Client d'essai : jeton par mot de passe, avec les mêmes claims que l'application (organisation, audience lettra-api).
kc -X POST "$KEYCLOAK/admin/realms/lettra/clients" -d "$(jq '.clients[] | select(.clientId == "lettra-maui")
  | {clientId: "lettra-essai", publicClient: true, standardFlowEnabled: false, directAccessGrantsEnabled: true,
     defaultClientScopes, protocolMappers: [.protocolMappers[] | del(.id)]}' "$ROOT/deploy/keycloak/realm-lettra.json")"
echo "   cabinet = organisation $cabinet ; administrateur = $utilisateur"

step "2. Cabinet fictif : déclaré au catalogue (sa KEK sera tirée par le KMS), API redémarrée"
kubectl -n "$NS" create secret generic lettra-api --dry-run=client -o yaml \
  --from-literal=Lettra__Identity__Oidc__BffClientSecret="$bff_secret" \
  --from-literal=Lettra__Organisations__0__Id="$cabinet" \
  --from-literal=Lettra__Organisations__0__Name="Cabinet mode 5 (fictif)" \
  --from-literal=Lettra__Organisations__0__CellId="$CELLULE" \
  --from-literal=Lettra__Authorization__Users__0__Id="$utilisateur" \
  --from-literal=Lettra__Authorization__Users__0__Cabinet="$cabinet" \
  --from-literal=Lettra__Authorization__Users__0__State=Active \
  --from-literal=Lettra__Authorization__Users__0__Assignments__0__Role=administrateur-cabinet \
  --from-literal=Lettra__Authorization__Users__0__Assignments__0__Scope=Cabinet \
  --from-literal=Lettra__Authorization__Users__0__Assignments__0__ScopeId="$cabinet" \
  --from-literal=Lettra__Authorization__Users__0__Assignments__1__Role=manager \
  --from-literal=Lettra__Authorization__Users__0__Assignments__1__Scope=Cabinet \
  --from-literal=Lettra__Authorization__Users__0__Assignments__1__ScopeId="$cabinet" | kubectl apply -f - >/dev/null
kubectl -n "$NS" rollout restart deploy/lettra-api >/dev/null
kubectl -n "$NS" rollout status deploy/lettra-api --timeout=4m
[[ $(catalogue "SELECT \"CellId\" FROM catalog.\"Organisations\" WHERE \"Id\" = '$cabinet'") == "$CELLULE" ]] \
  || echec "cabinet absent du catalogue"
[[ $(catalogue "SELECT \"CellId\" FROM catalog.\"Clusters\" WHERE \"Id\" = 'pg-a'") == "$CELLULE" ]] \
  || echec "cluster pg-a absent du catalogue ou hors de la cellule"

redirection_api() {
  kubectl -n "$NS" port-forward svc/lettra-api 8081:80 > "$SORTIE/port-forward-api.log" 2>&1 & pids+=($!)
  attendre 60 "API joignable" curl -sf -o /dev/null "$API/health"
}
redirection_api
jeton() {
  curl -fsS "$KEYCLOAK/realms/lettra/protocol/openid-connect/token" -d grant_type=password -d client_id=lettra-essai \
    -d username=admin-mode5 --data-urlencode "password=$mot_de_passe" -d scope=openid | jq -er .access_token
}
# appel MÉTHODE CHEMIN [CORPS] [DOSSIER] -> corps de la réponse ; le code HTTP dans $SORTIE/statut.
appel() {
  local args=(-sS -o "$SORTIE/reponse.json" -w '%{http_code}' -X "$1" "$API$2" -H "Authorization: Bearer $(jeton)" -H 'If-Match: *')
  [[ -n "${3:-}" ]] && args+=(-H 'Content-Type: application/json' -d "$3")
  [[ -n "${4:-}" ]] && args+=(-H "X-Dossier: $4")
  curl "${args[@]}" > "$SORTIE/statut"
  cat "$SORTIE/reponse.json"
}
statut() { cat "$SORTIE/statut"; }
attendu() { [[ $(statut) == "$1" ]] || echec "$2 : HTTP $(statut), attendu $1 : $(head -c 600 "$SORTIE/reponse.json")"; }

step "3. Dossier créé par l'API : actif, base d_… sur le cluster"
dossier=$(appel POST "/api/v1/cabinets/$cabinet/dossiers" '{"name":"Dossier mode 5 (fictif)"}' | jq -r '.id // empty')
attendu 201 "création du dossier"
base="d_${dossier//-/}"
[[ $(catalogue "SELECT \"State\" FROM catalog.\"Dossiers\" WHERE \"Id\" = '$dossier'") == 1 ]] || echec "dossier $dossier pas actif"
[[ $(sql postgres "SELECT datname FROM pg_database WHERE datname = '$base'") == "$base" ]] || echec "base $base absente"
[[ $(sql postgres "SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname = '$base'") == lettra ]] \
  || echec "base $base : propriétaire inattendu"
[[ $(catalogue "SELECT octet_length(\"WrappedDek\") FROM catalog.\"Dossiers\" WHERE \"Id\" = '$dossier'") == 60 ]] \
  || echec "DEK du dossier absente ou mal enveloppée"
appel GET /api/v1/dossiers | jq -e --arg d "$dossier" 'map(.id) | index($d)' >/dev/null || echec "dossier absent de la liste"
[[ $(enveloppe) == vault:v1:* ]] || echec "KEK du cabinet absente du catalogue ou pas enveloppée par le KMS : $(enveloppe)"
echo "   dossier $dossier actif ; base $base (propriétaire lettra) ; DEK enveloppée (60 octets) ; KEK enveloppée par le KMS"

step "4. Client et facture dans le dossier, validation, relecture"
appel PUT /api/v1/company-profile '{"name":"Conseil fictif","legalForm":"SAS","shareCapital":10000,"siren":"552100554",
  "rcs":"RCS Paris 552 100 554","vatNumber":"FR96552100554","addressLine1":"10 avenue de l'"'"'Opéra","postalCode":"75001",
  "city":"Paris","latePenaltyRate":10,"earlyPaymentDiscount":"Pas d'"'"'escompte pour paiement anticipé"}' "$dossier" >/dev/null
[[ $(statut) == 2* ]] || echec "profil de l'entreprise : HTTP $(statut) $(head -c 600 "$SORTIE/reponse.json")"
client=$(appel POST /api/v1/customers '{"kind":"Company","name":"Boulangerie fictive","siret":"73282932000074",
  "email":"compta@boulangerie.example","addressLine1":"1 rue de la Paix","postalCode":"75002","city":"Paris","countryCode":"FR"}' \
  "$dossier" | jq -r '.id // empty')
attendu 201 "création du client"
facture=$(appel POST /api/v1/invoices "{\"customerId\":\"$client\",\"lines\":[{\"designation\":\"Conseil\",\"quantity\":1,
  \"unitPriceExclTax\":100,\"unitCode\":\"HUR\",\"kind\":\"Service\"}]}" "$dossier" | jq -r '.id // empty')
attendu 201 "brouillon de facture"
appel POST "/api/v1/invoices/$facture/validate" "" "$dossier" >/dev/null
attendu 200 "validation de la facture"
numero=$(appel GET "/api/v1/invoices/$facture" "" "$dossier" | jq -er '.number')
attendu 200 "relecture de la facture"
[[ $(sql "$base" "SELECT count(*) FROM \"DossierStamp\"") == 1 ]] || echec "estampille du dossier absente de sa base"
courriel=$(sql "$base" "SELECT \"Email\" FROM \"Customers\" WHERE \"Id\" = '$client'")
[[ $courriel == lettra:enc:v1:* && $courriel != *boulangerie* ]] || echec "e-mail du client en clair dans la base : $courriel"
[[ $(appel GET "/api/v1/customers/$client" "" "$dossier" | jq -r .email) == compta@boulangerie.example ]] \
  || echec "e-mail du client illisible par l'API"
echo "   client $client (e-mail chiffré en base, lu en clair par l'API) ; facture $facture validée, numéro $numero"

step "4. Données gardées après redémarrage de l'API"
kill "${pids[-1]}" 2>/dev/null || true
kubectl -n "$NS" rollout restart deploy/lettra-api >/dev/null
kubectl -n "$NS" rollout status deploy/lettra-api --timeout=4m
redirection_api
[[ $(appel GET "/api/v1/invoices/$facture" "" "$dossier" | jq -r '.number') == "$numero" ]] || echec "facture perdue au redémarrage"
echo "   facture $numero relue après redémarrage"

step "5. Création interrompue (serveur de bases indisponible, Worker arrêté), puis reprise par le Worker"
kubectl -n "$NS" scale deploy/lettra-worker --replicas=0 >/dev/null
kubectl -n "$NS" rollout status deploy/lettra-worker --timeout=2m
sql postgres "ALTER ROLE lettra NOCREATEDB" >/dev/null
appel POST "/api/v1/cabinets/$cabinet/dossiers" '{"name":"Dossier interrompu (fictif)"}' >/dev/null
[[ $(statut) == 5* ]] || echec "création sans droit CREATEDB : HTTP $(statut), échec attendu"
interrompu=$(catalogue "SELECT \"Id\" FROM catalog.\"Dossiers\" WHERE \"State\" = 2 LIMIT 1")
[[ -n "$interrompu" ]] || echec "aucun dossier « en échec » dans le catalogue"
echo "   dossier $interrompu en échec : $(catalogue "SELECT \"LastError\" FROM catalog.\"Dossiers\" WHERE \"Id\" = '$interrompu'")"
sql postgres "ALTER ROLE lettra CREATEDB" >/dev/null
kubectl -n "$NS" scale deploy/lettra-worker --replicas=1 >/dev/null
attendre 120 "reprise du dossier $interrompu par le Worker" actif "$interrompu"
[[ $(sql postgres "SELECT count(*) FROM pg_database WHERE datname = 'd_${interrompu//-/}'") == 1 ]] || echec "base du dossier repris absente"
appel GET /api/v1/customers "" "$interrompu" >/dev/null
attendu 200 "lecture du dossier repris"
echo "   dossier $interrompu repris par le Worker : actif, base créée et migrée"

step "6. Rotation de la clé racine du cabinet dans le KMS, KEK réenveloppée par le Worker"
cle_racine="lettra-cabinet-${cabinet//-/}"
bao write -f "transit/keys/$cle_racine/rotate" >/dev/null
attendre 120 "réenveloppement de la KEK par le Worker" reenveloppee
kill "${pids[-1]}" 2>/dev/null || true
kubectl -n "$NS" rollout restart deploy/lettra-api >/dev/null
kubectl -n "$NS" rollout status deploy/lettra-api --timeout=4m
redirection_api
[[ $(appel GET "/api/v1/invoices/$facture" "" "$dossier" | jq -r '.number') == "$numero" ]] || echec "facture illisible après la rotation"
echo "   enveloppe de la KEK à la version 2 ; facture $numero relue par une API neuve"

step "7. Clé racine du cabinet détruite dans le KMS : ses dossiers ne s'ouvrent plus"
bao write "transit/keys/$cle_racine/config" deletion_allowed=true >/dev/null
bao delete "transit/keys/$cle_racine" >/dev/null
kill "${pids[-1]}" 2>/dev/null || true
kubectl -n "$NS" rollout restart deploy/lettra-api >/dev/null
kubectl -n "$NS" rollout status deploy/lettra-api --timeout=4m
redirection_api
appel GET "/api/v1/customers/$client" "" "$dossier" >/dev/null
[[ $(statut) != 2* ]] || echec "dossier encore lisible après la destruction de la clé du cabinet"
echo "   dossier $dossier fermé : HTTP $(statut)"

step "Cellule kind en mode 5 : vérifiée de bout en bout"
