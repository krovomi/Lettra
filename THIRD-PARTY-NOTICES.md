# Avis de tiers

Lettra embarque ou embarquera du contenu tiers sous licence. Chaque composant vendu dans le dépôt
(dossier `third_party/<nom>/`) garde son fichier `LICENSE` et figure ci-dessous. La CI le vérifie
(`build/check-third-party-notices.sh`).

## Paperasse

- Origine : https://github.com/romainsimon/paperasse (skills `comptable`, `controleur-fiscal`, `commissaire-aux-comptes`)
- Licence : MIT ; données sous Licence Ouverte Etalab 2.0
- Intégré dans `src/Ai/Lettra.Ai.Skills/vendor/paperasse/`, épinglé au commit `f2e15c4` (annexe B §2) : `SKILL.md`
  et fiches de référence des trois skills, embarqués comme ressources et vérifiés par empreinte SHA-256 au démarrage
  (`skillpack.manifest.json`). Les scripts JavaScript ne sont pas embarqués ; leurs règles déterministes sont
  portées en C# avec des tests de parité.

```
MIT License

Copyright (c) 2026 Romain Simon

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Liberation Fonts

- Origine : https://github.com/liberationfonts/liberation-fonts (version 2.1.5)
- Licence : SIL Open Font License 1.1 (texte complet dans `third_party/liberation-fonts/LICENSE`)
- Intégré dans `third_party/liberation-fonts/` : Liberation Sans Regular et Bold, incorporées aux PDF des devis
  et factures par `Lettra.Printing`. L'OFL permet d'incorporer et redistribuer la police avec un logiciel commercial ;
  elle ne s'applique pas aux documents produits avec elle.

## Paquets NuGet

Les licences des paquets NuGet sont celles déclarées par leurs auteurs ; la nomenclature (SBOM CycloneDX)
produite par la CI en donne la liste exacte pour chaque version.

## Whisper (dictée et commandes vocales)

- Whisper.net (paquet NuGet, licence MIT, https://github.com/sandrohanea/whisper.net) embarque whisper.cpp (licence
  MIT, https://github.com/ggml-org/whisper.cpp). Le serveur de la cellule est l'image publique de whisper.cpp.
- Les modèles ggml (convertis des poids Whisper d'OpenAI, licence MIT) ne sont pas dans le dépôt : le poste ou le
  conteneur les télécharge depuis le dépôt public de whisper.cpp et vérifie leur empreinte avant de les charger.
