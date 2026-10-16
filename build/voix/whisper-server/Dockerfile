# Serveur whisper.cpp de la cellule (vague 5, lane D) : dictée transcrite sans quitter la cellule (ADR-0012).
# Construit depuis une version épinglée, sans optimisations propres à la machine de construction (GGML_NATIVE=OFF) :
# l'image publique « main » de whisper.cpp s'arrêtait au chargement du modèle sur les machines de la CI.
# Le modèle n'est pas dans l'image : il est téléchargé au premier démarrage dans /models, puis vérifié.
# Image de base épinglée par empreinte (debian:bookworm-slim) et source vérifiée par son commit : la même image dans la
# démonstration (deploy/demo/compose.yaml, service « whisper ») et dans le workflow « Voix ».
ARG DEBIAN_IMAGE=debian:bookworm-slim@sha256:7c7b2c966bc9ee8cedfeef67e0e279108992c77681fa595db4a9d65c06ccc587
FROM ${DEBIAN_IMAGE} AS build
RUN apt-get update -q && apt-get install -yq --no-install-recommends build-essential cmake git ca-certificates \
    && rm -rf /var/lib/apt/lists/*
ARG WHISPER_CPP_VERSION=v1.8.7
ARG WHISPER_CPP_COMMIT=48f628a84833905ee4a0658ee6d4a5c915ce1997
RUN git clone --depth 1 --branch "$WHISPER_CPP_VERSION" https://github.com/ggml-org/whisper.cpp /src \
    && test "$(git -C /src rev-parse HEAD)" = "$WHISPER_CPP_COMMIT" \
    && cmake -S /src -B /build -DCMAKE_BUILD_TYPE=Release -DGGML_NATIVE=OFF -DBUILD_SHARED_LIBS=OFF -DWHISPER_BUILD_TESTS=OFF \
    && cmake --build /build -j"$(nproc)" --target whisper-server

FROM ${DEBIAN_IMAGE}
RUN apt-get update -q && apt-get install -yq --no-install-recommends libgomp1 curl ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && useradd --uid 10001 --create-home whisper && mkdir /models && chown whisper /models
COPY --from=build /build/bin/whisper-server /usr/local/bin/whisper-server
COPY entrypoint.sh /usr/local/bin/entrypoint.sh
COPY LICENSE-whisper.cpp /usr/share/doc/whisper.cpp/LICENSE
USER whisper
VOLUME /models
EXPOSE 8080
ENV WHISPER_MODEL=base
ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
