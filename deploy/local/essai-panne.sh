#!/usr/bin/env bash
# Essai de bout en bout de la chaîne d'alerte, sans Kubernetes (annexe C, sortie de phase 2 :
# « une panne injectée dans cette cellule déclenche une alerte S1 chez l'astreinte »).
#
#   Lettra.Api ──OTLP──► collecteur filtrant ──OTLP──► Prometheus ──règles──► Alertmanager ─┬─► astreinte (factice)
#                                                                                           └─► webhook Supervision (API)
# Étapes : 1) une alerte S2 synthétique traverse Alertmanager jusqu'au module Supervision et au ticket d'astreinte ;
#          2) l'API est arrêtée : la règle LettraApiIndisponible appelle l'astreinte en S1, sans dépendre de Lettra.
#
# Prérequis : SDK .NET 10, Docker (collecteur), prometheus, alertmanager et amtool dans le PATH, python3.
set -euo pipefail

ROOT=$(cd "$(dirname "$0")/../.." && pwd)
FILES="$ROOT/deploy/helm/lettra-observability/files"
RUN="$ROOT/deploy/local/.run"
TOKEN="jeton-essai-local"
API=http://127.0.0.1:5080
rm -rf "$RUN" && mkdir -p "$RUN/secrets" "$RUN/astreinte" "$RUN/prometheus"
PIDS=()
cleanup() { kill "${PIDS[@]}" 2>/dev/null || true; docker rm -f lettra-essai-collecteur >/dev/null 2>&1 || true; }
trap cleanup EXIT

step() { printf '\n== %s\n' "$*"; }
wait_for() { # description, délai en secondes, commande
  local what=$1 timeout=$2; shift 2
  for ((i = 0; i < timeout; i += 5)); do "$@" >/dev/null 2>&1 && { echo "   ok ($what, ${i}s)"; return 0; }; sleep 5; done
  echo "   ÉCHEC : $what après ${timeout}s" >&2; return 1
}

step "Astreinte factice et secrets d'Alertmanager"
python3 "$ROOT/deploy/local/astreinte-factice.py" 9099 "$RUN/astreinte" & PIDS+=($!)
echo -n "http://127.0.0.1:9099/battement" > "$RUN/secrets/battement-url"
echo -n "http://127.0.0.1:9099/appel" > "$RUN/secrets/astreinte-appel-url"
echo -n "http://127.0.0.1:9099/ticket" > "$RUN/secrets/astreinte-ticket-url"
echo -n "$API/internal/supervision/alertmanager" > "$RUN/secrets/supervision-url"
echo -n "$TOKEN" > "$RUN/secrets/supervision-token"
sed "s#/etc/alertmanager/secrets/lettra-alerting#$RUN/secrets#" "$FILES/alertmanager.yml" > "$RUN/alertmanager.yml"
alertmanager --config.file="$RUN/alertmanager.yml" --storage.path="$RUN/am" --web.listen-address=127.0.0.1:9093 \
  --cluster.listen-address= > "$RUN/alertmanager.log" 2>&1 & PIDS+=($!)

step "Prometheus (récepteur OTLP, règles de la cellule)"
{
  echo "global: { scrape_interval: 15s, evaluation_interval: 15s, external_labels: { cell: locale } }"
  echo "alerting: { alertmanagers: [ { static_configs: [ { targets: ['127.0.0.1:9093'] } ] } ] }"
  echo "rule_files: ['$FILES/rules/*.yml']"
  cat "$FILES/prometheus-otlp.yml"
} > "$RUN/prometheus.yml"
prometheus --config.file="$RUN/prometheus.yml" --storage.tsdb.path="$RUN/prometheus" --web.listen-address=127.0.0.1:9090 \
  --web.enable-otlp-receiver > "$RUN/prometheus.log" 2>&1 & PIDS+=($!)

step "Collecteur OpenTelemetry filtrant (même configuration que la cellule)"
docker run -d --rm --name lettra-essai-collecteur --network host -v "$FILES/collector.yaml:/conf/collector.yaml:ro" \
  -e PROMETHEUS_OTLP=http://127.0.0.1:9090/api/v1/otlp -e LOKI_OTLP=http://127.0.0.1:3100/otlp -e TEMPO_OTLP=127.0.0.1:4319 \
  otel/opentelemetry-collector-contrib:0.136.0 --config=/conf/collector.yaml > /dev/null

step "Lettra.Api (mode 2, développement), export OTLP toutes les 5 s"
dotnet build "$ROOT/src/Hosts/Lettra.Api" -c Release -v quiet -nologo > "$RUN/build.log"
(cd "$ROOT/src/Hosts/Lettra.Api" && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=$API \
  OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:4317 OTEL_METRIC_EXPORT_INTERVAL=5000 \
  Lettra__Observability__ServiceName=lettra-api Lettra__Observability__Cell=locale \
  Lettra__Supervision__WebhookToken=$TOKEN Lettra__Supervision__PublicBaseAddress=http://127.0.0.1:5080/ \
  exec dotnet bin/Release/net10.0/Lettra.Api.dll > "$RUN/api.log" 2>&1) & API_PID=$!; PIDS+=($API_PID)
wait_for "API prête" 120 curl -fs $API/health
for _ in $(seq 20); do curl -s -o /dev/null -H "X-Dossier: 0199e3a0-0000-7000-8000-00000000d051" $API/api/v1/customers; done
has_series() { curl -fs "http://127.0.0.1:9090/api/v1/query" --data-urlencode "query=$1" | grep -q '"result":\[{'; }
wait_for "métriques de l'API reçues par Prometheus" 120 has_series 'target_info{job="lettra/lettra-api"}'
wait_for "histogramme HTTP avec la borne SLO de 300 ms" 60 has_series 'http_server_request_duration_seconds_bucket{job="lettra/lettra-api",le="0.3"}'

step "1) Alerte S2 synthétique : Alertmanager → Supervision (API) et ticket d'astreinte"
amtool alert add EssaiFonctionnel severity=S2 category=functional scope=cell cell=locale \
  --annotation=title="Essai de bout en bout" --alertmanager.url=http://127.0.0.1:9093
wait_for "ticket d'astreinte reçu" 120 grep -q EssaiFonctionnel "$RUN/astreinte/ticket.jsonl"
wait_for "alerte ingérée et notifiée par le module Supervision" 60 grep -q "Notification S2 « Essai de bout en bout »" "$RUN/api.log"

step "2) Panne injectée : arrêt de l'API"
kill "$API_PID"; date -u +"   arrêt à %H:%M:%S"
wait_for "astreinte appelée en S1 pour LettraApiIndisponible" 600 grep -q LettraApiIndisponible "$RUN/astreinte/appel.jsonl"
date -u +"   appel reçu à %H:%M:%S"

step "Contrôles"
grep LettraApiIndisponible "$RUN/astreinte/appel.jsonl" | python3 -c '
import json,sys
a=[x for l in sys.stdin for x in json.loads(l)["alerts"] if x["labels"]["alertname"]=="LettraApiIndisponible"][0]
print("   étiquettes :", {k: a["labels"][k] for k in ("severity","category","scope","cell")})
print("   titre      :", a["annotations"]["title"])'
if grep -qiE "Fromagerie|@|FR[0-9]{2}[A-Z0-9]{4}" "$RUN/astreinte/"*.jsonl; then echo "   ÉCHEC : donnée sensible dans une notification" >&2; exit 1; fi
echo "   aucune donnée de dossier dans les notifications"
echo; echo "Essai réussi. Journaux : $RUN"
