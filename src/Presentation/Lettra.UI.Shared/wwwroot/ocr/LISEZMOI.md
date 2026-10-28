# Lecture des pièces sur l'appareil

Reconnaissance de texte locale (Tesseract compilé en WebAssembly), utilisée par `lettra-ocr.js` pour lire une photo de
ticket ou de facture avant son dépôt. Les fichiers sont servis par Lettra lui-même : aucune image, aucun texte et
aucun appel ne quitte l'appareil.

| Fichier | Source | Licence |
|---|---|---|
| `tesseract.min.js`, `worker.min.js` | paquet npm `tesseract.js` 7.0.0, dossier `dist/` (commentaire `sourceMappingURL` retiré : les fichiers `.map` ne sont pas livrés ; un correctif dans `worker.min.js`, voir plus bas) | Apache-2.0 |
| `tesseract-core-simd-lstm.wasm.js` | paquet npm `tesseract.js-core` 7.0.0 (moteur LSTM seul, WebAssembly SIMD) | Apache-2.0 |
| `fra.traineddata.bin` | paquet npm `@tesseract.js-data/fra` 1.0.0, fichier `4.0.0_best_int/fra.traineddata.gz`, décompressé et renommé en `.bin` (type de fichier servi sans réglage par le serveur et l'application) ; `lettra-ocr.js` le lit et le remet au moteur | Apache-2.0 (tessdata), paquet MIT |

Empreintes SHA-256 :

```
bf83833fa957ff0076f6aa93f69e3bdf7b014dea829ea0c0d6be6b48a3ceef6d  fra.traineddata.bin
c58b46a4c796c0b8afccf77591d5b875b6896b45d402bbce8caa6f5362447b38  tesseract-core-simd-lstm.wasm.js
6795ee3bde03681e64fa4ddd747c4d9acb388bb9f5e35870fe71dd22feaa1ea1  tesseract.min.js
7c7f228bcbe29804432155cf4093367c12ef632703872e1394508a0c0200544d  worker.min.js
```

Correctif de `worker.min.js` : quand le modèle est remis en octets (`{ code, data }`), l'initialisation du moteur
joignait `t.data` (les octets) au lieu de `t.code` (le nom de la langue) et le moteur cherchait un fichier introuvable.
Le texte `return"string"==typeof t?t:t.data})).join("+")` devient `return"string"==typeof t?t:t.code})).join("+")`.
À refaire à chaque mise à jour tant que la version publiée ne le corrige pas.

Mise à jour : `npm pack tesseract.js@<version> tesseract.js-core@<version> @tesseract.js-data/fra@<version>`, recopier
les mêmes fichiers, retirer la ligne `//# sourceMappingURL=…` des deux fichiers de `dist/`, mettre à jour ce tableau,
les empreintes et `THIRD-PARTY-NOTICES.md`.

Le moteur exige WebAssembly SIMD (Chrome et Edge 91, Firefox 89, Safari 16.4 et leurs WebView) ; sans lui, la lecture
est indisponible et l'utilisateur saisit montant, date et TVA lui-même.
