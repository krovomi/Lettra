#!/usr/bin/env bash
# Publie l'application WASM en Release puis lance les tests Playwright de la grille.
set -euo pipefail
cd "$(dirname "$0")"
dotnet publish ../../src/Lettra.Poc.Grille.Wasm -c Release -o ../../artifacts/publish
npm ci
npx playwright test "$@"
