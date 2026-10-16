#!/usr/bin/env bash
# Modèle ggml de whisper.cpp pour le workflow « Voix » : téléchargé depuis le dépôt public de whisper.cpp, puis
# vérifié par l'empreinte SHA-1 que whisper.cpp publie (la même que WhisperModelStore vérifie sur le poste).
# Affiche aussi le SHA-256, pour l'épingler.
# Usage : build/voix/model.sh <tiny|base|small> <dossier>
set -euo pipefail

size="${1:?tiny, base ou small}"
dir="${2:?dossier}"
case "$size" in
  tiny)  sha1=bd577a113a864445d4c299885e0cb97d4ba92b5f ;;
  base)  sha1=465707469ff3a37a2b9b8d8f89f2f99de7299dac ;;
  small) sha1=55356645c2b361a969dfd0ef2c5a50d530afd8d5 ;;
  *) echo "Modèle inconnu : $size" >&2; exit 2 ;;
esac

file="$dir/ggml-$size.bin"
mkdir -p "$dir"
if [ ! -f "$file" ] || [ "$(sha1sum "$file" | cut -d' ' -f1)" != "$sha1" ]; then
  curl -fsSL --retry 4 -o "$file.part" "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-$size.bin"
  mv "$file.part" "$file"
fi

actual="$(sha1sum "$file" | cut -d' ' -f1)"
if [ "$actual" != "$sha1" ]; then
  echo "Empreinte SHA-1 incorrecte pour ggml-$size.bin : $actual (attendu $sha1)" >&2
  rm -f "$file"
  exit 1
fi

echo "ggml-$size.bin vérifié. SHA-1 $sha1, SHA-256 $(sha256sum "$file" | cut -d' ' -f1)"
