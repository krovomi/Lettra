#!/usr/bin/env bash
# Ajoute un cabinet au realm lettra : une organisation Keycloak par cabinet (deploy/keycloak/README.md).
# Affiche l'identifiant de l'organisation : c'est l'OrganisationId du cabinet à déclarer dans le catalogue Lettra.
#
#   KEYCLOAK_URL=https://auth.recette.lettra.fr KEYCLOAK_TOKEN=<jeton d'administration> \
#     deploy/keycloak/ajouter-cabinet.sh cabinet-exemple "Cabinet Exemple" cabinet-exemple.example
#
# Sans KEYCLOAK_TOKEN, le jeton est demandé au realm master avec KEYCLOAK_ADMIN et KEYCLOAK_ADMIN_PASSWORD (compte
# d'amorçage d'une cellule neuve). Nécessite curl et jq.
set -euo pipefail

alias_=${1:?usage : ajouter-cabinet.sh <alias> <nom> <domaine e-mail>}
nom=${2:?nom du cabinet manquant}
domaine=${3:?domaine e-mail du cabinet manquant}
url=${KEYCLOAK_URL:?KEYCLOAK_URL manquant}
realm=${KEYCLOAK_REALM:-lettra}

token=${KEYCLOAK_TOKEN:-}
if [[ -z "$token" ]]; then
  token=$(curl -fsS "$url/realms/master/protocol/openid-connect/token" \
    -d grant_type=password -d client_id=admin-cli \
    --data-urlencode "username=${KEYCLOAK_ADMIN:?KEYCLOAK_ADMIN ou KEYCLOAK_TOKEN manquant}" \
    --data-urlencode "password=${KEYCLOAK_ADMIN_PASSWORD:?KEYCLOAK_ADMIN_PASSWORD manquant}" | jq -er .access_token)
fi

organisation=$(jq -n --arg alias "$alias_" --arg nom "$nom" --arg domaine "$domaine" \
  '{alias: $alias, name: $nom, enabled: true, domains: [{name: $domaine, verified: false}]}')

entetes=$(curl -fsS -o /dev/null -D - -X POST "$url/admin/realms/$realm/organizations" \
  -H "Authorization: Bearer $token" -H 'Content-Type: application/json' -d "$organisation")
emplacement=$(tr -d '\r' <<<"$entetes" | sed -n 's/^[Ll]ocation: *//p')
[[ -n "$emplacement" ]] || { echo "Keycloak n'a pas renvoyé l'adresse de l'organisation créée" >&2; exit 1; }
echo "${emplacement##*/}"
