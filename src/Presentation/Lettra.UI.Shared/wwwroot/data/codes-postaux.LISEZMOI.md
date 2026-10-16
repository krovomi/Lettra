# codes-postaux.txt

Une ligne par couple « code postal;commune » (35 683 lignes), pour l'autocomplétion du code postal et de la ville
(vague 5, lane F, `Typing/PostalCodeDirectory.cs`). Chargé à la demande, la première fois qu'un champ d'adresse
est utilisé, puis gardé en mémoire ; fonctionne hors ligne (fichier livré avec l'application).

- Source : paquet npm `codes-postaux` 4.1.2 (Base Adresse Nationale, https://github.com/BaseAdresseNationale/codes-postaux),
  fichier `codes-postaux.json`, réduit aux colonnes `codePostal` et `nomCommune`, doublons retirés, trié.
- Données d'origine : Code officiel géographique de l'INSEE et fichier FIMOCT de la DGFiP, publiés sur data.gouv.fr
  sous Licence Ouverte 2.0 (Etalab). Le paquet lui-même est sous licence MIT.
- Ce jeu évite volontairement la base officielle de La Poste, diffusée sous ODbL (licence virale pour les bases dérivées).
- Mise à jour : `npm pack codes-postaux`, puis le script décrit dans `docs/aide-a-la-saisie.md`.
