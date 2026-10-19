#!/usr/bin/env python3
"""Vérifie la pile Grafana de la démonstration (deploy/demo/compose.observabilite.yaml) après un passage k6.

Par l'API HTTP de Grafana seulement (ses sources de données servent de relais) :
  1. Grafana répond ; les sources Prometheus, Tempo et Loki provisionnées répondent, avec leurs liens
     (exemplaire → trace, trace → journaux, journal → trace) ;
  2. les trois tableaux de bord sont provisionnés dans le dossier « Lettra » ;
  3. chaque requête de chaque panneau rend des données : PromQL dans Prometheus, TraceQL dans Tempo, LogQL dans Loki ;
     seuls les panneaux listés dans SANS_DONNEES_EN_DEMO peuvent rester vides, avec leur raison ;
  4. les courbes de latence ont des exemplaires, dont au moins une trace existe dans Tempo et a ses journaux dans Loki ;
  5. les règles d'alerte de la cellule sont chargées et visibles depuis Grafana (Watchdog active).

Les données attendues arrivent par lots (export OTLP toutes les 10 s, règles toutes les 30 s) : chaque vérification est
reprise jusqu'à ATTENTE secondes. Bilan dans la sortie et, avec --bilan, dans un fichier Markdown (résumé de la CI).

    GRAFANA_ADMIN_PASSWORD=admin-demo python3 deploy/tests/grafana/verifier.py --bilan /tmp/bilan.md

Bibliothèque standard seulement. Données fictives : rien de ce qui est lu n'est réécrit hors de la sortie.
"""
import argparse
import base64
import json
import os
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

TABLEAUX = {"lettra-slo": "Lettra · Objectifs de service", "lettra-activite": "Lettra · Activité", "lettra-metier": "Lettra · Métier",
            "lettra-pilotage-cabinet": "Lettra · Pilotage du cabinet", "lettra-pilotage-dossier": "Lettra · Pilotage d'un dossier",
            "lettra-administration": "Lettra · Administration"}

# Panneaux sans données dans la démonstration, et pourquoi. Tout autre panneau vide fait échouer la vérification.
SANS_DONNEES_EN_DEMO = {
    ("lettra-slo", "PgBouncer : clients en attente (dossiers actifs au-delà du budget)"): "pas de PostgreSQL ni de PgBouncer (SQLite)",
    ("lettra-slo", "Connexions PostgreSQL utilisées / max_connections"): "pas de PostgreSQL (SQLite)",
    ("lettra-administration", "Requêtes et erreurs de l'API"): "aucune erreur 5xx attendue",
    ("lettra-administration", "Connexions refusées (24 h)"): "aucun échec de connexion n'est garanti",
    ("lettra-administration", "Connexions réussies et refusées"): "aucune connexion n'est garantie",
    ("lettra-administration", "Postes synchronisés par dossier"): "aucune application installée reliée au serveur",
}

# Variables de Grafana remplacées pour interroger les sources directement.
VARIABLES = {"$__rate_interval": "2m", "$__interval": "1m", "$__range": "1h", '="$dossier"': '=~".+"'}


class Grafana:
    def __init__(self, url, user, password):
        self.url = url.rstrip("/")
        self.auth = "Basic " + base64.b64encode(f"{user}:{password}".encode()).decode()

    def get(self, path, params=None):
        query = "?" + urllib.parse.urlencode(params) if params else ""
        request = urllib.request.Request(self.url + path + query, headers={"Authorization": self.auth, "Accept": "application/json"})
        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                corps = response.read()
                try:
                    return response.status, json.loads(corps or b"null")
                except ValueError:
                    return response.status, corps.decode("utf-8", "replace")
        except urllib.error.HTTPError as error:
            return error.code, None
        except (urllib.error.URLError, TimeoutError, ConnectionError):
            return 0, None

    def prometheus(self, path, params):
        return self.get(f"/api/datasources/proxy/uid/prometheus/api/v1/{path}", params)

    def tempo(self, path, params=None):
        return self.get(f"/api/datasources/proxy/uid/tempo{path}", params)

    def loki(self, path, params):
        return self.get(f"/api/datasources/proxy/uid/loki/loki/api/v1/{path}", params)


def substituer(expression):
    """Variables de Grafana remplacées ; un « or vector(0) » final retiré : c'est la série elle-même qui doit exister."""
    for variable, valeur in VARIABLES.items():
        expression = expression.replace(variable, valeur)
    return re.sub(r"\s+or\s+vector\(0\)\s*$", "", expression)


def reessayer(attente, verification):
    """Reprend la vérification jusqu'à ce qu'elle rende autre chose que None, ou jusqu'à la fin de l'attente."""
    fin = time.monotonic() + attente
    while True:
        resultat = verification()
        if resultat is not None or time.monotonic() >= fin:
            return resultat
        time.sleep(5)


def panneaux(tableau):
    for panneau in tableau.get("panels", []):
        yield panneau
        yield from panneau.get("panels", [])


def type_source(panneau, cible):
    source = cible.get("datasource") or panneau.get("datasource") or {"type": "prometheus"}
    return source.get("type", "prometheus") if isinstance(source, dict) else "prometheus"


def main():
    arguments = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    arguments.add_argument("--url", default=os.environ.get("GRAFANA_URL", "http://localhost:3000"))
    arguments.add_argument("--utilisateur", default="admin")
    arguments.add_argument("--attente", type=int, default=int(os.environ.get("ATTENTE", "180")))
    arguments.add_argument("--bilan", help="fichier Markdown du bilan (résumé de la CI)")
    options = arguments.parse_args()
    mot_de_passe = os.environ.get("GRAFANA_ADMIN_PASSWORD") or sys.exit("GRAFANA_ADMIN_PASSWORD manquant (deploy/demo/.env)")
    grafana = Grafana(options.url, options.utilisateur, mot_de_passe)
    echecs, lignes = [], []

    def constat(ok, sujet, detail=""):
        lignes.append((ok, sujet, detail))
        print(f"   {'ok ' if ok is True else ('-- ' if ok is None else 'ÉCHEC')} {sujet}{' : ' + detail if detail else ''}")
        if ok is False:
            echecs.append(f"{sujet} {detail}".strip())

    print("== Grafana et sources de données")
    def sante_grafana():
        reponse = grafana.get("/api/health")
        return reponse if reponse[0] == 200 else None
    statut, sante = reessayer(options.attente, sante_grafana) or (0, None)
    constat(statut == 200 and sante.get("database") == "ok", "Grafana répond", (sante or {}).get("version", ""))
    statut, sources = grafana.get("/api/datasources")
    sources = {s["uid"]: s for s in (sources or [])}
    for uid, genre in (("prometheus", "prometheus"), ("tempo", "tempo"), ("loki", "loki")):
        constat(sources.get(uid, {}).get("type") == genre, f"source {uid} provisionnée")
    liens = {
        "exemplaire → trace (Prometheus → Tempo)": any(d.get("datasourceUid") == "tempo" for d in sources.get("prometheus", {}).get("jsonData", {}).get("exemplarTraceIdDestinations", [])),
        "trace → journaux (Tempo → Loki)": sources.get("tempo", {}).get("jsonData", {}).get("tracesToLogsV2", {}).get("datasourceUid") == "loki",
        "journal → trace (Loki → Tempo)": any(d.get("datasourceUid") == "tempo" for d in sources.get("loki", {}).get("jsonData", {}).get("derivedFields", [])),
    }
    for sujet, ok in liens.items():
        constat(ok, f"lien {sujet}")
    constat(grafana.prometheus("query", {"query": "up"})[0] == 200, "Prometheus joignable depuis Grafana")
    constat(grafana.tempo("/api/status/buildinfo")[0] == 200, "Tempo joignable depuis Grafana")
    constat(grafana.loki("labels", {})[0] == 200, "Loki joignable depuis Grafana")

    print("== Tableaux de bord provisionnés")
    _, trouves = grafana.get("/api/search", {"type": "dash-db", "tag": "lettra"})
    trouves = {d["uid"]: d for d in (trouves or [])}
    for uid, titre in TABLEAUX.items():
        d = trouves.get(uid, {})
        constat(d.get("title") == titre and d.get("folderTitle") == "Lettra", f"{titre} ({uid})", d.get("folderTitle", "absent"))

    print("== Données de chaque panneau")
    maintenant = int(time.time())
    exemplaires = []
    for uid, titre in TABLEAUX.items():
        statut, reponse = grafana.get(f"/api/dashboards/uid/{uid}")
        if statut != 200:
            constat(False, f"{titre} illisible", str(statut))
            continue
        for panneau in panneaux(reponse["dashboard"]):
            cibles = panneau.get("targets", [])
            if not cibles:
                continue
            nom = f"{titre} › {panneau.get('title')}"
            raison = SANS_DONNEES_EN_DEMO.get((uid, panneau.get("title")))
            vides = []
            for cible in cibles:
                genre = type_source(panneau, cible)
                if genre == "prometheus":
                    expression = substituer(cible["expr"])
                    def serie(e=expression):
                        s, r = grafana.prometheus("query", {"query": e})
                        return r["data"]["result"] if s == 200 and r and r["data"]["result"] else None
                    if not (reessayer(0 if raison else options.attente, serie)):
                        vides.append(cible.get("legendFormat") or expression)
                    elif cible.get("exemplar"):
                        s, r = grafana.prometheus("query_exemplars", {"query": expression, "start": maintenant - 1800, "end": int(time.time())})
                        trouve = [e["labels"]["trace_id"] for serie_ in (r or {}).get("data", []) for e in serie_.get("exemplars", []) if "trace_id" in e.get("labels", {})]
                        exemplaires.extend(trouve)
                        if not trouve:
                            vides.append(f"exemplaires de {cible.get('legendFormat') or expression}")
                elif genre == "tempo":
                    def traces(q=cible["query"]):
                        s, r = grafana.tempo("/api/search", {"q": q, "limit": 20, "start": maintenant - 3600, "end": int(time.time())})
                        return r["traces"] if s == 200 and r and r.get("traces") else None
                    if not reessayer(options.attente, traces):
                        vides.append(cible["query"])
                elif genre == "loki":
                    def journaux(q=cible["expr"]):
                        s, r = grafana.loki("query_range", {"query": q, "limit": 20, "start": f"{maintenant - 3600}000000000"})
                        return r["data"]["result"] if s == 200 and r and r["data"]["result"] else None
                    if not reessayer(options.attente, journaux):
                        vides.append(cible["expr"])
            if raison:
                constat(None if vides else True, nom, f"sans données en démonstration ({raison})" if vides else "")
            else:
                constat(not vides, nom, "vide : " + " ; ".join(vides) if vides else "")

    print("== Liens : exemplaire → trace → journaux")
    avec_journaux = None
    for trace_id in list(dict.fromkeys(exemplaires))[:60]:
        if grafana.tempo(f"/api/v2/traces/{trace_id}")[0] != 200:
            continue
        s, r = grafana.loki("query_range", {"query": f'{{service_name=~"lettra-.+"}} | trace_id="{trace_id}"', "limit": 5, "start": f"{maintenant - 3600}000000000"})
        if s == 200 and r and r["data"]["result"]:
            avec_journaux = trace_id
            break
    constat(bool(exemplaires), "exemplaires portant un trace_id", f"{len(set(exemplaires))} trace(s)")
    constat(avec_journaux is not None, "une trace d'exemplaire existe dans Tempo et a ses journaux dans Loki", avec_journaux or "aucune")

    print("== Alertes de la cellule")
    s, r = grafana.get("/api/prometheus/prometheus/api/v1/rules")
    groupes = {g["name"]: g for g in (r or {}).get("data", {}).get("groups", [])}
    constat({"lettra-slo-alertes", "lettra-disponibilite"} <= set(groupes), "règles Prometheus de la cellule visibles dans Grafana", ", ".join(sorted(groupes)))
    watchdog = any(regle.get("name") == "Watchdog" and regle.get("state") == "firing" for g in groupes.values() for regle in g.get("rules", []))
    constat(watchdog, "Watchdog active (la chaîne d'alerte tourne)")

    if options.bilan:
        with open(options.bilan, "w", encoding="utf-8") as fichier:
            fichier.write("### Grafana de la démonstration : tableaux de bord et données\n\n| | Vérification | Détail |\n|---|---|---|\n")
            for ok, sujet, detail in lignes:
                marque = "✅" if ok is True else ("➖" if ok is None else "❌")
                cellule = detail.replace("|", "/")
                fichier.write(f"| {marque} | {sujet} | {cellule} |\n")

    if echecs:
        print(f"\nÉCHEC : {len(echecs)} vérification(s)", file=sys.stderr)
        for echec in echecs:
            print(f"   {echec}", file=sys.stderr)
            if os.environ.get("GITHUB_ACTIONS") == "true":
                # Annotation lisible sans le journal du job (titre : la vérification en échec).
                print(f"::error title=Grafana::{echec.replace('%', '%25').replace(chr(10), '%0A')}")
        sys.exit(1)
    print("\nGrafana de la démonstration : tout est en place et chaque tableau de bord a des données.")


if __name__ == "__main__":
    main()
