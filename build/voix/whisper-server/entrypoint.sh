#!/bin/sh
# Modèle ggml présent et intact, sinon téléchargé une fois depuis le dépôt public de whisper.cpp et vérifié par
# l'empreinte SHA-1 publiée et par le SHA-256 épinglé quand il l'est (comme WhisperModelStore sur le poste) ; puis le
# serveur, sur toutes les interfaces du conteneur.
set -eu

case "$WHISPER_MODEL" in
  tiny)  sha1=bd577a113a864445d4c299885e0cb97d4ba92b5f; sha256= ;;
  base)  sha1=465707469ff3a37a2b9b8d8f89f2f99de7299dac; sha256=60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe ;;
  small) sha1=55356645c2b361a969dfd0ef2c5a50d530afd8d5; sha256= ;;
  *) echo "Modèle inconnu : $WHISPER_MODEL (tiny, base ou small)" >&2; exit 2 ;;
esac

intact() {
  [ -f "$1" ] && [ "$(sha1sum "$1" | cut -d' ' -f1)" = "$sha1" ] \
    && { [ -z "$sha256" ] || [ "$(sha256sum "$1" | cut -d' ' -f1)" = "$sha256" ]; }
}

model="/models/ggml-$WHISPER_MODEL.bin"
if ! intact "$model"; then
  echo "Téléchargement de ggml-$WHISPER_MODEL.bin"
  curl -fsSL --retry 4 -o "$model.part" "${WHISPER_MODEL_BASE_URL:-https://huggingface.co/ggerganov/whisper.cpp/resolve/main}/ggml-$WHISPER_MODEL.bin"
  if ! intact "$model.part"; then
    echo "Empreinte incorrecte pour ggml-$WHISPER_MODEL.bin : fichier écarté" >&2
    rm -f "$model.part"
    exit 1
  fi
  mv "$model.part" "$model"
fi

exec whisper-server -m "$model" --host 0.0.0.0 --port 8080 "$@"
