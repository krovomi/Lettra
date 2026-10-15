#!/usr/bin/env bash
# Contrôle les factures électroniques produites par les tests :
#   <xml>/cii/*.xml et <xml>/ubl/*.xml (Lettra.Persistence.Tests, variable LETTRA_EINVOICE_SAMPLES)
#   <pdf>/Factur-X-*.pdf, facultatif (Lettra.UI.Tests, variable LETTRA_PDF_SAMPLES)
# 1. Schematron EN 16931 officiel de la Commission européenne (ConnectingEurope/eInvoicing-EN16931, EUPL 1.2), en XSLT :
#    une assertion « fatal » non tenue fait échouer le contrôle ; les avertissements sont affichés.
# 2. Mustang : schémas XSD CII et UBL, Schematron Factur-X, et pour un PDF son PDF/A-3 et sa déclaration Factur-X.
set -euo pipefail
samples="${1:?dossier des factures électroniques à contrôler}"
pdfs="${2:-}"
here="$(cd "$(dirname "$0")" && pwd)"
release="validation-1.3.17"
rules="$here/rules/$release"

if [[ ! -d "$here/lib" ]]; then
  mvn -q -f "$here/pom.xml" dependency:copy-dependencies -DoutputDirectory="$here/lib"
fi

declare -A xslt=([cii]="cii/xslt/EN16931-CII-validation.xslt" [ubl]="ubl/xslt/EN16931-UBL-validation.xslt")
for syntax in cii ubl; do
  if [[ ! -f "$rules/${xslt[$syntax]}" ]]; then
    mkdir -p "$(dirname "$rules/${xslt[$syntax]}")"
    curl -fsSL "https://raw.githubusercontent.com/ConnectingEurope/eInvoicing-EN16931/$release/${xslt[$syntax]}" -o "$rules/${xslt[$syntax]}"
  fi
done

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
total=0
failed=0
all=()
for syntax in cii ubl; do
  shopt -s nullglob
  files=("$samples/$syntax"/*.xml)
  shopt -u nullglob
  if [[ ${#files[@]} -eq 0 ]]; then
    echo "::error::Aucune facture $syntax dans $samples/$syntax : les tests n'ont pas produit d'échantillons"; exit 1
  fi

  for file in "${files[@]}"; do
    all+=("$file")
    total=$((total + 1))
    svrl="$work/$syntax-$(basename "$file" .xml).svrl"
    if ! java -cp "$here/lib/*" net.sf.saxon.Transform -s:"$file" -xsl:"$rules/${xslt[$syntax]}" -o:"$svrl" 2>"$work/saxon.log"; then
      cat "$work/saxon.log" >&2
      echo "::error::Saxon n'a pas pu lire $file"; exit 1
    fi
    report=$(python3 -I "$here/svrl.py" "$svrl")
    fatal=$(grep -c $'^fatal\t' <<<"$report" || true)
    if [[ "$fatal" -gt 0 ]]; then
      failed=$((failed + 1))
      echo "FAIL $syntax/$(basename "$file")"
    else
      echo "PASS $syntax/$(basename "$file")"
    fi
    [[ -n "$report" ]] && sed 's/^/     /' <<<"$report"
  done
done

if [[ "$failed" -gt 0 ]]; then
  echo "::error::$failed factures électroniques sur $total enfreignent EN 16931"; exit 1
fi
echo "EN 16931 : $total factures électroniques conformes (CII et UBL, $release)"

if [[ -n "$pdfs" ]]; then
  shopt -s nullglob
  facturx=("$pdfs"/Factur-X-*.pdf)
  shopt -u nullglob
  if [[ ${#facturx[@]} -eq 0 ]]; then
    echo "::error::Aucun Factur-X dans $pdfs : les tests n'ont pas produit d'échantillons"; exit 1
  fi
  all+=("${facturx[@]}")
fi

if ! java -cp "$here/lib/*" "$here/MustangCheck.java" "${all[@]}" 2>/dev/null; then
  echo "::error::Mustang refuse au moins une facture électronique (schéma XSD, Factur-X ou PDF/A-3)"; exit 1
fi
echo "Mustang : ${#all[@]} fichiers conformes (XSD CII et UBL, Factur-X EN 16931)"
