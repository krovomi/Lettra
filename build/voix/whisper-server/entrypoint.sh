#!/bin/sh
# Modèle ggml présent et intact, sinon téléchargé une fois depuis le dépôt public de whisper.cpp et vérifié par
# l'empreinte SHA-1 publiée ; puis le serveur, sur toutes les interfaces du conteneur.
set -eu

case "$WHISPER_MODEL" in
  tiny)  sha1=bd577a113a864445d4c299885e0cb97d4ba92b5f ;;
  base)  sha1=465707469ff3a37a2b9b8d8f89f2f99de7299dac ;;
  small) sha1=55356645c2b361a969dfd0ef2c5a50d530afd8d5 ;;
  *) echo "Modèle inconnu : $WHISPER_MODEL (tiny, base ou small)" >&2; exit 2 ;;
esac

model="/models/ggml-$WHISPER_MODEL.bin"
if [ ! -f "$model" ] || [ "$(sha1sum "$model" | cut -d' ' -f1)" != "$sha1" ]; then
  echo "Téléchargement de ggml-$WHISPER_MODEL.bin"
  curl -fsSL --retry 4 -o "$model.part" "${WHISPER_MODEL_BASE_URL:-https://huggingface.co/ggerganov/whisper.cpp/resolve/main}/ggml-$WHISPER_MODEL.bin"
  if [ "$(sha1sum "$model.part" | cut -d' ' -f1)" != "$sha1" ]; then
    echo "Empreinte incorrecte pour ggml-$WHISPER_MODEL.bin : fichier écarté" >&2
    rm -f "$model.part"
    exit 1
  fi
  mv "$model.part" "$model"
fi

exec whisper-server -m "$model" --host 0.0.0.0 --port 8080 "$@"
