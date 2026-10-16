#!/usr/bin/env bash
# Corpus vocal du workflow « Voix » : chaque phrase de tests/Lettra.Speech.Tests/Corpus/phrases.json est lue par une
# voix de synthèse neuronale locale (Piper : fr_FR-siwis-medium, en_US-lessac-medium), puis convertie au format de
# Whisper (WAV PCM 16 bits, mono, 16 kHz). espeak-ng, essayé d'abord, est trop robotique en français : Whisper n'y
# reconnaissait presque rien, alors qu'il comprenait la même chaîne en anglais.
# Prérequis : python3 -m pip install piper-tts==1.3.0, ffmpeg, jq.
# Usage : build/voix/corpus.sh <dossier de sortie> [dossier des voix]
set -euo pipefail

out="${1:?dossier de sortie}"
voices="${2:-$out/../voices}"
python="${PIPER_PYTHON:-python3}"
root="$(cd "$(dirname "$0")/../.." && pwd)"
phrases="$root/tests/Lettra.Speech.Tests/Corpus/phrases.json"
mkdir -p "$out" "$voices"

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

jq -r '.phrases[] | [.id, .language, .text] | @tsv' "$phrases" | while IFS=$'\t' read -r id language text; do
  raw="$out/$id.raw.wav"
  printf '%s\n' "$text" | "$python" -m piper -m "$voices/$(voice_for "$language").onnx" -f "$raw"
  ffmpeg -nostdin -loglevel error -y -i "$raw" -ar 16000 -ac 1 -sample_fmt s16 "$out/$id.wav"
  rm -f "$raw"
done

echo "Corpus : $(find "$out" -name '*.wav' | wc -l) enregistrements dans $out"
