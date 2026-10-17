#!/usr/bin/env bash
# Régénère le corpus vocal du workflow « Voix » (à la main, jamais en CI) : chaque phrase de
# tests/Lettra.Speech.Tests/Corpus/phrases.json est lue par une voix de synthèse neuronale locale (Piper :
# fr_FR-siwis-medium, en_US-lessac-medium), puis convertie au format de Whisper (WAV PCM 16 bits, mono, 16 kHz) dans
# tests/Lettra.Speech.Tests/Corpus/wav, avec ses empreintes dans tests/Lettra.Speech.Tests/Corpus/corpus.sha256.
# espeak-ng, essayé d'abord, est trop robotique en français : Whisper n'y reconnaissait presque rien.
#
# Le corpus est commité, et la CI ne fait que vérifier ses empreintes (build/voix/corpus-verifier.sh). Piper, relancé à
# chaque run, donnait un corpus différent et un score qui bougeait sans changement de code (7/12 commandes puis 10/12) :
# il tire du bruit au hasard, et même sans bruit les échantillons varient d'un processeur à l'autre (ONNX Runtime).
# Ici le bruit est tout de même à zéro (--noise-scale 0 --noise-w-scale 0), les versions sont épinglées
# (build/voix/piper-requirements.txt) et les voix vérifiées par SHA-256 (build/voix/voices.sha256), pour qu'une
# régénération ne change que ce qui doit changer.
#
# Prérequis : Python 3.13, python3 -m pip install -r build/voix/piper-requirements.txt, ffmpeg, jq, accès à
# huggingface.co (voix Piper).
# Usage : build/voix/corpus.sh [dossier des voix, par défaut voix/voices]
set -euo pipefail

python="${PIPER_PYTHON:-python3}"
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
voices="${1:-$root/voix/voices}"
corpus="$root/tests/Lettra.Speech.Tests/Corpus"
out="$corpus/wav"
mkdir -p "$voices"
rm -rf "$out" && mkdir -p "$out"

voice_for() {
  case "$1" in
    fr) echo fr_FR-siwis-medium ;;
    en) echo en_US-lessac-medium ;;
    *) echo "Langue sans voix : $1" >&2; exit 2 ;;
  esac
}

for language in fr en; do
  voice="$(voice_for "$language")"
  [ -f "$voices/$voice.onnx" ] || "$python" -m piper.download_voices --download-dir "$voices" "$voice"
done
if ! (cd "$voices" && sha256sum --check --strict --quiet "$here/voices.sha256"); then
  echo "Voix Piper différentes de build/voix/voices.sha256 : vérifier le changement avant de mettre à jour le manifeste." >&2
  (cd "$voices" && sha256sum ./*.onnx ./*.onnx.json) >&2
  exit 1
fi

while IFS=$'\t' read -r id language text; do
  raw="$out/$id.raw.wav"
  printf '%s\n' "$text" | "$python" -m piper -m "$voices/$(voice_for "$language").onnx" -f "$raw" \
    --noise-scale 0 --noise-w-scale 0
  # bitexact : pas de métadonnées variables (version d'ffmpeg) dans l'en-tête du WAV.
  ffmpeg -nostdin -loglevel error -y -i "$raw" -map_metadata -1 -fflags +bitexact -flags:a +bitexact \
    -ar 16000 -ac 1 -sample_fmt s16 "$out/$id.wav"
  rm -f "$raw"
done < <(jq -r '.phrases[] | [.id, .language, .text] | @tsv' "$corpus/phrases.json")

(cd "$out" && sha256sum -- *.wav) > "$corpus/corpus.sha256"
echo "Corpus : $(find "$out" -name '*.wav' | wc -l) enregistrements dans ${out#"$root"/}, empreintes dans ${corpus#"$root"/}/corpus.sha256"
"$here/corpus-verifier.sh"
