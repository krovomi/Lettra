#!/usr/bin/env python3
"""Annotations GitHub pour les tests en échec : nom, message et début de la pile, lus dans les fichiers .trx.

Le journal d'un job n'est pas toujours lisible (artefacts et journaux bloqués hors de GitHub) ; les annotations le sont.

    python3 build/tests/annoter-echecs.py TestResults
"""
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def echapper(texte: str) -> str:
    return texte.replace("%", "%25").replace("\r", "").replace("\n", "%0A")


def echapper_titre(texte: str) -> str:
    return echapper(texte).replace(":", "%3A").replace(",", "%2C")


def main(racine: str) -> int:
    echecs = 0
    for trx in sorted(Path(racine).rglob("*.trx")):
        for resultat in ET.parse(trx).getroot().iterfind(".//t:UnitTestResult", NS):
            if resultat.get("outcome") != "Failed":
                continue
            echecs += 1
            message = resultat.findtext(".//t:ErrorInfo/t:Message", "", NS).strip()
            pile = "\n".join(resultat.findtext(".//t:ErrorInfo/t:StackTrace", "", NS).strip().splitlines()[:8])
            corps = f"{message[:1500]}\n{pile}".strip()
            print(f"::error title={echapper_titre('Test en échec : ' + (resultat.get('testName') or '?'))}::{echapper(corps)}")
    print(f"{echecs} test(s) en échec")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else "TestResults"))
