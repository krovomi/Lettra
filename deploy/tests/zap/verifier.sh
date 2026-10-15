#!/usr/bin/env bash
# Après le scan ZAP : échoue sur toute alerte de risque faible ou plus dont la règle n'est pas classée dans rules.tsv.
# (zap-baseline.py classe en WARN, sans échec, les règles absentes du fichier : une nouvelle constatation passerait.)
#
#   deploy/tests/zap/verifier.sh <rapport.json> [rules.tsv]
set -euo pipefail
REPORT=${1:?rapport JSON de ZAP}
RULES=${2:-$(dirname "$0")/rules.tsv}

known=$(grep -v '^#' "$RULES" | cut -f1 | grep -E '^[0-9]+$' | jq -Rn '[inputs]')
unknown=$(jq --argjson known "$known" -r '
  [.site[]?.alerts[]? | select((.riskcode | tonumber) >= 1) | select(.pluginid as $id | $known | index($id) | not)]
  | unique_by(.pluginid)[]
  | "\(.pluginid)\t\(.riskdesc)\t\(.alert) (\(.count) occurrence(s))"' "$REPORT")

all=$(jq -r '.site[]?.alerts[]? | "\(.pluginid)\t\(.riskdesc)\t\(.alert) (\(.count))"' "$REPORT" | sort -u)
echo "Alertes relevées (règle, risque, nom, occurrences) :"
echo "$all" | sed 's/^/  /'

# En CI : une annotation récapitulative, lisible sans télécharger les rapports.
annotate() { [[ -n "${GITHUB_ACTIONS:-}" ]] && printf '::%s title=%s::%s\n' "$1" "$2" "$(printf '%s' "$3" | sed 's/%/%25/g' | awk 1 ORS='%0A')"; return 0; }
annotate notice "ZAP : alertes relevées" "${all:-aucune}"

if [[ -n "$unknown" ]]; then
  echo
  echo "Nouvelles constatations, à corriger dans l'API ou à classer dans $RULES avec leur justification :" >&2
  echo "$unknown" | sed 's/^/  /' >&2
  annotate error "ZAP : constatations non classées" "$unknown"
  exit 1
fi
echo "Aucune constatation nouvelle."
