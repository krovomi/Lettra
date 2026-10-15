#!/usr/bin/env bash
# Valide en PDF/A-3b (veraPDF) les PDF produits par les tests (Lettra.UI.Tests, variable LETTRA_PDF_SAMPLES).
# Un Factur-X (phase 5) est un PDF/A-3 qui embarque son XML : cette garde le prouve dès la phase 3.
set -euo pipefail
samples="${1:?dossier des PDF à valider}"
here="$(cd "$(dirname "$0")" && pwd)"

shopt -s nullglob
files=("$samples"/*.pdf)
if [[ ${#files[@]} -eq 0 ]]; then
  echo "::error::Aucun PDF dans $samples : les tests n'ont pas produit d'échantillons"; exit 1
fi

if [[ ! -d "$here/lib" ]]; then
  mvn -q -f "$here/pom.xml" dependency:copy-dependencies -DoutputDirectory="$here/lib"
fi

report=$(java -cp "$here/lib/*" org.verapdf.apps.GreenfieldCliWrapper --flavour 3b --format text -v "${files[@]}" 2>/dev/null)
echo "$report"
passed=$(grep -c '^PASS ' <<<"$report" || true)
if [[ "$passed" -ne ${#files[@]} ]]; then
  echo "::error::$((${#files[@]} - passed)) PDF non conformes PDF/A-3b sur ${#files[@]}"; exit 1
fi
echo "PDF/A-3b : ${#files[@]} documents conformes"
