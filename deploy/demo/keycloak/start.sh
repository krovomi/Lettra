#!/bin/bash
# Démonstration seulement : le realm versionné (deploy/keycloak) complété du cabinet et de l'utilisateur fictifs,
# importé au premier démarrage. Bash seul : l'image Keycloak n'a ni jq ni python.
set -euo pipefail
src=${LETTRA_REALM_DIR:-/opt/lettra}
dest=${LETTRA_IMPORT_DIR:-/opt/keycloak/data/import}
realm=$(<"$src/realm-lettra.json")
additions=$(<"$src/demo-additions.json")
mkdir -p "$dest"
# Retire l'accolade fermante du realm et l'accolade ouvrante des ajouts : un seul objet JSON.
printf '%s,\n%s\n' "${realm%\}*}" "${additions#*\{}" > "$dest/realm-lettra.json"

# L'adresse publique entre dans le realm à l'import (URL de retour du BFF) : une base par adresse, pour passer
# de localhost au réseau local (ou changer d'adresse IP) sans réinitialiser à la main.
url=${LETTRA_PUBLIC_URL:?LETTRA_PUBLIC_URL manquant}
exec "${KC_SH:-/opt/keycloak/bin/kc.sh}" "$@" --db-url-path="${KC_DEMO_DB_ROOT:-/opt/keycloak/data}/base-${url//[^a-zA-Z0-9]/-}"
