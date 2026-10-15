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

echo "Alertes relevées (règle, risque, nom) :"
jq -r '.site[]?.alerts[]? | "  \(.pluginid)\t\(.riskdesc)\t\(.alert)"' "$REPORT" | sort -u

if [[ -n "$unknown" ]]; then
  echo
  echo "Nouvelles constatations, à corriger dans l'API ou à classer dans $RULES avec leur justification :" >&2
  echo "$unknown" | sed 's/^/  /' >&2
  exit 1
fi
echo "Aucune constatation nouvelle."
