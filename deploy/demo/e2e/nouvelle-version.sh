#!/usr/bin/env bash
# Fabrique une « nouvelle version » de l'application à partir d'un wwwroot publié, pour vérifier qu'un navigateur
# déjà ouvert la prend (mise-a-jour.mjs) : index.html reçoit <meta name="lettra-build" content="<marque>">, son
# empreinte et la version du manifeste du service worker changent, comme après une vraie nouvelle publication.
#
#   nouvelle-version.sh <dossier wwwroot> <marque>
set -euo pipefail
www="$1"
marque="$2"
index="$www/index.html"
assets="$www/service-worker-assets.js"

sed -i "s|<title>Lettra</title>|<title>Lettra</title>\n    <meta name=\"lettra-build\" content=\"$marque\" />|" "$index"
grep -q "name=\"lettra-build\" content=\"$marque\"" "$index"
empreinte="sha256-$(openssl dgst -sha256 -binary "$index" | base64 -w0)"

# L'entrée de index.html est la ligne « hash » juste avant « "url": "index.html" ».
python3 - "$assets" "$empreinte" "$marque" <<'PY'
import re, sys
path, digest, mark = sys.argv[1:]
text = open(path, encoding='utf-8').read()
text, n = re.subn(r'("hash": ")[^"]+("\s*,\s*"url": "index\.html")', lambda m: m.group(1) + digest + m.group(2), text)
assert n == 1, f"entrée index.html introuvable ({n})"
text, n = re.subn(r'("version": ")[^"]+(")', lambda m: m.group(1) + mark + m.group(2), text, count=1)
assert n == 1, "version introuvable"
open(path, 'w', encoding='utf-8').write(text)
PY

# Les variantes précompressées serviraient l'ancien contenu.
rm -f "$index.br" "$index.gz" "$assets.br" "$assets.gz"
echo "nouvelle version $marque : index.html $empreinte"
