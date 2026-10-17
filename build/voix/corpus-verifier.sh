#!/usr/bin/env bash
# Vérifie le corpus vocal commité (tests/Lettra.Speech.Tests/Corpus/wav) avant que Whisper ne le transcrive : un WAV
# par phrase de phrases.json, ni plus ni moins, et chacun avec le SHA-256 de corpus.sha256. Le même corpus à chaque
# run, donc le même score à code égal. Pour le régénérer : build/voix/corpus.sh.
# Prérequis : jq, sha256sum.
# Usage : build/voix/corpus-verifier.sh
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
corpus="$root/tests/Lettra.Speech.Tests/Corpus"
manifest="$corpus/corpus.sha256"

expected="$(jq -r '.phrases[].id + ".wav"' "$corpus/phrases.json" | sort)"
listed="$(awk '{print $2}' "$manifest" | sort)"
present="$(cd "$corpus/wav" && find . -maxdepth 1 -name '*.wav' -printf '%f\n' | sort)"

if [ "$expected" != "$listed" ] || [ "$expected" != "$present" ]; then
  echo "::error title=Corpus vocal::phrases.json, corpus.sha256 et Corpus/wav ne listent pas les mêmes enregistrements. Régénérer avec build/voix/corpus.sh."
  diff <(echo "$expected") <(echo "$listed") || true
  diff <(echo "$expected") <(echo "$present") || true
  exit 1
fi

if ! (cd "$corpus/wav" && sha256sum --check --strict "$manifest"); then
  echo "::error title=Corpus vocal::Un WAV du corpus ne correspond pas à corpus.sha256. Régénérer avec build/voix/corpus.sh."
  exit 1
fi

echo "Corpus vocal vérifié : $(echo "$expected" | wc -l) enregistrements conformes à corpus.sha256."
