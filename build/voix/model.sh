#!/usr/bin/env bash
# Modèle ggml de whisper.cpp pour le workflow « Voix » : téléchargé depuis le dépôt public de whisper.cpp, puis
# vérifié par l'empreinte SHA-1 que whisper.cpp publie et par le SHA-256 épinglé quand il l'est (les mêmes que
# WhisperModelStore vérifie sur le poste). Le SHA-256 obtenu est affiché et annoté en CI, pour l'épingler.
# Usage : build/voix/model.sh <tiny|base|small> <dossier>
set -euo pipefail

size="${1:?tiny, base ou small}"
dir="${2:?dossier}"
case "$size" in
  tiny)  sha1=bd577a113a864445d4c299885e0cb97d4ba92b5f; sha256= ;;
  base)  sha1=465707469ff3a37a2b9b8d8f89f2f99de7299dac; sha256=60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe ;;
  small) sha1=55356645c2b361a969dfd0ef2c5a50d530afd8d5; sha256= ;;
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

actual256="$(sha256sum "$file" | cut -d' ' -f1)"
[ -n "${GITHUB_ACTIONS:-}" ] && echo "::notice title=Modèle ggml-$size.bin::SHA-1 $actual, SHA-256 $actual256"
if [ -n "$sha256" ] && [ "$actual256" != "$sha256" ]; then
  echo "Empreinte SHA-256 incorrecte pour ggml-$size.bin : $actual256 (attendu $sha256)" >&2
  [ -n "${GITHUB_ACTIONS:-}" ] && echo "::error title=SHA-256 de ggml-$size.bin::obtenu $actual256, attendu $sha256"
  rm -f "$file"
  exit 1
fi

echo "ggml-$size.bin vérifié. SHA-1 $sha1, SHA-256 $actual256${sha256:+ (épinglé)}"
