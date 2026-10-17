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

## Plus Jakarta Sans

- Origine : https://github.com/tokotype/PlusJakartaSans, via le paquet `@fontsource-variable/plus-jakarta-sans` 5.1.1
- Licence : SIL Open Font License 1.1 (texte complet dans
  `src/Presentation/Lettra.UI.Shared/wwwroot/fonts/OFL-plus-jakarta-sans.txt`)
- Intégré dans `src/Presentation/Lettra.UI.Shared/wwwroot/fonts/` : police variable (sous-ensembles latin et
  latin étendu, WOFF2), police de l'interface (`lettra-tokens.css`). Servie avec l'application, jamais par un CDN.

## Lucide

- Origine : https://lucide.dev, via le paquet `lucide-static` 0.460.0
- Licence : ISC (portions issues de Feather sous MIT)
- Intégré dans `src/Presentation/Lettra.UI.Shared/Components/IconPaths.cs` : tracés SVG des icônes de la navigation
  et du tableau de bord, recopiés tels quels.

```
ISC License

Copyright (c) for portions of Lucide are held by Cole Bemis 2013-2022 as part of Feather (MIT). All other copyright (c) for Lucide are held by Lucide Contributors 2022.

Permission to use, copy, modify, and/or distribute this software for any
purpose with or without fee is hereby granted, provided that the above
copyright notice and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
```

## UI UX Pro Max (skill de conception)

- Origine : https://github.com/nextlevelbuilder/ui-ux-pro-max-skill (version Basic, commit `56ea9cf`)
- Licence : MIT
- Outil de conception, non embarqué : sa recherche a servi à choisir le style, la palette, la typographie et les
  règles d'accessibilité du système de design (`docs/design/systeme-de-design.md`). Aucun fichier du dépôt n'est copié.

```
MIT License

Copyright (c) 2024 Next Level Builder

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

## Paquets NuGet

Les licences des paquets NuGet sont celles déclarées par leurs auteurs ; la nomenclature (SBOM CycloneDX)
produite par la CI en donne la liste exacte pour chaque version.

## Whisper (dictée et commandes vocales)

- Whisper.net (paquet NuGet, licence MIT, https://github.com/sandrohanea/whisper.net) embarque whisper.cpp (licence
  MIT, https://github.com/ggml-org/whisper.cpp). Le serveur de la cellule est whisper.cpp compilé par
  `build/voix/whisper-server`, qui copie sa licence dans l'image.
- Les modèles ggml (convertis des poids Whisper d'OpenAI, licence MIT) ne sont pas dans le dépôt : le poste ou le
  conteneur les télécharge depuis le dépôt public de whisper.cpp et vérifie leur empreinte avant de les charger.

## Codes postaux et communes (aide à la saisie)

- `src/Presentation/Lettra.UI.Shared/wwwroot/data/codes-postaux.txt` est tiré du paquet npm `codes-postaux` 4.1.2
  (licence MIT, https://github.com/BaseAdresseNationale/codes-postaux), lui-même construit sur le Code officiel
  géographique de l'INSEE et le fichier FIMOCT de la DGFiP, publiés sous Licence Ouverte 2.0 (Etalab). Seuls le code
  postal et le nom de la commune sont gardés. La base de La Poste (ODbL) n'est pas utilisée.
