#!/usr/bin/env bash
# Essai d'une cellule en mode 5 (HostedSaaS) dans un cluster kind jetable (CI : job « mode5-kind » de
# .github/workflows/deploy.yml ; en local : même commande, avec docker, kind, kubectl, helm, curl, jq et openssl).
# Rien n'est appliqué à un cloud, aucune donnée réelle : un cabinet et un utilisateur fictifs, tirés au hasard.
#
# Installe l'opérateur CloudNativePG, un KMS OpenBao d'essai (openbao-essai.yaml) puis le chart lettra-cell
# (valeurs-mode5.yaml) : Keycloak, l'API et le Worker, un cluster PostgreSQL derrière PgBouncer. Puis vérifie, de bout
# en bout :
#   1. le Worker migre le catalogue (l'API ne migre rien en mode 5) ;
#   2. l'administrateur de la plateforme inscrit un cabinet par l'API, sans script : organisation et compte de son
#      administrateur dans Keycloak (changement de mot de passe exigé), cabinet au catalogue, KEK tirée par le KMS,
#      rôle administrateur-cabinet ; une seconde demande retrouve le même cabinet ;
#   3. un dossier créé par l'API passe à « actif », sa base d_… existe sur le cluster, appartient au rôle du cabinet et
#      refuse tout autre rôle ; la KEK du cabinet est tirée par le KMS et gardée enveloppée dans le catalogue ;
#   4. un client et une facture s'y créent, la facture se valide et se relit, y compris après redémarrage de l'API ;
#      l'e-mail du client est chiffré dans la base du dossier ; un poste relié (mode 4) reçoit le dossier, ouvre sa série
#      de factures et voit son intention appliquée une seule fois, même renvoyée ;
#   5. une création de dossier interrompue (serveur de bases indisponible, Worker arrêté) est reprise par le Worker ;
#   6. après une rotation de la clé racine du cabinet, le Worker réenveloppe sa KEK et tout reste lisible ;
#   7. l'effacement d'un dossier demandé par le cabinet : fermé aussitôt, annulable, puis, le délai de rétractation passé,
#      sa DEK détruite et sa base supprimée par le Worker ;
#   8. la clé racine détruite dans le KMS, les dossiers du cabinet ne s'ouvrent plus (effacement cryptographique) ;
#   9. le départ du cabinet demandé par la plateforme : annulable, puis, le délai passé, ses bases supprimées, sa clé
#      détruite, son rôle PostgreSQL et ses comptes supprimés ; seuls restent son identifiant, son nom et ses dates.
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
  catalogue 'SELECT "Id", "OrganisationId", "State", "ClusterId", "ErasureDueAt", "LastError" FROM catalog."Dossiers"' \
    > "$SORTIE/catalogue-dossiers.txt" 2>&1
  catalogue 'SELECT "Id", "State", "DepartureDueAt", "DepartedAt", now() FROM catalog."Organisations"' > "$SORTIE/catalogue-cabinets.txt" 2>&1
  # Les artefacts ne sont pas toujours lisibles : l'état du catalogue et les passes d'effacement vont aussi dans le journal.
  echo "--- catalogue : dossiers (id, cabinet, état, cluster, effacement dû, erreur), puis cabinets"
  cat "$SORTIE/catalogue-dossiers.txt" "$SORTIE/catalogue-cabinets.txt"
  echo "--- Worker : effacements et départs"
  grep -E -A2 'DossierErasure|CatalogDossierEraser|CatalogCabinetLifecycle' "$SORTIE/worker.log" | grep -v '^ *at ' | tail -n 40
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
etat() { catalogue "SELECT \"State\" FROM catalog.\"Dossiers\" WHERE \"Id\" = '$1'"; }
efface() { [[ $(etat "$1") == 3 ]]; }
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
# Secret du compte de service lettra-provisioning (installé dans Keycloak à l'étape 2), pour l'API et le Worker.
provisioning_secret=$(openssl rand -hex 32)
# L'administrateur de la plateforme n'est déclaré à l'API qu'une fois son compte Keycloak créé (étape 2 plus bas).
kubectl -n "$NS" create secret generic lettra-api --dry-run=client -o yaml \
  --from-literal=Lettra__Identity__Oidc__BffClientSecret="$bff_secret" \
  --from-literal=Lettra__Identity__Provisioning__ClientSecret="$provisioning_secret" | kubectl apply -f - >/dev/null
kubectl -n "$NS" create secret generic lettra-worker --dry-run=client -o yaml \
  --from-literal=Lettra__Observability__ServiceName=lettra-worker \
  --from-literal=Lettra__Identity__Provisioning__ClientSecret="$provisioning_secret" | kubectl apply -f - >/dev/null
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

step "2. Installation de la cellule dans Keycloak : compte de service, administrateur de la plateforme"
# Le jeton du realm master ne vit qu'une minute : un nouveau à chaque appel.
jeton_maitre() {
  curl -fsS "$KEYCLOAK/realms/master/protocol/openid-connect/token" \
    -d grant_type=password -d client_id=admin-cli -d username=lettra-amorcage --data-urlencode "password=$admin_password" \
    | jq -er .access_token
}
kc() { curl -fsS -H "Authorization: Bearer $(jeton_maitre)" -H 'Content-Type: application/json' "$@"; }
admin_realm="$KEYCLOAK/admin/realms/lettra"
# Compte de service de la plateforme : crée et supprime les organisations et les comptes des cabinets.
kc -X POST "$admin_realm/clients" -d "$(jq -n --arg s "$provisioning_secret" '{clientId: "lettra-provisioning", enabled: true,
  publicClient: false, serviceAccountsEnabled: true, standardFlowEnabled: false, directAccessGrantsEnabled: false, secret: $s}')"
client_provisioning=$(kc "$admin_realm/clients?clientId=lettra-provisioning" | jq -er '.[0].id')
compte_service=$(kc "$admin_realm/clients/$client_provisioning/service-account-user" | jq -er .id)
gestion=$(kc "$admin_realm/clients?clientId=realm-management" | jq -er '.[0].id')
kc -X POST "$admin_realm/users/$compte_service/role-mappings/clients/$gestion" -d "$(
  for role in manage-users view-users query-users manage-realm view-realm; do kc "$admin_realm/clients/$gestion/roles/$role"; done | jq -sc .)"
# Client d'essai : jeton par mot de passe, avec les mêmes claims que l'application (organisation, audience lettra-api).
kc -X POST "$admin_realm/clients" -d "$(jq '.clients[] | select(.clientId == "lettra-maui")
  | {clientId: "lettra-essai", publicClient: true, standardFlowEnabled: false, directAccessGrantsEnabled: true,
     defaultClientScopes, protocolMappers: [.protocolMappers[] | del(.id)]}' "$ROOT/deploy/keycloak/realm-lettra.json")"
# L'administrateur de la plateforme : un compte sans cabinet. Le realm ajoute l'enrôlement TOTP à tout nouveau compte :
# retiré pour ce compte d'essai (jeton par mot de passe).
mdp_plateforme=$(openssl rand -hex 16)
kc -X POST "$admin_realm/users" -d "$(jq -n --arg p "$mdp_plateforme" '{username: "plateforme", email: "plateforme@lettra.example",
  emailVerified: true, enabled: true, firstName: "Dominique", lastName: "Plateforme",
  credentials: [{type: "password", value: $p, temporary: false}]}')"
plateforme=$(kc "$admin_realm/users?username=plateforme&exact=true" | jq -er '.[0].id')
kc -X PUT "$admin_realm/users/$plateforme" -d '{"requiredActions": []}'
kubectl -n "$NS" create secret generic lettra-api --dry-run=client -o yaml \
  --from-literal=Lettra__Identity__Oidc__BffClientSecret="$bff_secret" \
  --from-literal=Lettra__Identity__Provisioning__ClientSecret="$provisioning_secret" \
  --from-literal=Lettra__Authorization__Users__0__Id="$plateforme" \
  --from-literal=Lettra__Authorization__Users__0__State=Active \
  --from-literal=Lettra__Authorization__Users__0__Assignments__0__Role=administrateur-plateforme \
  --from-literal=Lettra__Authorization__Users__0__Assignments__0__Scope=Plateforme \
  --from-literal=Lettra__Authorization__Users__0__Assignments__0__ScopeId=00000000-0000-0000-0000-000000000000 \
  | kubectl apply -f - >/dev/null
kubectl -n "$NS" rollout restart deploy/lettra-api >/dev/null
kubectl -n "$NS" rollout status deploy/lettra-api --timeout=4m
[[ $(catalogue "SELECT \"CellId\" FROM catalog.\"Clusters\" WHERE \"Id\" = 'pg-a'") == "$CELLULE" ]] \
  || echec "cluster pg-a absent du catalogue ou hors de la cellule"

redirection_api() {
  kubectl -n "$NS" port-forward svc/lettra-api 8081:80 > "$SORTIE/port-forward-api.log" 2>&1 & pids+=($!)
  attendre 60 "API joignable" curl -sf -o /dev/null "$API/health"
}
redirection_api
utilisateur_courant=plateforme
mot_de_passe_courant=$mdp_plateforme
jeton() {
  curl -fsS "$KEYCLOAK/realms/lettra/protocol/openid-connect/token" -d grant_type=password -d client_id=lettra-essai \
    -d "username=$utilisateur_courant" --data-urlencode "password=$mot_de_passe_courant" -d scope=openid | jq -er .access_token
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

step "2. Arrivée d'un cabinet fictif par l'API de la plateforme, sans script"
appel GET /api/v1/plateforme/cabinets | jq -e '.canOnboard == true' >/dev/null \
  || echec "la plateforme ne peut pas inscrire de cabinet : $(head -c 600 "$SORTIE/reponse.json")"
demande='{"name":"Cabinet mode 5 (fictif)","administratorName":"Camille Essai","administratorEmail":"admin@cabinet-mode5.example"}'
arrivee=$(appel POST /api/v1/plateforme/cabinets "$demande")
attendu 201 "arrivée du cabinet"
cabinet=$(jq -er .cabinet.id <<<"$arrivee")
utilisateur=$(jq -er .administratorId <<<"$arrivee")
appel POST /api/v1/plateforme/cabinets "$demande" >/dev/null
attendu 201 "arrivée redemandée"
[[ $(jq -r .cabinet.id "$SORTIE/reponse.json") == "$cabinet" ]] || echec "arrivée redemandée : un autre cabinet"
[[ $(kc "$admin_realm/organizations/$cabinet" | jq -r .name) == "Cabinet mode 5 (fictif)" ]] || echec "organisation Keycloak absente"
kc "$admin_realm/organizations/$cabinet/members" | jq -e --arg u "$utilisateur" 'map(.id) | index($u)' >/dev/null \
  || echec "administrateur du cabinet pas membre de son organisation"
kc "$admin_realm/users/$utilisateur" | jq -e '.requiredActions | index("UPDATE_PASSWORD")' >/dev/null \
  || echec "choix du mot de passe non exigé à la première connexion"
[[ $(catalogue "SELECT \"CellId\" FROM catalog.\"Organisations\" WHERE \"Id\" = '$cabinet'") == "$CELLULE" ]] \
  || echec "cabinet absent du catalogue"
[[ $(enveloppe) == vault:v1:* ]] || echec "KEK du cabinet pas tirée par le KMS : $(enveloppe)"
echo "   cabinet $cabinet inscrit : organisation, KEK, administrateur $utilisateur (invitation : $(jq -r .invitation <<<"$arrivee"))"
# L'administrateur suit le lien de l'invitation et choisit son mot de passe (pas de second facteur pour l'essai), puis
# se donne le rôle manager pour créer et ouvrir les dossiers.
mot_de_passe=$(openssl rand -hex 16)
kc -X PUT "$admin_realm/users/$utilisateur/reset-password" \
  -d "$(jq -n --arg p "$mot_de_passe" '{type: "password", value: $p, temporary: false}')"
kc -X PUT "$admin_realm/users/$utilisateur" -d '{"requiredActions": []}'
utilisateur_courant=admin@cabinet-mode5.example
mot_de_passe_courant=$mot_de_passe
appel POST "/api/v1/cabinets/$cabinet/attributions" \
  "{\"subjectKind\":\"User\",\"subjectId\":\"$utilisateur\",\"role\":\"manager\",\"scopeKind\":\"Cabinet\",\"scopeId\":\"$cabinet\"}" >/dev/null
[[ $(statut) == 2* ]] || echec "rôle manager : HTTP $(statut) $(head -c 600 "$SORTIE/reponse.json")"

step "3. Dossier créé par l'API : actif, base d_… sur le cluster"
dossier=$(appel POST "/api/v1/cabinets/$cabinet/dossiers" '{"name":"Dossier mode 5 (fictif)"}' | jq -r '.id // empty')
attendu 201 "création du dossier"
base="d_${dossier//-/}"
[[ $(catalogue "SELECT \"State\" FROM catalog.\"Dossiers\" WHERE \"Id\" = '$dossier'") == 1 ]] || echec "dossier $dossier pas actif"
[[ $(sql postgres "SELECT datname FROM pg_database WHERE datname = '$base'") == "$base" ]] || echec "base $base absente"
role_cabinet="c_${cabinet//-/}"
[[ $(sql postgres "SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname = '$base'") == "$role_cabinet" ]] \
  || echec "base $base : propriétaire inattendu ($(sql postgres "SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname = '$base'"))"
[[ $(sql "$base" "SELECT count(*) FROM pg_tables WHERE schemaname = 'public' AND tableowner <> '$role_cabinet'") == 0 ]] \
  || echec "base $base : tables d'un autre propriétaire que le rôle du cabinet"
# Le rôle d'un autre cabinet (ici un rôle quelconque) n'a pas le droit de se connecter à la base.
sql postgres "DROP ROLE IF EXISTS essai_autre_cabinet; CREATE ROLE essai_autre_cabinet LOGIN" >/dev/null
[[ $(sql postgres "SELECT has_database_privilege('essai_autre_cabinet', '$base', 'CONNECT')") == f ]] \
  || echec "base $base ouverte à un autre rôle que celui du cabinet"
sql postgres "DROP ROLE essai_autre_cabinet" >/dev/null
[[ $(catalogue "SELECT octet_length(\"WrappedDek\") FROM catalog.\"Dossiers\" WHERE \"Id\" = '$dossier'") == 60 ]] \
  || echec "DEK du dossier absente ou mal enveloppée"
appel GET /api/v1/dossiers | jq -e --arg d "$dossier" 'map(.id) | index($d)' >/dev/null || echec "dossier absent de la liste"
[[ $(enveloppe) == vault:v1:* ]] || echec "KEK du cabinet absente du catalogue ou pas enveloppée par le KMS : $(enveloppe)"
echo "   dossier $dossier actif ; base $base (propriétaire $role_cabinet, fermée aux autres rôles) ; DEK enveloppée (60 octets) ; KEK enveloppée par le KMS"

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

step "4. Poste relié (mode 4) sur la cellule : réception, intention rejouée, série du poste"
poste=$(cat /proc/sys/kernel/random/uuid)
appel_poste() { # méthode, chemin, corps : comme appel, au nom du poste relié, sur le dossier
  local args=(-sS -o "$SORTIE/reponse.json" -w '%{http_code}' -X "$1" "$API$2" -H "Authorization: Bearer $(jeton)"
    -H "X-Dossier: $dossier" -H "X-Lettra-Device: $poste")
  [[ -n "${3:-}" ]] && args+=(-H 'Content-Type: application/json' -d "$3")
  curl "${args[@]}" > "$SORTIE/statut"
  cat "$SORTIE/reponse.json"
}
recu=$(appel_poste GET /api/v1/sync/changes)
attendu 200 "réception des changements par le poste"
[[ $(jq -r --arg c "$client" '.customers[] | select(.id == $c) | .email' <<<"$recu") == compta@boulangerie.example ]] \
  || echec "le poste ne reçoit pas le client en clair : $(head -c 400 <<<"$recu")"
jq -e '.companyProfile.siren == "552100554"' <<<"$recu" >/dev/null || echec "fiche entreprise absente de la réception"
jeton_synchro=$(jq -r .token <<<"$recu")
appel_poste POST /api/v1/sync/device-series "{\"deviceId\":\"$poste\"}" | jq -e '.series[0].seriesCode == "FAC-P01"' >/dev/null \
  || echec "série du poste : HTTP $(statut) $(head -c 400 "$SORTIE/reponse.json")"
hors_ligne=$(cat /proc/sys/kernel/random/uuid)
intention="{\"deviceId\":\"$poste\",\"intents\":[{\"\$type\":\"customer.create\",\"idempotencyKey\":\"$(cat /proc/sys/kernel/random/uuid)\",
  \"entityId\":\"$hors_ligne\",\"recordedAt\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",\"data\":{\"kind\":\"Company\",\"name\":\"Client saisi hors ligne\",
  \"siret\":\"73282932000074\",\"email\":\"hors-ligne@client.example\",\"addressLine1\":\"2 rue du Train\",\"postalCode\":\"69001\",
  \"city\":\"Lyon\",\"countryCode\":\"FR\"}}]}"
[[ $(appel_poste POST /api/v1/sync/intents "$intention" | jq -c '.results[0] | [.outcome, .serverVersion]') == '["Applied",1]' ]] \
  || echec "intention du poste : $(head -c 600 "$SORTIE/reponse.json")"
# Renvoi après une réponse perdue : le même résultat, rien de rejoué.
[[ $(appel_poste POST /api/v1/sync/intents "$intention" | jq -c '.results[0] | [.outcome, .serverVersion]') == '["Applied",1]' ]] \
  || echec "renvoi de l'intention : $(head -c 600 "$SORTIE/reponse.json")"
[[ $(sql "$base" "SELECT count(*) FROM \"Customers\" WHERE \"Id\" = '$hors_ligne'") == 1 ]] || echec "client du poste absent ou créé deux fois"
[[ $(sql "$base" "SELECT \"Email\" FROM \"Customers\" WHERE \"Id\" = '$hors_ligne'") == lettra:enc:v1:* ]] \
  || echec "e-mail du client du poste en clair dans la base"
appel_poste GET "/api/v1/sync/changes?token=$jeton_synchro" | jq -e --arg c "$hors_ligne" '[.customers[].id] | index($c)' >/dev/null \
  || echec "réception suivante sans le client du poste"
appel GET /api/v1/sync/devices "" "$dossier" | jq -e --arg p "$poste" 'map(.deviceId) | index($p)' >/dev/null \
  || echec "poste absent du registre : $(head -c 400 "$SORTIE/reponse.json")"
echo "   poste $poste : dossier reçu (e-mails en clair), série FAC-P01 ouverte, intention appliquée une seule fois, poste au registre"

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

step "7. Effacement d'un dossier par le cabinet : annulable, puis DEK détruite et base supprimée par le Worker"
appel POST "/api/v1/cabinets/$cabinet/dossiers/$interrompu/effacement" >/dev/null
attendu 202 "demande d'effacement"
[[ $(etat "$interrompu") == 4 ]] || echec "dossier $interrompu pas en attente d'effacement : état $(etat "$interrompu")"
appel GET /api/v1/customers "" "$interrompu" >/dev/null
[[ $(statut) != 2* ]] || echec "dossier encore ouvert après la demande d'effacement"
appel DELETE "/api/v1/cabinets/$cabinet/dossiers/$interrompu/effacement" >/dev/null
attendu 200 "annulation de l'effacement"
appel GET /api/v1/customers "" "$interrompu" >/dev/null
attendu 200 "lecture du dossier rouvert"
appel POST "/api/v1/cabinets/$cabinet/dossiers/$interrompu/effacement" >/dev/null
attendu 202 "nouvelle demande d'effacement"
attendre 120 "effacement du dossier $interrompu par le Worker" efface "$interrompu"
[[ $(catalogue "SELECT coalesce(octet_length(\"WrappedDek\"), 0) FROM catalog.\"Dossiers\" WHERE \"Id\" = '$interrompu'") == 0 ]] \
  || echec "DEK du dossier effacé encore présente"
[[ $(sql postgres "SELECT count(*) FROM pg_database WHERE datname = 'd_${interrompu//-/}'") == 0 ]] || echec "base du dossier effacé encore présente"
[[ $(sql postgres "SELECT count(*) FROM pg_database WHERE datname = '$base'") == 1 ]] || echec "base d'un autre dossier supprimée"
echo "   dossier $interrompu effacé : DEK détruite, base supprimée ; le dossier $dossier reste intact"

step "8. Clé racine du cabinet détruite dans le KMS : ses dossiers ne s'ouvrent plus"
bao write "transit/keys/$cle_racine/config" deletion_allowed=true >/dev/null
bao delete "transit/keys/$cle_racine" >/dev/null
kill "${pids[-1]}" 2>/dev/null || true
kubectl -n "$NS" rollout restart deploy/lettra-api >/dev/null
kubectl -n "$NS" rollout status deploy/lettra-api --timeout=4m
redirection_api
appel GET "/api/v1/customers/$client" "" "$dossier" >/dev/null
[[ $(statut) != 2* ]] || echec "dossier encore lisible après la destruction de la clé du cabinet"
echo "   dossier $dossier fermé : HTTP $(statut)"

step "9. Départ du cabinet demandé par la plateforme : annulable, puis rien ne reste (bases, clé, comptes)"
utilisateur_courant=plateforme
mot_de_passe_courant=$mdp_plateforme
appel POST "/api/v1/plateforme/cabinets/$cabinet/depart" >/dev/null
attendu 202 "demande de départ"
[[ $(jq -r .state "$SORTIE/reponse.json") == Leaving ]] || echec "cabinet pas en partance"
appel DELETE "/api/v1/plateforme/cabinets/$cabinet/depart" >/dev/null
attendu 200 "annulation du départ"
appel POST "/api/v1/plateforme/cabinets/$cabinet/depart" >/dev/null
attendu 202 "nouvelle demande de départ"
parti() { [[ $(catalogue "SELECT \"State\" FROM catalog.\"Organisations\" WHERE \"Id\" = '$cabinet'") == 2 ]]; }
attendre 180 "départ du cabinet par le Worker" parti
for d in $(catalogue "SELECT replace(\"Id\"::text, '-', '') FROM catalog.\"Dossiers\" WHERE \"OrganisationId\" = '$cabinet'"); do
  [[ $(sql postgres "SELECT count(*) FROM pg_database WHERE datname = 'd_$d'") == 0 ]] || echec "base d_$d encore présente"
done
[[ $(catalogue "SELECT count(*) FROM catalog.\"Dossiers\" WHERE \"OrganisationId\" = '$cabinet' AND \"State\" <> 3") == 0 ]] \
  || echec "dossier du cabinet pas effacé"
[[ $(sql postgres "SELECT count(*) FROM pg_roles WHERE rolname = '$role_cabinet'") == 0 ]] || echec "rôle $role_cabinet encore présent"
[[ $(catalogue "SELECT count(*) FROM catalog.\"UserAccounts\" WHERE \"OrganisationId\" = '$cabinet'") == 0 ]] \
  || echec "comptes du cabinet encore au registre"
[[ $(catalogue "SELECT coalesce(\"WrappedKek\", 'aucune') FROM catalog.\"Organisations\" WHERE \"Id\" = '$cabinet'") == aucune ]] \
  || echec "KEK du cabinet encore au catalogue"
! bao read "transit/keys/$cle_racine" >/dev/null 2>&1 || echec "clé racine du cabinet encore dans le KMS"
[[ $(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $(jeton_maitre)" "$admin_realm/organizations/$cabinet") == 404 ]] \
  || echec "organisation Keycloak du cabinet encore présente"
[[ $(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $(jeton_maitre)" "$admin_realm/users/$utilisateur") == 404 ]] \
  || echec "compte Keycloak de l'administrateur encore présent"
echo "   cabinet $cabinet parti : bases et rôle supprimés, clé détruite, comptes supprimés ; reste : $(catalogue \
  "SELECT \"Name\" || ', parti le ' || \"DepartedAt\" FROM catalog.\"Organisations\" WHERE \"Id\" = '$cabinet'")"

step "Cellule kind en mode 5 : vérifiée de bout en bout"
