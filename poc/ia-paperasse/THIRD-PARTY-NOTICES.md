# Notices tierces

## Paperasse

Ce POC embarque une partie des skills Paperasse (`comptable`, `controleur-fiscal`, `commissaire-aux-comptes`) et copie, pour les tests de parité uniquement, les scripts `validate-facture.js`, `calc.js`, `generate-facturx.js` et `test-deterministic-calculations.js`.

- Source : https://github.com/romainsimon/paperasse
- Commit épinglé : `f2e15c43112ddda9b80f53822185a21b6ad3b099` (10/08/2026)
- Licence du code et des skills : MIT, texte complet dans [`src/Lettra.Ai.Skills/vendor/paperasse/LICENSE`](src/Lettra.Ai.Skills/vendor/paperasse/LICENSE)
- Données de référence citées par les skills : Licence Ouverte Etalab 2.0 (sources : DGFiP, data.gouv.fr, INSEE)

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

Avertissement repris dans l'écran « À propos » : les skills ne remplacent pas un expert-comptable inscrit à l'Ordre.

## Anthropic SDK pour C#

Paquet NuGet `Anthropic` (SDK officiel), utilisé par l'adapter `Lettra.Ai.Claude`. Licence MIT.
