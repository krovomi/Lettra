#!/usr/bin/env bash
# Vérifie que chaque composant tiers vendu dans le dépôt a son LICENSE et figure dans THIRD-PARTY-NOTICES.md.
# Garde-fou de la notice MIT de Paperasse (annexe B, ADR-0006).
set -euo pipefail
cd "$(dirname "$0")/.."

status=0
notices=THIRD-PARTY-NOTICES.md
[[ -f "$notices" ]] || { echo "::error::$notices absent"; exit 1; }

# Tout dossier nommé paperasse hors bin/obj/poc doit porter la licence MIT et être cité.
while IFS= read -r dir; do
  if [[ ! -f "$dir/LICENSE" ]] || ! grep -q "MIT" "$dir/LICENSE"; then
    echo "::error::$dir : fichier LICENSE MIT manquant"; status=1
  fi
  if ! grep -qi "paperasse" "$notices" || ! grep -q "Permission is hereby granted" "$notices"; then
    echo "::error::$notices doit reproduire la notice MIT complète de Paperasse"; status=1
  fi
done < <(find . -type d -iname 'paperasse' -not -path '*/bin/*' -not -path '*/obj/*' -not -path './poc/*' -not -path './.git/*')

if [[ -d third_party ]]; then
  for dir in third_party/*/; do
    name=$(basename "$dir")
    [[ -f "$dir/LICENSE" ]] || { echo "::error::third_party/$name : LICENSE manquant"; status=1; }
    grep -qi "$name" "$notices" || { echo "::error::third_party/$name absent de $notices"; status=1; }
  done
fi

[[ $status -eq 0 ]] && echo "Avis de tiers : OK"
exit $status
