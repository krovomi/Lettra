#!/usr/bin/env bash
# Corpus vocal du workflow « Voix » : chaque phrase de tests/Lettra.Speech.Tests/Corpus/phrases.json est lue par une
# voix de synthèse (espeak-ng), puis convertie au format de Whisper (WAV PCM 16 bits, mono, 16 kHz).
# Usage : build/voix/corpus.sh <dossier de sortie>
set -euo pipefail

out="${1:?dossier de sortie}"
root="$(cd "$(dirname "$0")/../.." && pwd)"
phrases="$root/tests/Lettra.Speech.Tests/Corpus/phrases.json"
mkdir -p "$out"

jq -r '.phrases[] | [.id, .language, .text] | @tsv' "$phrases" | while IFS=$'\t' read -r id language text; do
  raw="$out/$id.raw.wav"
  # Débit un peu lent et pause entre les mots : une voix de synthèse est plus difficile à suivre qu'une vraie voix.
  espeak-ng -v "$language" -s 140 -g 4 -w "$raw" "$text" < /dev/null
  ffmpeg -nostdin -loglevel error -y -i "$raw" -ar 16000 -ac 1 -sample_fmt s16 "$out/$id.wav"
  rm -f "$raw"
done

echo "Corpus : $(find "$out" -name '*.wav' | wc -l) enregistrements dans $out"
