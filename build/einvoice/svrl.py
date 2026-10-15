"""Liste les assertions échouées d'un rapport SVRL : drapeau, règle, emplacement, message (une par ligne, séparées par des tabulations)."""
import sys
import xml.etree.ElementTree as ET

SVRL = {"svrl": "http://purl.oclc.org/dsdl/svrl"}
for failed in ET.parse(sys.argv[1]).iterfind(".//svrl:failed-assert", SVRL):
    text = " ".join((failed.findtext("svrl:text", "", SVRL) or "").split())
    print("\t".join([failed.get("flag", "fatal"), failed.get("id", ""), failed.get("location", ""), text]))
