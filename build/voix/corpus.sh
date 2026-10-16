#!/usr/bin/env bash
# Corpus vocal du workflow « Voix » : chaque phrase de tests/Lettra.Speech.Tests/Corpus/phrases.json est lue par une
# voix de synthèse neuronale locale (Piper : fr_FR-siwis-medium, en_US-lessac-medium), puis convertie au format de
# Whisper (WAV PCM 16 bits, mono, 16 kHz). espeak-ng, essayé d'abord, est trop robotique en français : Whisper n'y
# reconnaissait presque rien, alors qu'il comprenait la même chaîne en anglais.
#
# Le corpus est déterministe : Piper tire d'ordinaire du bruit au hasard (intonation, durée des phonèmes), si bien
# que deux runs donnaient deux corpus et deux scores (7/12 puis 10/12 sans changer une ligne de code). Ici le bruit
# est à zéro (--noise-scale 0 --noise-w-scale 0), les versions de Piper et d'ONNX Runtime sont épinglées
# (build/voix/piper-requirements.txt), les voix sont vérifiées par leur SHA-256 (build/voix/voices.sha256) et chaque
# WAV produit doit avoir le SHA-256 attendu (build/voix/corpus.sha256). Un écart arrête le workflow.
#
# Prérequis : python3 -m pip install -r build/voix/piper-requirements.txt, ffmpeg, jq.
# Usage : build/voix/corpus.sh [--epingler] <dossier de sortie> [dossier des voix]
#   --epingler : réécrit voices.sha256 et corpus.sha256 d'après ce qui vient d'être produit (après un changement de
#                phrases.json, de voix ou de version de Piper), au lieu de les vérifier.
set -euo pipefail

pin=false
if [ "${1:-}" = "--epingler" ]; then pin=true; shift; fi
out="${1:?dossier de sortie}"
voices="${2:-$out/../voices}"
python="${PIPER_PYTHON:-python3}"
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
phrases="$root/tests/Lettra.Speech.Tests/Corpus/phrases.json"
mkdir -p "$out" "$voices"

voice_for() {
  case "$1" in
    fr) echo fr_FR-siwis-medium ;;
    en) echo en_US-lessac-medium ;;
    *) echo "Langue sans voix : $1" >&2; exit 2 ;;
  esac
}

# Vérifie (ou réécrit avec --epingler) le manifeste $1 pour les fichiers $3... du dossier $2.
# En cas d'écart, une seule annotation donne les lignes à coller dans le manifeste si le changement est voulu ; le
# script continue pour signaler tous les écarts d'un coup, puis échoue à la fin.
manifest() {
  local file="$1" dir="$2"; shift 2
  local actual
  actual="$(cd "$dir" && sha256sum -- "$@")"
  if $pin; then
    printf '%s\n' "$actual" > "$file"
    echo "Épinglé : ${file#"$root"/}"
    return
  fi
  if [ -f "$file" ] && diff -u "$file" <(printf '%s\n' "$actual"); then
    echo "Empreintes conformes : ${file#"$root"/}"
    return
  fi
  local lines="${actual//$'\n'/%0A}"
  echo "::error title=Empreintes de ${file#"$root"/}::SHA-256 obtenus (à coller dans le manifeste si le changement est voulu) :%0A$lines"
  failed=true
}

failed=false

voice_files=()
for language in fr en; do
  voice="$(voice_for "$language")"
  [ -f "$voices/$voice.onnx" ] || "$python" -m piper.download_voices --download-dir "$voices" "$voice"
  voice_files+=("$voice.onnx" "$voice.onnx.json")
done
manifest "$here/voices.sha256" "$voices" "${voice_files[@]}"

wavs=()
while IFS=$'\t' read -r id language text; do
  raw="$out/$id.raw.wav"
  printf '%s\n' "$text" | "$python" -m piper -m "$voices/$(voice_for "$language").onnx" -f "$raw" \
    --noise-scale 0 --noise-w-scale 0
  # -bitexact : ni version d'ffmpeg ni métadonnées variables dans l'en-tête du WAV.
  ffmpeg -nostdin -loglevel error -y -i "$raw" -map_metadata -1 -fflags +bitexact -flags:a +bitexact \
    -ar 16000 -ac 1 -sample_fmt s16 "$out/$id.wav"
  rm -f "$raw"
  wavs+=("$id.wav")
done < <(jq -r '.phrases[] | [.id, .language, .text] | @tsv' "$phrases")

echo "Corpus : ${#wavs[@]} enregistrements dans $out"
manifest "$here/corpus.sha256" "$out" "${wavs[@]}"
if $failed; then exit 1; fi
