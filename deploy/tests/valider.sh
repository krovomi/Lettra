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

step "Collecteur OpenTelemetry : base seule, puis avec les destinations rendues par le chart"
# Même image que la cellule (values.yaml) ; otelcol-contrib dans le PATH évite Docker en local.
COLLECTOR_IMAGE=$(sed -n 's/^  image: \(otel\/opentelemetry-collector-contrib:.*\)$/\1/p' "$ROOT/deploy/helm/lettra-observability/values.yaml")
[[ -n "$COLLECTOR_IMAGE" ]] || { echo "image du collecteur introuvable dans lettra-observability/values.yaml" >&2; exit 1; }
otelcol_validate() { # fichiers de configuration, fusionnés dans l'ordre comme dans le pod
  local args=() mounts=() i=0 f
  if command -v otelcol-contrib >/dev/null; then
    for f in "$@"; do args+=("--config=$f"); done
    PROMETHEUS_OTLP=http://p FOURNISSEUR_AUTORISATION=x otelcol-contrib validate "${args[@]}"
  else
    for f in "$@"; do mounts+=(-v "$f:/conf/$i.yaml:ro"); args+=("--config=/conf/$i.yaml"); i=$((i + 1)); done
    docker run --rm "${mounts[@]}" -e PROMETHEUS_OTLP=http://p -e FOURNISSEUR_AUTORISATION=x "$COLLECTOR_IMAGE" validate "${args[@]}"
  fi
}
backends() { # options helm -> backends.yaml rendu
  helm template o lettra-observability --show-only templates/collector.yaml "$@" \
    | yq 'select(.kind == "ConfigMap") | .data["backends.yaml"]'
}
if command -v otelcol-contrib >/dev/null || command -v docker >/dev/null; then
  RENDU=$(mktemp -d)
  otelcol_validate "$OBS/collector.yaml" && echo "   collector.yaml seul (essai local, démonstration) : valide"
  backends > "$RENDU/defaut.yaml"
  backends --set collector.traces.tempo.enabled=true --set collector.logs.loki.enabled=true > "$RENDU/cellule.yaml"
  backends --set collector.fournisseur.enabled=true --set collector.fournisseur.endpoint=https://otlp.exemple.fr \
    --set 'collector.fournisseur.signals={traces,metrics,logs}' --set collector.fournisseur.authSecret=s > "$RENDU/fournisseur.yaml"
  for cas in defaut cellule fournisseur; do
    otelcol_validate "$OBS/collector.yaml" "$RENDU/$cas.yaml" && echo "   collector.yaml + backends.yaml ($cas) : valide"
  done
  grep -q 'otlp/tempo' "$RENDU/cellule.yaml" && grep -q 'otlphttp/loki' "$RENDU/cellule.yaml" \
    || { echo "Tempo et Loki doivent être branchés quand la cellule les active" >&2; exit 1; }
  ! grep -qE 'otlp/tempo|otlphttp/(loki|fournisseur)' "$RENDU/defaut.yaml" \
    || { echo "aucune destination de traces ni de journaux ne doit être active par défaut" >&2; exit 1; }
  rm -rf "$RENDU"
else
  echo "   ni otelcol-contrib ni docker : validation du collecteur ignorée" >&2
fi
helm template o lettra-observability --set collector.fournisseur.enabled=true --set collector.fournisseur.endpoint=https://otlp.exemple.fr \
  --set collector.fournisseur.authSecret=s | kubeconform_ -
if helm template o lettra-observability --set collector.fournisseur.enabled=true >/dev/null 2>&1; then
  echo "un fournisseur sans adresse doit être refusé" >&2; exit 1
fi
echo "   fournisseur sans adresse : refus vérifié"

step "OpenTofu : format et validation (sans état ni identifiants)"
cd "$ROOT/deploy/opentofu"
tofu fmt -recursive -check
for dir in modules/ovh-cluster modules/cell-bootstrap envs/local envs/recette; do
  (cd "$dir" && tofu init -backend=false -input=false >/dev/null && tofu validate -no-color | sed "s#^#   $dir : #")
done
echo; echo "deploy/ : tout est valide."
