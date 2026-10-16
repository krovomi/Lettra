# Serveur whisper.cpp de la cellule (vague 5, lane D) : dictée transcrite sans quitter la cellule (ADR-0012).
# Construit depuis une version épinglée, sans optimisations propres à la machine de construction (GGML_NATIVE=OFF) :
# l'image publique « main » de whisper.cpp s'arrêtait au chargement du modèle sur les machines de la CI.
# Le modèle n'est pas dans l'image : il est téléchargé au premier démarrage dans /models, puis vérifié.
FROM debian:bookworm-slim AS build
RUN apt-get update -q && apt-get install -yq --no-install-recommends build-essential cmake git ca-certificates \
    && rm -rf /var/lib/apt/lists/*
ARG WHISPER_CPP_VERSION=v1.8.7
RUN git clone --depth 1 --branch "$WHISPER_CPP_VERSION" https://github.com/ggml-org/whisper.cpp /src \
    && cmake -S /src -B /build -DCMAKE_BUILD_TYPE=Release -DGGML_NATIVE=OFF -DBUILD_SHARED_LIBS=OFF -DWHISPER_BUILD_TESTS=OFF \
    && cmake --build /build -j"$(nproc)" --target whisper-server

FROM debian:bookworm-slim
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
