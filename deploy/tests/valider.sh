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
