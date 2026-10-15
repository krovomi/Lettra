# lettra-observability

Ce que Lettra ajoute à la pile d'observabilité de chaque cellule (annexe G) : collecteur OpenTelemetry filtrant,
règles Prometheus (SLO et infrastructure), routage Alertmanager et tableaux de bord Grafana. Déployé par la racine
Argo CD (`deploy/argocd/lettra-racine`, vague 0) dans l'espace de noms `lettra-observabilite`, après
kube-prometheus-stack, Loki et Tempo (vague -1).

## Chemin des signaux

```
lettra-api ┐                      ┌─ métriques ─► Prometheus (récepteur OTLP)   toujours
           ├─ OTLP ─► collecteur ─┼─ traces ────► Tempo                         collector.traces.tempo.enabled
lettra-worker ┘  filtrant         ├─ journaux ──► Loki                          collector.logs.loki.enabled
                                  ├─ au choix ──► fournisseur hébergé en France collector.fournisseur.enabled
                                  └─ tous ──────► debug (résumé par lot dans le journal du collecteur)
```

### Côté applications

`Lettra.Infrastructure.Observability` (appelé par `LettraBootstrap` pour l'API comme pour le Worker) exporte traces,
métriques et journaux en OTLP **seulement si un point de collecte est configuré** :

| Réglage | Effet |
|---|---|
| `OTEL_EXPORTER_OTLP_ENDPOINT` (et `OTEL_EXPORTER_OTLP_PROTOCOL`) | Variables standard, posées par les charts `lettra-api` et `lettra-worker` (`otel.endpoint`, par défaut `http://lettra-otel-collector.lettra-observabilite:4317`, gRPC). Elles l'emportent sur tout le reste. |
| `Lettra:Observability:Otlp:Endpoint` et `:Protocol` (`grpc` ou `http/protobuf`) | Même chose depuis la configuration de Lettra (appsettings, poste ou serveur du cabinet). |
| `Lettra:Observability:ExportEnabled` | Export vers l'adresse par défaut d'OpenTelemetry (`localhost:4317`). |
| rien | Aucun export : rien ne sort du processus (poste isolé, tests). |

Attributs de ressource de chaque signal : `service.name` (`Lettra:Observability:ServiceName`), `service.namespace`
(`lettra`), `service.version` (version informative de l'assembly, SHA du commit compris), `deployment.environment.name`
et `deployment.environment` (`Lettra:Observability:Environment`), `lettra.cell` (`Lettra:Observability:Cell`),
`lettra.deployment.mode`, plus `service.instance.id` et `k8s.namespace.name` posés par les charts.

Avant tout export, l'application retire déjà : la requête des adresses (`url.query`, partie après `?` de `url.full`),
les corps et en-têtes HTTP, l'adresse du client, le SQL et ses paramètres (`db.statement`, `db.query.text`,
`db.query.parameter.*` ; EF Core n'est pas instrumenté), le message et la pile des exceptions (journaux comme
événements de span) ; elle masque toute valeur qui ressemble à un e-mail, un IBAN, une TVA, un SIREN ou un SIRET. Le
test `NoDossierDataInLogsTests` (tests/Lettra.Api.Tests) le vérifie sur un parcours réel, sur ce qu'un exportateur en
mémoire reçoit à la place de l'exportateur OTLP.

### Collecteur

Image `otel/opentelemetry-collector-contrib`, version figée dans `values.yaml`. Deux fichiers de configuration,
fusionnés dans cet ordre :

- `files/collector.yaml`, la base, valide seule (reprise telle quelle par l'essai local `deploy/local/essai-panne.sh`
  et par la démonstration `deploy/demo/compose.otel.yaml`) : récepteurs OTLP gRPC 4317 et HTTP 4318, `memory_limiter`,
  second filtrage (`attributes/interdits`, `redaction/donnees-de-dossier`, `transform/evenements-et-corps`), `batch`,
  métriques vers Prometheus, exportateur `debug` en mode `basic` ;
- `backends.yaml`, rendu par le chart depuis `values.yaml` : exportateurs Tempo, Loki et fournisseur activés, et liste
  des exportateurs de chaque pipeline.

Les métriques partent en OTLP vers le récepteur de Prometheus plutôt que d'être exposées pour un scrape : les règles
SLO comptent sur `job="lettra/lettra-api"` et sur la disparition de `target_info` quand l'API s'arrête
(`LettraApiIndisponible`), que la poussée OTLP donne directement. Le collecteur expose ses propres métriques sur le
port 8888 (ServiceMonitor), surveillées par `infra.rules.yml`.

## Valeurs

| Clé | Défaut | Rôle |
|---|---|---|
| `collector.image` | `otel/opentelemetry-collector-contrib:0.136.0` | Version figée, montée par PR. |
| `collector.prometheusOtlp` | Prometheus de la cellule | Récepteur OTLP des métriques. |
| `collector.traces.tempo.enabled` / `.endpoint` | `false` / `tempo.lettra-observabilite:4317` | Traces vers Tempo (activé par la racine Argo CD). |
| `collector.logs.loki.enabled` / `.endpoint` | `false` / `http://loki-gateway.lettra-observabilite/otlp` | Journaux vers Loki (activé par la racine Argo CD). |
| `collector.fournisseur.enabled` | `false` | Emplacement réservé : fournisseur OTLP/HTTP hébergé en France. |
| `collector.fournisseur.endpoint` | `""` | Obligatoire s'il est activé (le rendu échoue sinon). |
| `collector.fournisseur.signals` | `[traces, logs]` | Signaux envoyés (`traces`, `metrics`, `logs`). |
| `collector.fournisseur.authSecret` | `""` | Secret existant, clé `authorization` (en-tête complet). |

Sans Tempo, Loki ni fournisseur, traces et journaux ne vont nulle part ailleurs que dans le résumé `debug` : rien
n'est perdu côté application, mais rien n'est conservé.

### Brancher un fournisseur hébergé en France

1. Validation du DPO : contrat de sous-traitance, hébergement et support dans l'Union européenne (en France de
   préférence), durées de conservation de l'annexe G §3 (journaux 30 jours, traces 7 jours).
2. Secret dans le coffre de la cellule, par exemple `lettra-observabilite-fournisseur`, clé `authorization`.
3. Valeurs de la cellule :

   ```yaml
   collector:
     fournisseur:
       enabled: true
       endpoint: https://otlp.fournisseur.example
       signals: [traces, logs]
       authSecret: lettra-observabilite-fournisseur
     # Pour remplacer Tempo et Loki plutôt que de s'y ajouter :
     traces: { tempo: { enabled: false } }
     logs: { loki: { enabled: false } }
   ```

Le filtrage du collecteur s'applique avant le fournisseur exactement comme avant Tempo et Loki.

## Vérifications

`deploy/tests/valider.sh` (job `valider` de `.github/workflows/deploy.yml`) : `otelcol-contrib validate` sur la base
seule, puis sur la base et le `backends.yaml` rendu pour trois cas (défaut, cellule avec Tempo et Loki, fournisseur
sur les trois signaux), par l'image du collecteur ou par `otelcol-contrib` s'il est dans le `PATH` ; refus d'un
fournisseur sans adresse ; `helm lint`, rendu et schémas Kubernetes (kubeconform) ; règles (`promtool`) et routage
(`amtool`).
