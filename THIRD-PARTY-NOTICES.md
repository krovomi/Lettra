# Avis de tiers

Lettra embarque ou embarquera du contenu tiers sous licence. Chaque composant vendu dans le dépôt
(dossier `third_party/<nom>/`) garde son fichier `LICENSE` et figure ci-dessous. La CI le vérifie
(`build/check-third-party-notices.sh`).

## Paperasse

- Origine : https://github.com/romainsimon/paperasse (skills `comptable`, `controleur-fiscal`, `commissaire-aux-comptes`)
- Licence : MIT ; données sous Licence Ouverte Etalab 2.0
- Statut : pas encore intégré au produit (POC « IA métier » en cours). À l'intégration, copier la notice MIT
  complète ici et le fichier `LICENSE` dans `third_party/paperasse/`, au commit épinglé (annexe B).

## Paquets NuGet

Les licences des paquets NuGet sont celles déclarées par leurs auteurs ; la nomenclature (SBOM CycloneDX)
produite par la CI en donne la liste exacte pour chaque version.
