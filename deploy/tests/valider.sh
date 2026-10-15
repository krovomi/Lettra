#!/usr/bin/env bash
# Validation hors cloud de tout ce que deploy/ décrit (CI : .github/workflows/deploy.yml ; en local : même commande).
# Aucun identifiant requis : plan et application réels se font dans la pipeline de la cellule.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
OBS="$ROOT/deploy/helm/lettra-observability/files"
KUBE_VERSION=${KUBE_VERSION:-1.33.0}
CRDS='https://raw.githubusercontent.com/datreeio/CRDs-catalog/main/{{.Group}}/{{.ResourceKind}}_{{.ResourceAPIVersion}}.json'
step() { printf '\n== %s\n' "$*"; }

step "Règles Prometheus : syntaxe et pannes injectées"
promtool check rules "$OBS"/rules/*.yml
promtool test rules "$OBS"/tests/*.yml

step "Alertmanager : configuration et routage par gravité"
amtool check-config "$OBS/alertmanager.yml"
route() { # étiquettes… -> destinataires attendus
  local expected=${*: -1}; local got
  got=$(amtool config routes test --config.file="$OBS/alertmanager.yml" "${@:1:$#-1}")
  [[ "$got" == "$expected" ]] || { echo "routage ${*:1:$#-1} : attendu $expected, obtenu $got" >&2; exit 1; }
  echo "   ${*:1:$#-1} → $got"
}
route severity=S1 category=availability cell=c alertname=X "lettra-supervision,astreinte-appel"
route severity=S2 cell=c alertname=X "lettra-supervision,astreinte-ticket"
route severity=S3 cell=c alertname=X "lettra-supervision"
route severity=S4 cell=c alertname=X "lettra-supervision"
route alertname=Watchdog severity=none "battement"

step "Collecteur OpenTelemetry"
if command -v docker >/dev/null; then
  docker run --rm -v "$OBS/collector.yaml:/c.yaml:ro" -e PROMETHEUS_OTLP=http://p -e LOKI_OTLP=http://l -e TEMPO_OTLP=t:4317 \
    otel/opentelemetry-collector-contrib:0.136.0 validate --config=/c.yaml && echo "   collector.yaml valide"
else
  echo "   docker absent : validation du collecteur ignorée" >&2
fi

step "Helm : lint, rendu de chaque taille et validation des schémas Kubernetes et CRD"
cd "$ROOT/deploy/helm"
helm dependency build lettra-cell >/dev/null
kubeconform_() { kubeconform -strict -summary -kubernetes-version "$KUBE_VERSION" -schema-location default -schema-location "$CRDS" "$@"; }
for size in S M L XL; do
  helm lint lettra-cell -f lettra-cell/values-taille-$size.yaml --set lettra-api.image.tag=ci --set lettra-worker.image.tag=ci --quiet
  helm template c lettra-cell -f lettra-cell/values-taille-$size.yaml --set lettra-api.image.tag=ci --set lettra-worker.image.tag=ci | kubeconform_ -
done
helm template pr lettra-cell -f lettra-cell/values-pr.yaml --set lettra-api.image.tag=ci | kubeconform_ -
helm template c lettra-cell -f ../tests/kind/valeurs-ci.yaml | kubeconform_ -

step "Keycloak de la cellule (lettra-keycloak)"
KC_REQUIS=(--set hostname=https://auth.example.test --set publicUrl=https://app.example.test)
helm lint lettra-keycloak "${KC_REQUIS[@]}" --quiet
helm template k lettra-keycloak "${KC_REQUIS[@]}" | kubeconform_ -
helm template k lettra-keycloak "${KC_REQUIS[@]}" --set database.cnpg.enabled=false \
  --set database.host=pg.example.test --set database.existingSecret=keycloak-base | kubeconform_ -
# Le realm importé est exactement le realm versionné (lien files/realm-lettra.json → deploy/keycloak/).
helm template k lettra-keycloak "${KC_REQUIS[@]}" --show-only templates/realm.yaml \
  | sed -n '/realm-lettra.json: |/,$p' | tail -n +2 \
  | python3 -c 'import json, sys; sys.exit(json.load(sys.stdin) != json.load(open(sys.argv[1])))' "$ROOT/deploy/keycloak/realm-lettra.json" \
  || { echo "le ConfigMap du realm diffère de deploy/keycloak/realm-lettra.json" >&2; exit 1; }
echo "   realm importé = deploy/keycloak/realm-lettra.json"
# Recette : l'API vérifie les jetons auprès du Keycloak de la cellule ; l'émetteur reste l'adresse publique.
recette=$(helm template c lettra-cell -f lettra-cell/values-taille-S.yaml -f ../cells/recette/values.yaml \
  --set lettra-api.image.tag=ci --set lettra-worker.image.tag=ci)
kubeconform_ - <<<"$recette"
grep -q 'name: Lettra__Identity__Oidc__Authority, value: "http://keycloak:8080/realms/lettra"' <<<"$recette" \
  || { echo "recette : l'API ne pointe pas sur le Keycloak de la cellule" >&2; exit 1; }
grep -q 'name: KC_HOSTNAME, value: "https://auth.recette.lettra.fr"' <<<"$recette" \
  || { echo "recette : adresse publique de Keycloak absente" >&2; exit 1; }
echo "   recette : API → http://keycloak:8080/realms/lettra, émetteur https://auth.recette.lettra.fr"
for manque in hostname publicUrl existingSecret; do
  if helm template k lettra-keycloak "${KC_REQUIS[@]}" --set "$manque=" >/dev/null 2>&1; then
    echo "un Keycloak sans $manque doit être refusé" >&2; exit 1
  fi
done
echo "   adresse publique, URL de l'API et secret du coffre obligatoires : refus vérifiés"
helm lint lettra-observability --quiet && helm template o lettra-observability | kubeconform_ -
helm lint ../argocd/lettra-racine --set imageTag=ci --quiet && helm template r ../argocd/lettra-racine --set imageTag=ci | kubeconform_ -
kubeconform_ ../argocd/pr-environnements.yaml
if helm template c lettra-cell --set lettra-api.image.tag=ci --set lettra-worker.image.tag=ci \
     --set 'postgres.clusters[0].name=x' --set 'postgres.clusters[0].activeDossierBudget=900' >/dev/null 2>&1; then
  echo "un cluster au-delà du budget de dossiers actifs doit être refusé" >&2; exit 1
fi
echo "   budget de dossiers actifs par cluster : refus vérifié"
if helm template c lettra-cell --set lettra-api.image.tag=ci --set lettra-worker.image.tag=ci \
     --set lettra-api.oidc.authority= >/dev/null 2>&1; then
  echo "une API sans realm Keycloak doit être refusée" >&2; exit 1
fi
echo "   realm Keycloak obligatoire : refus vérifié"

step "OpenTofu : format et validation (sans état ni identifiants)"
cd "$ROOT/deploy/opentofu"
tofu fmt -recursive -check
for dir in modules/ovh-cluster modules/cell-bootstrap envs/local envs/recette; do
  (cd "$dir" && tofu init -backend=false -input=false >/dev/null && tofu validate -no-color | sed "s#^#   $dir : #")
done
echo; echo "deploy/ : tout est valide."
