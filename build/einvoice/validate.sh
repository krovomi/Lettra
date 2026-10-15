#!/usr/bin/env bash
# Contrôle les factures électroniques produites par les tests :
#   <xml>/cii/*.xml et <xml>/ubl/*.xml (Lettra.Persistence.Tests, variable LETTRA_EINVOICE_SAMPLES)
#   <pdf>/Factur-X-*.pdf, facultatif (Lettra.UI.Tests, variable LETTRA_PDF_SAMPLES)
# 1. Schematron EN 16931 officiel de la Commission européenne (ConnectingEurope/eInvoicing-EN16931, EUPL 1.2), en XSLT :
#    une assertion « fatal » non tenue fait échouer le contrôle ; les avertissements sont affichés.
# 2. Norme française XP Z12-012 (Schematron BR-FR « Flux 2 », livré avec Mustang, CII seulement) sur les factures
#    du Flux 2 (acheteur français identifié par son SIREN ; le reste relève de l'e-reporting) : toute règle BR-FR non
#    tenue, avertissement compris, fait échouer le contrôle. UBL n'a pas encore de Schematron BR-FR publié.
# 3. Mustang : schémas XSD CII et UBL, Schematron Factur-X, et pour un PDF son PDF/A-3 et sa déclaration Factur-X.
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

brfr_dir="$work/brfr"
mkdir -p "$brfr_dir"
(cd "$brfr_dir" && unzip -q -o "$here/lib/validator-"*.jar 'xslt/XP_Z12_012/*Flux2-Schematron-CII*.xsl')
brfr=$(ls "$brfr_dir"/xslt/XP_Z12_012/*Flux2-Schematron-CII*.xsl | sort | tail -1)
checked=0
failed=0
for file in "$samples/cii"/*.xml; do
  name="cii/$(basename "$file")"
  if [[ "$(python3 -I "$here/flux2.py" "$file")" != "yes" ]]; then
    echo "SKIP $name (hors Flux 2 : e-reporting)"
    continue
  fi
  checked=$((checked + 1))
  svrl="$work/brfr-$(basename "$file" .xml).svrl"
  if ! java -cp "$here/lib/*" net.sf.saxon.Transform -s:"$file" -xsl:"$brfr" -o:"$svrl" 2>"$work/saxon.log"; then
    cat "$work/saxon.log" >&2
    echo "::error::Saxon n'a pas pu appliquer les règles BR-FR à $file"; exit 1
  fi
  report=$(python3 -I "$here/svrl.py" "$svrl")
  if [[ -n "$report" ]]; then
    failed=$((failed + 1))
    echo "FAIL BR-FR $name"
    sed 's/^/     /' <<<"$report"
  else
    echo "PASS BR-FR $name"
  fi
done
if [[ "$checked" -eq 0 ]]; then
  echo "::error::Aucune facture du Flux 2 parmi les échantillons CII : les règles BR-FR n'ont rien contrôlé"; exit 1
fi
if [[ "$failed" -gt 0 ]]; then
  echo "::error::$failed factures du Flux 2 sur $checked enfreignent la norme XP Z12-012 (BR-FR)"; exit 1
fi
echo "BR-FR : $checked factures du Flux 2 conformes ($(basename "$brfr"))"

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
