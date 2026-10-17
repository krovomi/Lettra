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
  otelcol_validate "$OBS/collector.yaml" "$ROOT/deploy/demo/otel/collecteur-observabilite.yaml" \
    && echo "   collector.yaml + destinations de la démonstration (compose.observabilite.yaml) : valide"
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

step "Démonstration avec Grafana : mêmes destinations et mêmes tableaux de bord que la cellule"
# Les destinations du collecteur de la démonstration sont le backends.yaml du chart, Tempo et Loki activés, aux adresses
# de la démonstration près : la démonstration passe par le même filtrage et les mêmes sorties qu'une cellule.
diff <(backends --set collector.traces.tempo.enabled=true --set collector.traces.tempo.endpoint=tempo:4317 \
         --set collector.logs.loki.enabled=true --set collector.logs.loki.endpoint=http://loki:3100/otlp | yq -o=json 'sort_keys(..)') \
     <(yq -o=json 'sort_keys(..)' "$ROOT/deploy/demo/otel/collecteur-observabilite.yaml") \
  || { echo "deploy/demo/otel/collecteur-observabilite.yaml diffère du rendu du chart (Tempo et Loki activés)" >&2; exit 1; }
echo "   destinations du collecteur de la démonstration = rendu du chart"
# Tableaux de bord : JSON valide, uid unique, rangés par Grafana (cellule : sidecar ; démonstration : provisionnement).
python3 - "$ROOT/deploy/helm/lettra-observability/dashboards" <<'PY'
import json, pathlib, sys
uids = {}
for path in sorted(pathlib.Path(sys.argv[1]).glob("*.json")):
    d = json.loads(path.read_text(encoding="utf-8"))
    assert d.get("uid") and d.get("title"), f"{path.name} : uid et titre obligatoires"
    assert d["uid"] not in uids, f"{path.name} : uid {d['uid']} déjà pris par {uids.get(d['uid'])}"
    assert "lettra" in d.get("tags", []), f"{path.name} : étiquette « lettra » manquante"
    uids[d["uid"]] = path.name
    print(f"   {path.name} : {d['title']} ({d['uid']})")
PY

step "OpenTofu : format et validation (sans état ni identifiants)"
cd "$ROOT/deploy/opentofu"
tofu fmt -recursive -check
for dir in modules/ovh-cluster modules/cell-bootstrap envs/local envs/recette; do
  (cd "$dir" && tofu init -backend=false -input=false >/dev/null && tofu validate -no-color | sed "s#^#   $dir : #")
done
echo; echo "deploy/ : tout est valide."
