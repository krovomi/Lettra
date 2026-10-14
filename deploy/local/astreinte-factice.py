"""Astreinte factice pour l'essai local : enregistre chaque webhook reçu (un fichier JSON lines par chemin)."""
import json
import sys
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

OUT = Path(sys.argv[2])


class Handler(BaseHTTPRequestHandler):
    def do_POST(self):  # noqa: N802
        body = self.rfile.read(int(self.headers.get("Content-Length", 0)))
        with (OUT / (self.path.strip("/") or "racine")).with_suffix(".jsonl").open("a") as f:
            f.write(json.dumps(json.loads(body)) + "\n")
        self.send_response(200)
        self.end_headers()

    def log_message(self, *args):
        pass


HTTPServer(("127.0.0.1", int(sys.argv[1])), Handler).serve_forever()
